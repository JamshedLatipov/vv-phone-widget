using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OrbitalSIP.Models;
using OrbitalSIP.Services;
using Avalonia.Threading;

namespace OrbitalSIP.Views
{
    public partial class ScriptsDialog : Window
    {
        private TreeView _treeView;
        private List<CallScript> _scripts = new List<CallScript>();

        public ScriptsDialog()
        {
            InitializeComponent();
            _treeView = this.FindControl<TreeView>("ScriptsTreeView")!;

            var closeBtn = this.FindControl<Button>("CloseBtn");
            if (closeBtn != null) closeBtn.Click += (_, __) => Close(null);

            var selectBtn = this.FindControl<Button>("SelectBtn");
            if (selectBtn != null) selectBtn.Click += (_, __) => OnSelect();

            var searchBox = this.FindControl<TextBox>("SearchBox");
            if (searchBox != null)
                searchBox.TextChanged += (s, e) => FilterScripts(searchBox.Text);

            _ = LoadScriptsAsync();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private async Task LoadScriptsAsync()
        {
            _scripts = await App.ScriptService.GetScriptsAsync();
            Dispatcher.UIThread.Post(() =>
            {
                FilterScripts("");
            });
        }

        private void FilterScripts(string? query)
        {
            query = query?.Trim().ToLowerInvariant() ?? "";
            var filtered = string.IsNullOrEmpty(query) ? _scripts : FilterNodeList(_scripts, query);

            var items = BuildTreeItems(filtered, !string.IsNullOrEmpty(query));
            _treeView.ItemsSource = items;
        }

        private List<CallScript> FilterNodeList(IEnumerable<CallScript> nodes, string query)
        {
            var result = new List<CallScript>();
            foreach (var node in nodes)
            {
                bool matches = node.Title != null && node.Title.ToLowerInvariant().Contains(query);

                var filteredChildren = new List<CallScript>();
                if (node.Children != null && node.Children.Any())
                {
                    filteredChildren = FilterNodeList(node.Children, query);
                }

                if (matches || filteredChildren.Any())
                {
                    var clone = System.Text.Json.JsonSerializer.Deserialize<CallScript>(System.Text.Json.JsonSerializer.Serialize(node));
                    if (clone != null)
                    {
                        clone.Children = filteredChildren;
                        result.Add(clone);
                    }
                }
            }
            return result;
        }

        private List<TreeViewItem> BuildTreeItems(IEnumerable<CallScript> scripts, bool expand)
        {
            var items = new List<TreeViewItem>();
            foreach (var script in scripts.Where(s => s.IsActive).OrderBy(s => s.Title))
            {
                var item = new TreeViewItem { Header = script.Title, Tag = script, IsExpanded = expand };
                if (script.Children != null && script.Children.Any())
                {
                    item.ItemsSource = BuildTreeItems(script.Children, expand);
                }
                items.Add(item);
            }
            return items;
        }

        private void OnSelect()
        {
            if (_treeView.SelectedItem is TreeViewItem item && item.Tag is CallScript script)
            {
                Close(script);
            }
            else
            {
                Close(null);
            }
        }
    }
}
