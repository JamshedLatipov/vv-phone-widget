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
            _httpClient = BackendHttp.Client;
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

                using var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    return CreateLeadResult.Created();
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();

                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        var duplicate = ParseAlreadyOpenConflict(errorBody);
                        if (duplicate != null)
                        {
                            // No error banner: the panel renders the existing lead, which
                            // is a better answer than a raw-JSON toast. Logged here rather
                            // than through NotifyHttpError, which this path skips.
                            AppLogger.Log("LeadService",
                                $"Create lead: 409, caller already has an open lead (id={duplicate.ExistingLeadId?.ToString() ?? "?"}).");
                            return duplicate;
                        }
                    }

                    // Logs the URL and body itself; no separate line here.
                    HttpErrorNotifier.NotifyHttpError("LeadService", url, response.StatusCode, errorBody);
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
        /// forwards only message/error/errors, so the payload lives under `errors`.
        ///
        /// Walks JsonDocument by hand rather than deserializing into a type with
        /// _readOptions, deliberately: a field of an unexpected JSON kind (say a
        /// stringified `existingLeadId`) must degrade to null while the conflict is
        /// still reported as a conflict, whereas Deserialize would throw on it and
        /// lose the AlreadyOpen signal entirely — i.e. the widget would fall back to
        /// a generic error on the one case this method exists to recognise.
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
                string? existingLeadName = null;
                string? existingLeadStatus = null;

                if (root.TryGetProperty("errors", out var errorsEl)
                    && errorsEl.ValueKind == JsonValueKind.Object)
                {
                    if (errorsEl.TryGetProperty("existingLeadId", out var idEl)
                        && idEl.ValueKind == JsonValueKind.Number
                        && idEl.TryGetInt32(out var id))
                    {
                        existingLeadId = id;
                    }

                    existingLeadName = ReadOptionalString(errorsEl, "existingLeadName");
                    existingLeadStatus = ReadOptionalString(errorsEl, "status");
                }

                // `message` is string|string[] across the API; this conflict sends a
                // string, and an array is left as «no message» rather than guessed at.
                string? message = ReadOptionalString(root, "message");

                return CreateLeadResult.Duplicate(
                    existingLeadId: existingLeadId,
                    existingLeadName: existingLeadName,
                    existingLeadStatus: existingLeadStatus,
                    message: message);
            }
            catch (JsonException)
            {
                // A 409 whose body is not JSON at all (proxy error page, truncated
                // response). Falls through to the generic failure path.
                return null;
            }
        }

        /// <summary>Reads a property only if it is actually a JSON string; any other
        /// kind (null, number, object) reads as absent rather than as a value.</summary>
        private static string? ReadOptionalString(JsonElement parent, string propertyName) =>
            parent.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;

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
                // Masked: this ran on every answered call and wrote the caller's full
                // number to a plain-text file, several times per call.
                AppLogger.Log("LeadService", $"Requesting call context for {LogRedaction.Phone(phone)}.");

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);

                using var response = await _httpClient.SendAsync(request);

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
                    AppLogger.Log("LeadService", $"Call context failed. Status: {(int)response.StatusCode}. Body: {errorBody}");
                    // Deliberately NOT notified. This lookup fires automatically on
                    // every answered call, so a standing failure — an operator whose
                    // role lacks `lead-call:read`, say — would raise a banner on every
                    // single call. The panel's «Не удалось проверить активный лид»
                    // with a retry is the user-facing signal; the log is the rest.
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
                // Same reasoning as the non-2xx branch above: a dead network would
                // otherwise banner once per call. The panel already says so.
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

                using var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                    return true;

                var errorBody = await response.Content.ReadAsStringAsync();
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

        /// <summary>The client is shared, so there is nothing here to release.</summary>
        public void Dispose()
        {
        }
    }
}
