using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OrbitalSIP.Models;

namespace OrbitalSIP.Services
{
    public class LeadService : IDisposable
    {
        /// <summary>`error` value the backend sends with the 409 «у клиента уже
        /// есть открытый лид» (LeadService.openLeadConflict on the CRM side).</summary>
        public const string LeadAlreadyOpenError = "LEAD_ALREADY_OPEN";

        private readonly HttpClient _httpClient;

        private static readonly JsonSerializerOptions _readOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>Drops nulls so optional call links never reach the backend's
        /// @IsUUID / @IsString checks as empty values.</summary>
        private static readonly JsonSerializerOptions _writeOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public LeadService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler);
        }

        public async Task<CreateLeadResult> CreateLeadAsync(CreateLeadRequest lead)
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return CreateLeadResult.Failed();

                var url = $"{backendUrl}/api/leads";

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                request.Content = JsonContent.Create(lead);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    return CreateLeadResult.Created();
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    AppLogger.Log("LeadService", $"Create lead failed. Status: {response.StatusCode}. Body: {errorBody}");
                    // Notified for the duplicate too: the error banner is the only
                    // feedback the operator gets today, and the panel that will
                    // render the existing lead instead does not exist yet. Whoever
                    // builds it should drop this notify for the AlreadyOpen branch.
                    HttpErrorNotifier.NotifyHttpError("LeadService", url, response.StatusCode, errorBody);

                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        var duplicate = ParseAlreadyOpenConflict(errorBody);
                        if (duplicate != null)
                            return duplicate;
                    }

                    return CreateLeadResult.Failed();
                }
            }
            catch (Exception ex)
            {
                var details = $"Error creating lead: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("LeadService", details);
                HttpErrorNotifier.NotifyException("LeadService", ex);
                return CreateLeadResult.Failed();
            }
        }

        /// <summary>
        /// Reads a 409 body into <see cref="CreateLeadResult.Duplicate"/>, or returns
        /// null when the body is not the «lead already open» conflict — a 409 can also
        /// come from any other unique constraint, and those must keep taking the
        /// generic failure path (log + error banner).
        ///
        /// `error`/`errors`, not a top-level field: AllExceptionsFilter on the backend
        /// forwards only message/error/errors, so `existingLeadId` lives under `errors`.
        ///
        /// Static and public so the parsing is testable without a live server.
        /// </summary>
        public static CreateLeadResult? ParseAlreadyOpenConflict(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                    return null;

                if (!root.TryGetProperty("error", out var errorEl)
                    || errorEl.ValueKind != JsonValueKind.String
                    || !string.Equals(errorEl.GetString(), LeadAlreadyOpenError, StringComparison.Ordinal))
                    return null;

                int? existingLeadId = null;
                if (root.TryGetProperty("errors", out var errorsEl)
                    && errorsEl.ValueKind == JsonValueKind.Object
                    && errorsEl.TryGetProperty("existingLeadId", out var idEl)
                    && idEl.ValueKind == JsonValueKind.Number
                    && idEl.TryGetInt32(out var id))
                {
                    existingLeadId = id;
                }

                // `message` is string|string[] across the API; this conflict sends a
                // string, and an array is left as «no message» rather than guessed at.
                string? message = root.TryGetProperty("message", out var messageEl)
                                  && messageEl.ValueKind == JsonValueKind.String
                    ? messageEl.GetString()
                    : null;

                return CreateLeadResult.Duplicate(existingLeadId, message);
            }
            catch (JsonException)
            {
                // A 409 whose body is not JSON at all (proxy error page, truncated
                // response). Falls through to the generic failure path.
                return null;
            }
        }

        /// <summary>
        /// GET /api/leads/call-context?phone=… — whether this caller already has an
        /// open lead, who owns it, and what the operator may do.
        ///
        /// Returns a result, not a nullable context, so «lookup failed» stays
        /// distinguishable from «no lead»: see <see cref="LeadCallContextResult"/>.
        /// </summary>
        public async Task<LeadCallContextResult> GetCallContextAsync(string phone)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(phone))
                {
                    AppLogger.Log("LeadService", "Call context aborted: phone is empty.");
                    return LeadCallContextResult.Failed("Номер не определён");
                }

                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                {
                    AppLogger.Log("LeadService", "Call context aborted: BackendUrl or AccessToken is empty.");
                    return LeadCallContextResult.Failed("Нет подключения к CRM");
                }

                var url = $"{backendUrl}/api/leads/call-context?phone={Uri.EscapeDataString(phone)}";
                AppLogger.Log("LeadService", $"Requesting: GET {url}");

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                var response = await _httpClient.SendAsync(request);
                AppLogger.Log("LeadService", $"Call context response status: {(int)response.StatusCode} {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var rawBody = await response.Content.ReadAsStringAsync();

                    try
                    {
                        var context = JsonSerializer.Deserialize<LeadCallContext>(rawBody, _readOptions);
                        if (context == null)
                        {
                            AppLogger.Log("LeadService", "Call context body deserialized to null.");
                            return LeadCallContextResult.Failed("Пустой ответ CRM");
                        }

                        AppLogger.Log("LeadService", $"Call context: leadState={context.LeadState} leadId={context.Lead?.Id.ToString() ?? "-"} owner={context.Owner?.UserId.ToString() ?? "-"}");
                        return LeadCallContextResult.Loaded(context);
                    }
                    catch (JsonException jsonEx)
                    {
                        AppLogger.Log("LeadService", $"Call context JSON deserialization error: {jsonEx.Message}");
                        return LeadCallContextResult.Failed("Некорректный ответ CRM");
                    }
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    AppLogger.Log("LeadService", $"Call context failed. Status: {response.StatusCode}. Body: {errorBody}");
                    HttpErrorNotifier.NotifyHttpError("LeadService", url, response.StatusCode, errorBody);
                    return LeadCallContextResult.Failed($"HTTP {(int)response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                var details = $"Error loading call context: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("LeadService", details);
                HttpErrorNotifier.NotifyException("LeadService", ex);
                return LeadCallContextResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// POST /api/leads/{id}/call-comment — a comment left during a live call,
        /// including on a lead owned by someone else. Returns true on 2xx.
        /// </summary>
        public async Task<bool> AddCallCommentAsync(int leadId, AddCallCommentRequest payload)
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(backendUrl) || string.IsNullOrEmpty(settings.AccessToken))
                    return false;

                var url = $"{backendUrl}/api/leads/{leadId}/call-comment";

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                request.Content = JsonContent.Create(payload, options: _writeOptions);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                    return true;

                var errorBody = await response.Content.ReadAsStringAsync();
                AppLogger.Log("LeadService", $"Add call comment failed. Status: {response.StatusCode}. Body: {errorBody}");
                HttpErrorNotifier.NotifyHttpError("LeadService", url, response.StatusCode, errorBody);
                return false;
            }
            catch (Exception ex)
            {
                var details = $"Error adding call comment: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("LeadService", details);
                HttpErrorNotifier.NotifyException("LeadService", ex);
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
