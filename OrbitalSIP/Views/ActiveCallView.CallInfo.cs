using Avalonia.Input.Platform;
using System.Threading.Tasks;
using Avalonia.Controls;
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

            sectionsPanel.Children.Clear();

            // Handles both section shapes: `details` (object + ui.fields) and
            // `table` (array + ui.columns — Кредиты / Счета / Депозиты).
            var sections = CallInfoPresenter.BuildSections(response);
            AppLogger.Log("CallInfo", $"Renderable sections: {sections.Count}");

            if (sections.Count == 0)
            {
                emptyPanel.IsVisible = true;
                return;
            }

            foreach (var section in sections)
            {
                AppLogger.Log("CallInfo", $"  Section '{section.Title}': {section.Records.Count} record(s)");
                sectionsPanel.Children.Add(BuildCallInfoSectionCard(section));
            }

            emptyPanel.IsVisible    = false;
            sectionsPanel.IsVisible = true;
        }

        private Border BuildCallInfoSectionCard(CallInfoSectionView section)
        {
            var contentStack = new StackPanel { Spacing = 10 };

            contentStack.Children.Add(new TextBlock
            {
                Text          = section.Title,
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

            for (var i = 0; i < section.Records.Count; i++)
            {
                var record = section.Records[i];

                // Separator between records of a multi-row section (2nd loan
                // onwards), so the operator can tell one contract from the next.
                if (i > 0)
                {
                    contentStack.Children.Add(new Border
                    {
                        Height     = 1,
                        Background = new SolidColorBrush(Color.Parse("#1B2839")),
                        Margin     = new Avalonia.Thickness(0, 4, 0, 2)
                    });
                }

                if (!string.IsNullOrWhiteSpace(record.Heading))
                {
                    contentStack.Children.Add(new TextBlock
                    {
                        Text       = record.Heading,
                        FontSize   = 10,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.Parse("#8FA6BE"))
                    });
                }

                foreach (var row in record.Rows)
                    contentStack.Children.Add(BuildCallInfoRow(row.Label, row.Value));
            }

            return new Border
            {
                Background   = new SolidColorBrush(Color.Parse("#1E293B")),
                CornerRadius = new Avalonia.CornerRadius(12),
                Padding      = new Avalonia.Thickness(16, 14),
                Child        = contentStack
            };
        }

        private StackPanel BuildCallInfoRow(string label, string value)
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
                Content           = copyIcon,
                Background        = Brushes.Transparent,
                BorderThickness   = new Avalonia.Thickness(0),
                Padding           = new Avalonia.Thickness(4, 0, 0, 0),
                Focusable         = false,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var valueBlock = new TextBlock
            {
                Text         = value,
                FontSize     = 12,
                FontWeight   = FontWeight.Medium,
                Foreground   = new SolidColorBrush(Color.Parse("#F8FAFC")),
                TextWrapping = TextWrapping.Wrap
            };

            var valueRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };
            Grid.SetColumn(valueBlock, 0);
            Grid.SetColumn(copyBtn,    1);
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

            return rowStack;
        }
    }
}
