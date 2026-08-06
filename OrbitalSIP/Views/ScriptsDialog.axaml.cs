using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Avalonia.Threading;

namespace OrbitalSIP.Views
{
    public partial class ScriptsDialog : Window
    {
        /// <summary>
        /// Carries the picked script out of the window. This used to be the result of
        /// ShowDialog&lt;ScriptSelection?&gt;, but a modal dialog disables its owner — the
        /// operator could not hang up or answer the next call while it was up — so the
        /// window is now an ordinary owned one and hands its result over by event.
        /// </summary>
        public event EventHandler<ScriptSelection>? ScriptSelected;

        private TreeView _treeView;
        private List<CallScript> _scripts = new List<CallScript>();
        private CallScript? _selected;
        private string? _categoryFilter;
        private bool _loading;

        public ScriptsDialog()
        {
            InitializeComponent();
            _treeView = this.FindControl<TreeView>("ScriptsTreeView")!;

            var closeBtn = this.FindControl<Button>("CloseBtn");
            if (closeBtn != null) closeBtn.Click += (_, __) => Close();

            var cancelBtn = this.FindControl<Button>("CancelBtn");
            if (cancelBtn != null) cancelBtn.Click += (_, __) => Close();

            var selectBtn = this.FindControl<Button>("SelectBtn");
            if (selectBtn != null) selectBtn.Click += (_, __) => Confirm();

            var retryBtn = this.FindControl<Button>("RetryBtn");
            if (retryBtn != null) retryBtn.Click += (_, __) => _ = LoadScriptsAsync();

            var searchBox = this.FindControl<TextBox>("SearchBox");
            if (searchBox != null)
                searchBox.TextChanged += (s, e) => ApplyFilter();

            _treeView.SelectionChanged += (_, __) => OnTreeSelectionChanged();

            this.EnableDrag(this.FindControl<Border>("HeaderBar"));

            KeyDown += OnDialogKeyDown;
            Opened += (_, __) => searchBox?.Focus();

            _ = LoadScriptsAsync();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        /// <summary>
        /// CenterOwner positions this window off the softphone widget, which operators
        /// park against a screen edge. With SystemDecorations="None" the header bar is
        /// the only drag handle, so a header pushed off-screen leaves the window
        /// unreachable — pull it back inside the working area.
        /// </summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            this.KeepOnScreen();
        }

        private void OnDialogKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        // ── Loading ──────────────────────────────────────────────────────────

        private async Task LoadScriptsAsync()
        {
            _loading = true;
            SetState(loading: true);

            var result = await App.ScriptService.GetScriptsAsync();

            Dispatcher.UIThread.Post(() =>
            {
                _loading = false;

                if (result.Failed)
                {
                    _scripts = new List<CallScript>();
                    _treeView.ItemsSource = null;
                    SetState(error: true);
                    return;
                }

                _scripts = result.Scripts;
                BuildCategoryChips();
                ApplyFilter();
            });
        }

        /// <summary>Shows exactly one of: loading, error, empty, or the tree itself.</summary>
        private void SetState(bool loading = false, bool error = false, bool empty = false)
        {
            var loadingLabel = this.FindControl<TextBlock>("LoadingLabel");
            var emptyLabel = this.FindControl<TextBlock>("EmptyLabel");
            var errorPanel = this.FindControl<StackPanel>("ErrorPanel");
            var treeScroller = this.FindControl<ScrollViewer>("TreeScroller");

            if (loadingLabel != null) loadingLabel.IsVisible = loading;
            if (errorPanel != null) errorPanel.IsVisible = error;
            if (emptyLabel != null) emptyLabel.IsVisible = empty;
            if (treeScroller != null) treeScroller.IsVisible = !loading && !error && !empty;
        }

        // ── Category chips ───────────────────────────────────────────────────

        private void BuildCategoryChips()
        {
            var chips = this.FindControl<WrapPanel>("CategoryChips");
            var scroller = this.FindControl<ScrollViewer>("CategoryScroller");
            if (chips == null) return;

            chips.Children.Clear();

            var categories = new List<ScriptCategory>();
            CollectCategories(_scripts, categories);

            // A single category adds no filtering value — hide the row entirely.
            if (categories.Count < 2)
            {
                if (scroller != null) scroller.IsVisible = false;
                _categoryFilter = null;
                return;
            }

            if (scroller != null) scroller.IsVisible = true;

            chips.Children.Add(BuildChip(I18nService.Instance.Get("AllCategories"), null, null));
            foreach (var cat in categories.OrderBy(c => c.Name))
                chips.Children.Add(BuildChip(cat.Name ?? "", cat.Id, cat.Color));
        }

