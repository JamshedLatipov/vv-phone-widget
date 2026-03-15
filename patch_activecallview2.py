import sys

filename = 'OrbitalSIP/Views/ActiveCallView.axaml.cs'
with open(filename, 'r') as f:
    content = f.read()

new_method = '''        private async Task CreateLeadAsync()
        {
            Console.WriteLine("[CreateLeadAsync] Button clicked");
            var callerNumber = this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? string.Empty;

            Console.WriteLine($"[CreateLeadAsync] Caller number: {callerNumber}");
            if (string.IsNullOrWhiteSpace(callerNumber))
            {
                Console.WriteLine("[CreateLeadAsync] Caller number is empty, aborting.");
                return;
            }

            var request = new Models.CreateLeadRequest
            {
                Name = callerNumber,
                Phone = callerNumber,
                Status = "new",
                Source = "call",
                Priority = "low"
            };

            Console.WriteLine("[CreateLeadAsync] Sending request to LeadService...");
            bool success = await App.LeadService.CreateLeadAsync(request);
            Console.WriteLine($"[CreateLeadAsync] Request success: {success}");

            if (success)
            {
                var leadBtn = this.FindControl<Button>("CreateLeadBtn");
                if (leadBtn != null)
                {
                    // Find the icon within the StackPanel inside the Button
                    var stackPanel = leadBtn.Content as StackPanel;
                    if (stackPanel != null)
                    {
                        foreach (var child in stackPanel.Children)
                        {
                            if (child is Material.Icons.Avalonia.MaterialIcon icon)
                            {
                                var originalKind = icon.Kind;
                                icon.Kind = Material.Icons.MaterialIconKind.Check;
                                await Task.Delay(1200);
                                icon.Kind = originalKind;
                                break;
                            }
                        }
                    }
                }
            }
        }'''

# Replace the existing method
import re
content = re.sub(r'        private async Task CreateLeadAsync\(\)\s*\{.*?(?=        private async Task ShowScriptsDialog\(\))', new_method + '\n\n', content, flags=re.DOTALL)

with open(filename, 'w') as f:
    f.write(content)
