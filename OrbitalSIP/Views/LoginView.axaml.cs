using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Controls;
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
            // The backend is often accessed via IP and may use self-signed certificates.
            // For this specific internal use case, we ignore certificate validation errors.
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
        }

        private async Task AttemptLogin()
        {
            var username = this.FindControl<TextBox>("UsernameBox")?.Text?.Trim();
            var password = this.FindControl<TextBox>("PasswordBox")?.Text?.Trim();
            var errorLabel = this.FindControl<TextBlock>("ErrorLabel");

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
                    if (result != null)
                    {
                        // Update SipSettings with credentials but DON'T SAVE them (persistence is handled by JsonIgnore)
                        settings.Username = result.SipLogin;
                        settings.Password = result.SipPassword;

                        // We also need to update the global SipService settings
                        App.SipService.Start(settings);

                        OnLoginSuccess?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        ShowError("Invalid response from server.");
                    }
                }
                else
                {
                    ShowError($"Login failed: {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Error connecting to backend: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ShowError(string message)
        {
            var errorLabel = this.FindControl<TextBlock>("ErrorLabel");
            if (errorLabel != null)
            {
                errorLabel.Text = message;
                errorLabel.IsVisible = true;
            }
        }

        private void SetBusy(bool busy)
        {
            var loginBtn = this.FindControl<Button>("LoginBtn");
            if (loginBtn != null)
            {
                loginBtn.IsEnabled = !busy;
                loginBtn.Content = busy ? "Signing In..." : "Sign In";
            }
        }

        public event EventHandler? OnLoginSuccess;
        public event EventHandler? OnSettingsRequested;

        private class LoginResponse
        {
            [JsonPropertyName("sipLogin")]
            public string SipLogin { get; set; } = "";

            [JsonPropertyName("sipPassword")]
            public string SipPassword { get; set; } = "";
        }
    }
}