        private void CollectCategories(IEnumerable<CallScript> nodes, List<ScriptCategory> into)
        {
            foreach (var node in nodes.Where(n => n.IsActive))
            {
                if (node.Category?.Id != null && into.All(c => c.Id != node.Category.Id))
                    into.Add(node.Category);

                if (node.Children != null)
                    CollectCategories(node.Children, into);
            }
        }

        private Button BuildChip(string text, string? categoryId, string? color)
        {
            bool active = _categoryFilter == categoryId;
            var accent = ParseColor(color) ?? Color.Parse("#3B82F6");

            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            if (categoryId != null)
            {
                content.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = new SolidColorBrush(accent),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            content.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });

            var chip = new Button
            {
                Content = content,
                Tag = categoryId,
                Background = new SolidColorBrush(active ? Color.Parse("#1E4270") : Color.Parse("#1E293B")),
                Foreground = new SolidColorBrush(active ? Color.Parse("#E2E8F0") : Color.Parse("#94A3B8")),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(active ? Color.Parse("#3B82F6") : Color.Parse("#334155")),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4),
                // WrapPanel in Avalonia 11.0 has no Spacing — chips carry their own gaps.
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            chip.Click += (_, __) =>
            {
                _categoryFilter = _categoryFilter == categoryId ? null : categoryId;
                BuildCategoryChips();
                ApplyFilter();
            };

            return chip;
        }

        private static Color? ParseColor(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            try { return Color.Parse(value); }
            catch { return null; }
        }

        // ── Filtering ────────────────────────────────────────────────────────

        private void ApplyFilter()
        {
            if (_loading) return;

            var searchBox = this.FindControl<TextBox>("SearchBox");
            var query = searchBox?.Text?.Trim().ToLowerInvariant() ?? "";

            bool narrowed = !string.IsNullOrEmpty(query) || _categoryFilter != null;
            var filtered = narrowed ? FilterNodeList(_scripts, query, _categoryFilter) : _scripts;

            var items = BuildTreeItems(filtered, narrowed);
            _treeView.ItemsSource = items;

            SetState(empty: items.Count == 0);
        }

        private List<CallScript> FilterNodeList(IEnumerable<CallScript> nodes, string query, string? categoryId)
        {
            var result = new List<CallScript>();
            foreach (var node in nodes)
            {
                bool matches = MatchesQuery(node, query) && MatchesCategory(node, categoryId);

                var filteredChildren = new List<CallScript>();
                if (node.Children != null && node.Children.Any())
                {
                    // If the parent matches, include all its active children without further filtering.
                    // Otherwise, apply the filter to the children.
                    if (matches)
                        filteredChildren = node.Children.Where(c => c.IsActive).ToList();
                    else
                        filteredChildren = FilterNodeList(node.Children, query, categoryId);
                }

                if (matches || filteredChildren.Any())
                {
                    var clone = CloneWithChildren(node, filteredChildren);
                    result.Add(clone);
                }
            }
            return result;
        }

        private static bool MatchesQuery(CallScript node, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;

            if (Contains(node.Title, query)) return true;
            if (Contains(node.Description, query)) return true;
            if (node.Steps != null && node.Steps.Any(s => Contains(s, query))) return true;
            if (node.Questions != null && node.Questions.Any(s => Contains(s, query))) return true;
            if (node.Tips != null && node.Tips.Any(s => Contains(s, query))) return true;

            return false;
        }

        private static bool Contains(string? haystack, string needle)
            => haystack != null && haystack.ToLowerInvariant().Contains(needle);

        private static bool MatchesCategory(CallScript node, string? categoryId)
            => categoryId == null || node.CategoryId == categoryId || node.Category?.Id == categoryId;

        /// <summary>Copies a node so the filtered tree can carry a trimmed child list.</summary>
        private static CallScript CloneWithChildren(CallScript source, List<CallScript> children) => new CallScript
        {
            Id = source.Id,
            Title = source.Title,
            Description = source.Description,
            CategoryId = source.CategoryId,
            ParentId = source.ParentId,
            Steps = source.Steps,
            Questions = source.Questions,
            Tips = source.Tips,
            IsActive = source.IsActive,
            Category = source.Category,
            Children = children
        };

