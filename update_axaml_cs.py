import re

with open('OrbitalSIP/Views/ExpandedView.axaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Add new usings if not present
if "using OrbitalSIP.Models;" not in content:
    content = content.replace("using OrbitalSIP.Services;", "using OrbitalSIP.Services;\nusing OrbitalSIP.Models;\nusing OrbitalSIP.ViewModels;\nusing System.Collections.ObjectModel;\nusing System.Net.Http;\nusing System.Net.Http.Json;\nusing Avalonia.Interactivity;")

# Add timer and collection properties
properties_injection = """
        private DispatcherTimer? _cdrTimer;
        private static readonly HttpClient _httpClient;
        public ObservableCollection<CdrItemViewModel> CdrItems { get; } = new ObservableCollection<CdrItemViewModel>();

        static ExpandedView()
        {
            var handler = new HttpClientHandler();
#if DEBUG
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
#endif
            _httpClient = new HttpClient(handler);
        }
"""

if "_cdrTimer" not in content:
    # Insert inside class definition, after InitializeComponent();
    content = content.replace("public ExpandedView()", f"{properties_injection}\n        public ExpandedView()")

# Set DataContext, Timer inside constructor
constructor_additions = """
            DataContext = this;

            var refreshBtn = this.FindControl<Button>("RefreshCdrBtn");
            if (refreshBtn != null) refreshBtn.Click += async (_, __) => await LoadCallHistoryAsync();

            _cdrTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(2)
            };
            _cdrTimer.Tick += async (_, __) => await LoadCallHistoryAsync();
            _cdrTimer.Start();

            _ = LoadCallHistoryAsync();
"""
if "DataContext = this;" not in content:
    content = content.replace("WireButtons();", f"WireButtons();{constructor_additions}")

# Add OnDetachedFromVisualTree for timer cleanup
if "OnDetachedFromVisualTree" not in content:
    content = content.replace("private void InitializeComponent()", """
        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _cdrTimer?.Stop();
        }

        private void InitializeComponent()""")


# Add LoadCallHistoryAsync method
methods_injection = """
        private async Task LoadCallHistoryAsync()
        {
            try
            {
                var settings = App.SipService?.CurrentSettings ?? SipSettings.Load();
                var operatorId = settings.DecodedToken?.Operator?.Username ?? settings.Username;
                var backendUrl = settings.BackendUrl?.TrimEnd('/');

                if (string.IsNullOrEmpty(operatorId) || string.IsNullOrEmpty(backendUrl))
                    return;

                var startOfToday = DateTime.UtcNow.Date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var endOfToday = DateTime.UtcNow.Date.AddDays(1).AddTicks(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                var url = $"{backendUrl}/api/cdr?page=1&limit=20&fromDate={startOfToday}&toDate={endOfToday}&operatorId={Uri.EscapeDataString(operatorId)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(settings.AccessToken))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
                }

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<CdrResponse>();
                    if (data?.Data != null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            CdrItems.Clear();
                            foreach(var item in data.Data)
                            {
                                CdrItems.Add(new CdrItemViewModel(item, operatorId));
                            }

                            var ic = this.FindControl<ItemsControl>("CdrItemsControl");
                            if (ic != null)
                            {
                                ic.ItemsSource = CdrItems;
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ExpandedView] Error loading call history: {ex.Message}");
            }
        }

        private void OnCdrCallClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string num)
            {
                var d = this.FindControl<TextBlock>("DisplayText");
                if (d != null)
                {
                    d.Text = num;
                }
                OutgoingCallRequested?.Invoke(this, num);
            }
        }
"""
if "LoadCallHistoryAsync" not in content:
    content = content.replace("private void Bind(string name, Action action)", f"{methods_injection}\n        private void Bind(string name, Action action)")


with open('OrbitalSIP/Views/ExpandedView.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)
