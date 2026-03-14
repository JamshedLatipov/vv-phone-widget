import re

with open('OrbitalSIP/Views/ExpandedView.axaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

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

# Replace the closing brace of ExpandedView with the methods and then the closing brace
content = content.replace("        // ── Events ────────────────────────────────────────────────────", f"{methods_injection}\n        // ── Events ────────────────────────────────────────────────────")

with open('OrbitalSIP/Views/ExpandedView.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)
