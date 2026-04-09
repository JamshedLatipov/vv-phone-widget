using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    public partial class LoginView : UserControl
    {
        private static readonly HttpClient _httpClient;

        static LoginView()
        {
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _httpClient = new HttpClient(handler);
        }

        public LoginView()
        {
            InitializeComponent();
            WireButtons();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void WireButtons()
        {
            var loginBtn = this.FindControl<Button>("LoginBtn");
            if (loginBtn != null)
                loginBtn.Click += async (_, __) => await AttemptLogin();

            var settingsBtn = this.FindControl<Button>("SettingsBtn");
            if (settingsBtn != null)
                settingsBtn.Click += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);

            var userBox = this.FindControl<TextBox>("UsernameBox");
            if (userBox != null)
                userBox.KeyDown += async (s, e) => { if (e.Key == Key.Enter) await AttemptLogin(); };

            var passBox = this.FindControl<TextBox>("PasswordBox");
            if (passBox != null)
                passBox.KeyDown += async (s, e) => { if (e.Key == Key.Enter) await AttemptLogin(); };
        }

        private async Task AttemptLogin()
        {
            var username = this.FindControl<TextBox>("UsernameBox")?.Text?.Trim();
            var password = this.FindControl<TextBox>("PasswordBox")?.Text?.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowError("Please enter both username and password.");
                return;
            }

            var settings = SipSettings.Load();
            if (string.IsNullOrEmpty(settings.BackendUrl))
            {
                ShowError("Backend URL not configured in settings.");
                return;
            }
            if (string.IsNullOrEmpty(settings.Server))
            {
                ShowError("SIP Server not configured in settings.");
                return;
            }

            SetBusy(true);
            try
            {
                var baseUrl = settings.BackendUrl.TrimEnd('/');
                var response = await _httpClient.PostAsJsonAsync($"{baseUrl}/api/auth/login", new
                {
                    username = username,
                    password = password
                });

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                    if (result?.Sip != null)
                    {
                        settings.Username = result.Sip.Username;
                        settings.Password = result.Sip.Password;

                        if (!string.IsNullOrEmpty(result.AccessToken))
                        {
                            settings.AccessToken = result.AccessToken;
                            settings.DecodedToken = JwtDecoder.Decode(result.AccessToken);
                        }

                        App.SipService.Start(settings);
                        _ = App.StatusService.SetStateAsync(true, "offline");
                        OnLoginSuccess?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        ShowError("Invalid response from server.");
                    }
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? "Unknown error" : response.ReasonPhrase;
                    ShowError($"{Services.I18nService.Instance.Get("ErrorFailed")}: {reason}");
                    AppLogger.Log("LoginView", $"Login failed. Status: {response.StatusCode}. Body: {errorBody}");
                    HttpErrorNotifier.NotifyHttpError("LoginView", $"{baseUrl}/api/auth/login", response.StatusCode, errorBody);
                }
            }
            catch (Exception ex)
            {
                var details = $"Error connecting to backend: {ex.GetType().Name}: {ex.Message}";
                if (ex.InnerException != null)
                    details += $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                details += $" | StackTrace: {ex.StackTrace}";
                AppLogger.Log("LoginView", details);
                ShowError($"Error connecting to backend: {ex.Message}");
                HttpErrorNotifier.NotifyException("LoginView", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ShowError(string message)
        {
            var errorLabel = this.FindControl<TextBlock>("ErrorLabel");
            if (errorLabel != null) { errorLabel.Text = message; errorLabel.IsVisible = true; }
        }

        private void SetBusy(bool busy)
        {
            var loginBtn = this.FindControl<Button>("LoginBtn");
            if (loginBtn != null)
            {
                loginBtn.IsEnabled = !busy;
                loginBtn.Content = busy ? Services.I18nService.Instance.Get("SigningIn") : Services.I18nService.Instance.Get("SignIn");
            }
        }

        public event EventHandler? OnLoginSuccess;
        public event EventHandler? OnSettingsRequested;

        private class LoginResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = "";

            [JsonPropertyName("sip")]
            public SipCredentials? Sip { get; set; }
        }

        private class SipCredentials
        {
            [JsonPropertyName("username")]
            public string Username { get; set; } = "";

            [JsonPropertyName("password")]
            public string Password { get; set; } = "";
        }
    }
}
