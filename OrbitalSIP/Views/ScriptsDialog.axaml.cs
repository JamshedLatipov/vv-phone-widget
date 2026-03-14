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

            _ = LoadScriptsAsync();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private async Task LoadScriptsAsync()
        {
            _scripts = await App.ScriptService.GetScriptsAsync();
            Dispatcher.UIThread.Post(() =>
            {
                var items = BuildTreeItems(_scripts);
                _treeView.ItemsSource = items;
            });
        }

        private List<TreeViewItem> BuildTreeItems(IEnumerable<CallScript> scripts)
        {
            var items = new List<TreeViewItem>();
            foreach (var script in scripts.Where(s => s.IsActive).OrderBy(s => s.Title))
            {
                var item = new TreeViewItem { Header = script.Title, Tag = script };
                if (script.Children != null && script.Children.Any())
                {
                    item.ItemsSource = BuildTreeItems(script.Children);
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
