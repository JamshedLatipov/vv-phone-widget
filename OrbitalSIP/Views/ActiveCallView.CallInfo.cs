using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    public partial class ActiveCallView
    {
        private bool _callInfoLoaded;

        // ── Call Info Panel ───────────────────────────────────────────
        private void ToggleCallInfoPanel()
        {
            var panel = this.FindControl<Border>("CallInfoPanel");
            if (panel == null) return;

            if (panel.IsVisible)
            {
                HideCallInfoPanel();
            }
            else
            {
                panel.IsVisible = true;
                if (!_callInfoLoaded)
                    _ = LoadCallInfoAsync();
            }
        }

        private void HideCallInfoPanel()
        {
            var panel = this.FindControl<Border>("CallInfoPanel");
            if (panel != null) panel.IsVisible = false;
        }

        private async Task LoadCallInfoAsync()
        {
            var callerNumber = this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? string.Empty;
            AppLogger.Log("CallInfo", $"Loading call info for '{callerNumber}'");

            var response = await App.CallInfoService.GetCallInfoAsync(callerNumber);
            AppLogger.Log("CallInfo", $"Response: {(response == null ? "null" : $"{response.Sections.Count} sections")}");

            _callInfoLoaded = true;

            await Dispatcher.UIThread.InvokeAsync(() => RenderCallInfo(response));
        }

        private void RenderCallInfo(CallInfoResponse? response)
        {
            var loadingLabel  = this.FindControl<TextBlock>("CallInfoLoadingLabel");
            var emptyPanel    = this.FindControl<StackPanel>("CallInfoEmptyPanel");
            var sectionsPanel = this.FindControl<StackPanel>("CallInfoSectionsPanel");

            if (loadingLabel  != null) loadingLabel.IsVisible  = false;
            if (emptyPanel    == null || sectionsPanel == null) return;

            if (response == null || response.Sections.Count == 0)
            {
                emptyPanel.IsVisible = true;
                return;
            }

            bool hasAnyData = false;
            foreach (var section in response.Sections)
            {
                if (section.Ui == null || section.Ui.Fields.Count == 0) continue;

                var rows = new System.Collections.Generic.List<(string Label, string Value)>();
                foreach (var field in section.Ui.Fields)
                {
                    var value = CallInfoService.ResolveField(section.Data, field.Key);
                    AppLogger.Log("CallInfo", $"  Field '{field.Key}' => '{value ?? "(null)"}'" );
                    if (!string.IsNullOrWhiteSpace(value))
                        rows.Add((field.Label, value));
                }

                if (rows.Count == 0) continue;
                hasAnyData = true;
                sectionsPanel.Children.Add(BuildCallInfoSectionCard(section.Ui.Title, rows));
            }

            if (hasAnyData)
                sectionsPanel.IsVisible = true;
            else
                emptyPanel.IsVisible = true;
        }

        private Border BuildCallInfoSectionCard(
            string title,
            System.Collections.Generic.List<(string Label, string Value)> rows)
        {
            var contentStack = new StackPanel { Spacing = 10 };

            contentStack.Children.Add(new TextBlock
            {
                Text          = title,
                FontSize      = 11,
                FontWeight    = FontWeight.Bold,
                Foreground    = new SolidColorBrush(Color.Parse("#60A5FA")),
                LetterSpacing = 0.8
            });

            contentStack.Children.Add(new Border
            {
                Height     = 1,
                Background = new SolidColorBrush(Color.Parse("#243348")),
                Margin     = new Avalonia.Thickness(0, 0, 0, 2)
            });

            foreach (var (label, value) in rows)
            {
                var copyIcon = new MaterialIcon
                {
                    Kind       = MaterialIconKind.ContentCopy,
                    Width      = 13,
                    Height     = 13,
                    Foreground = new SolidColorBrush(Color.Parse("#60A5FA"))
                };

                var copyBtn = new Button
                {
                    Content         = copyIcon,
                    Background      = Brushes.Transparent,
                    BorderThickness = new Avalonia.Thickness(0),
                    Padding         = new Avalonia.Thickness(4, 0, 0, 0),
                    Opacity         = 0,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var valueRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };

                var valueBlock = new TextBlock
                {
                    Text         = value,
                    FontSize     = 12,
                    FontWeight   = FontWeight.Medium,
                    Foreground   = new SolidColorBrush(Color.Parse("#F8FAFC")),
                    TextWrapping = TextWrapping.Wrap
                };

                valueRow.Children.Add(valueBlock);
                valueRow.Children.Add(copyBtn);

                var rowStack = new StackPanel { Spacing = 1 };

                var labelBlock = new TextBlock
                {
                    Text         = label,
                    FontSize     = 10,
                    Foreground   = new SolidColorBrush(Color.Parse("#6E859D")),
                    TextWrapping = TextWrapping.Wrap
                };

                rowStack.Children.Add(labelBlock);
                rowStack.Children.Add(valueRow);

                rowStack.PointerEntered += (_, __) => copyBtn.Opacity = 1;
                rowStack.PointerExited  += (_, __) => copyBtn.Opacity = 0;

                var capturedValue = value;
                var capturedIcon  = copyIcon;
                copyBtn.Click += async (_, __) =>
                {
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel?.Clipboard == null) return;
                    await topLevel.Clipboard.SetTextAsync(capturedValue);
                    capturedIcon.Kind = MaterialIconKind.Check;
                    await Task.Delay(1000);
                    capturedIcon.Kind = MaterialIconKind.ContentCopy;
                };

                contentStack.Children.Add(rowStack);
            }

            return new Border
            {
                Background   = new SolidColorBrush(Color.Parse("#1E293B")),
                CornerRadius = new Avalonia.CornerRadius(12),
                Padding      = new Avalonia.Thickness(16, 14),
                Child        = contentStack
            };
        }
    }
}