        private List<TreeViewItem> BuildTreeItems(IEnumerable<CallScript> scripts, bool expand)
        {
            var items = new List<TreeViewItem>();
            foreach (var script in scripts.Where(s => s.IsActive).OrderBy(s => s.Title))
            {
                var item = new TreeViewItem { Header = BuildNodeHeader(script), Tag = script, IsExpanded = expand };
                item.DoubleTapped += (_, e) =>
                {
                    e.Handled = true;
                    if (_selected != null) Confirm();
                };

                if (script.Children != null && script.Children.Any())
                    item.ItemsSource = BuildTreeItems(script.Children, expand);

                items.Add(item);

                if (_selected != null && script.Id == _selected.Id)
                    item.IsSelected = true;
            }
            return items;
        }

        private Control BuildNodeHeader(CallScript script)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

            var accent = ParseColor(script.Category?.Color);
            if (accent != null)
            {
                panel.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = new SolidColorBrush(accent.Value),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            panel.Children.Add(new TextBlock
            {
                Text = script.Title ?? "",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }

        // ── Details ──────────────────────────────────────────────────────────

        private void OnTreeSelectionChanged()
        {
            if (_treeView.SelectedItem is TreeViewItem item && item.Tag is CallScript script)
            {
                _selected = script;
                ShowDetails(script);
            }
        }

        private void ShowDetails(CallScript script)
        {
            var placeholder = this.FindControl<TextBlock>("DetailPlaceholder");
            var scroller = this.FindControl<ScrollViewer>("DetailScroller");
            if (placeholder != null) placeholder.IsVisible = false;
            if (scroller != null) scroller.IsVisible = true;

            var title = this.FindControl<TextBlock>("DetailTitle");
            if (title != null) title.Text = script.Title ?? "";

            var chip = this.FindControl<Border>("DetailCategoryChip");
            var chipText = this.FindControl<TextBlock>("DetailCategoryText");
            var categoryName = script.Category?.Name;
            if (chip != null && chipText != null)
            {
                chip.IsVisible = !string.IsNullOrWhiteSpace(categoryName);
                chipText.Text = categoryName ?? "";
                chip.Background = new SolidColorBrush(ParseColor(script.Category?.Color) ?? Color.Parse("#3B82F6"));
            }

            var description = this.FindControl<TextBlock>("DetailDescription");
            if (description != null)
            {
                description.Text = script.Description ?? "";
                description.IsVisible = !string.IsNullOrWhiteSpace(script.Description);
            }

            FillSection("StepsSection", "StepsList", script.Steps, numbered: true);
            FillSection("QuestionsSection", "QuestionsList", script.Questions, numbered: false);
            FillSection("TipsSection", "TipsList", script.Tips, numbered: false);

            var selectBtn = this.FindControl<Button>("SelectBtn");
            if (selectBtn != null) selectBtn.IsEnabled = true;

            scroller?.ScrollToHome();
        }

        private void FillSection(string sectionName, string listName, List<string>? values, bool numbered)
        {
            var section = this.FindControl<StackPanel>(sectionName);
            var list = this.FindControl<StackPanel>(listName);
            if (section == null || list == null) return;

            list.Children.Clear();

            var entries = values?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList() ?? new List<string>();
            section.IsVisible = entries.Count > 0;

            for (int i = 0; i < entries.Count; i++)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };

                var marker = new TextBlock
                {
                    Text = numbered ? $"{i + 1}." : "•",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.Parse("#3B82F6")),
                    Margin = new Thickness(0, 0, 8, 0),
                    MinWidth = numbered ? 18 : 10
                };

                var text = new TextBlock
                {
                    Text = entries[i],
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.Parse("#CBD5E1")),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(text, 1);

                row.Children.Add(marker);
                row.Children.Add(text);
                list.Children.Add(row);
            }
        }

        // ── Confirm ──────────────────────────────────────────────────────────

        private void Confirm()
        {
            if (_selected == null)
            {
                Close();
                return;
            }

            var commentBox = this.FindControl<TextBox>("CommentBox");

            // Hand the selection over before closing: the Closed handler is what releases
            // the launcher's slot, so raising afterwards would race a re-open.
            ScriptSelected?.Invoke(this, new ScriptSelection
            {
                Script = _selected,
                Note = commentBox?.Text?.Trim() ?? ""
            });
            Close();
        }
    }
}
