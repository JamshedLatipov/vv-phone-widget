using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// Collects task fields (title, description, priority, due, type) for a task
    /// created off an active call. Raises <see cref="TaskConfirmed"/> on save
    /// (without the call anchor / assignee — the caller fills those in), and nothing
    /// at all on cancel.
    /// </summary>
    public partial class TaskDialog : Window
    {
        /// <summary>
        /// Carries the filled-in request out of the window. This used to be the result
        /// of ShowDialog&lt;CreateTaskRequest?&gt;, but a modal dialog disables its owner —
        /// the operator could not hang up or answer the next call while it was up — so
        /// the window is now an ordinary owned one and hands its result over by event.
        /// </summary>
        public event EventHandler<CreateTaskRequest>? TaskConfirmed;

        private TextBox _titleBox = null!;
        private TextBox _descriptionBox = null!;
        private ComboBox _priorityBox = null!;
        private ComboBox _dueBox = null!;
        private ComboBox _typeBox = null!;
        private TextBlock _errorLabel = null!;

        public TaskDialog() : this("") { }

        public TaskDialog(string callerNumber)
        {
            InitializeComponent();

            _titleBox = this.FindControl<TextBox>("TitleBox")!;
            _descriptionBox = this.FindControl<TextBox>("DescriptionBox")!;
            _priorityBox = this.FindControl<ComboBox>("PriorityBox")!;
            _dueBox = this.FindControl<ComboBox>("DueBox")!;
            _typeBox = this.FindControl<ComboBox>("TypeBox")!;
            _errorLabel = this.FindControl<TextBlock>("ErrorLabel")!;

            var closeBtn = this.FindControl<Button>("CloseBtn");
            if (closeBtn != null) closeBtn.Click += (_, __) => Close();

            var cancelBtn = this.FindControl<Button>("CancelBtn");
            if (cancelBtn != null) cancelBtn.Click += (_, __) => Close();

            var saveBtn = this.FindControl<Button>("SaveBtn");
            if (saveBtn != null) saveBtn.Click += (_, __) => Confirm();

            BuildPriorityOptions();
            BuildDueOptions();
            BuildTypePlaceholder();

            var prefix = I18nService.Instance.Get("TaskCallbackTitle");
            _titleBox.Text = string.IsNullOrWhiteSpace(callerNumber) ? prefix : $"{prefix} {callerNumber}".Trim();

            this.EnableDrag(this.FindControl<Border>("HeaderBar"));

            KeyDown += OnDialogKeyDown;
            Opened += (_, __) =>
            {
                _titleBox.Focus();
                _titleBox.SelectAll();
            };

            _ = LoadTypesAsync();
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

        // ── Option lists ─────────────────────────────────────────────────────

        private void BuildPriorityOptions()
        {
            var i18n = I18nService.Instance;
            _priorityBox.Items.Add(NewItem(i18n.Get("PriorityLow"), "low"));
            _priorityBox.Items.Add(NewItem(i18n.Get("PriorityMedium"), "medium"));
            _priorityBox.Items.Add(NewItem(i18n.Get("PriorityHigh"), "high"));
            _priorityBox.Items.Add(NewItem(i18n.Get("PriorityUrgent"), "urgent"));
            _priorityBox.SelectedIndex = 1; // medium
        }

        private void BuildDueOptions()
        {
            var i18n = I18nService.Instance;
            _dueBox.Items.Add(NewItem(i18n.Get("DueNone"), "none"));
            _dueBox.Items.Add(NewItem(i18n.Get("DueIn30m"), "30m"));
            _dueBox.Items.Add(NewItem(i18n.Get("DueIn1h"), "1h"));
            _dueBox.Items.Add(NewItem(i18n.Get("DueTomorrow"), "tomorrow"));
            _dueBox.SelectedIndex = 2; // in 1 hour
        }

        private void BuildTypePlaceholder()
        {
            _typeBox.Items.Clear();
            _typeBox.Items.Add(NewItem(I18nService.Instance.Get("TaskTypeNone"), null));
            _typeBox.SelectedIndex = 0;
            _typeBox.IsEnabled = false; // re-enabled after types load
        }

        private async Task LoadTypesAsync()
        {
            var types = await App.TaskService.GetTaskTypesAsync();

            Dispatcher.UIThread.Post(() =>
            {
                _typeBox.Items.Clear();
                _typeBox.Items.Add(NewItem(I18nService.Instance.Get("TaskTypeNone"), null));
                foreach (var t in types)
                    _typeBox.Items.Add(NewItem(t.Name, t.Id));
                _typeBox.SelectedIndex = 0;
                _typeBox.IsEnabled = true;
            });
        }

        private static ComboBoxItem NewItem(string text, object? tag)
            => new ComboBoxItem { Content = text, Tag = tag };

        // ── Confirm ──────────────────────────────────────────────────────────

        private void Confirm()
        {
            var title = _titleBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(title))
            {
                _errorLabel.Text = I18nService.Instance.Get("TaskTitleRequired");
                _errorLabel.IsVisible = true;
                _titleBox.Focus();
                return;
            }

            var description = _descriptionBox.Text?.Trim();

            var request = new CreateTaskRequest
            {
                Title = title,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                Priority = SelectedTag(_priorityBox) as string,
                DueDate = ResolveDueDate(SelectedTag(_dueBox) as string),
                TaskTypeId = SelectedTag(_typeBox) as int?,
            };

            // Hand the request over before closing: the Closed handler is what releases
            // the launcher's slot, so raising afterwards would race a re-open.
            TaskConfirmed?.Invoke(this, request);
            Close();
        }

        private static object? SelectedTag(ComboBox box)
            => (box.SelectedItem as ComboBoxItem)?.Tag;

        /// <summary>Turns a due-option token into an ISO-8601 string, or null for "no deadline".</summary>
        private static string? ResolveDueDate(string? token)
        {
            var now = DateTime.Now;
            DateTime due;
            switch (token)
            {
                case "30m": due = now.AddMinutes(30); break;
                case "1h": due = now.AddHours(1); break;
                case "tomorrow": due = DateTime.Today.AddDays(1).AddHours(9); break;
                default: return null; // "none" or unknown
            }
            return due.ToString("o");
        }
    }
}
