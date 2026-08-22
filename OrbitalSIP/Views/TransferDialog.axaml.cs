using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// The transfer target picker: tabs between operators and queues, a search box,
    /// the list itself, and a manual-entry fallback. Only reports what the operator
    /// picked — see <see cref="TransferSelected"/> — and never performs the transfer
    /// itself; that is TransferWindowLauncher's caller's job, the same split
    /// ScriptsDialog uses for ScriptSelected.
    ///
    /// Relocated near-verbatim from ActiveCallView, which drove the same tabs,
    /// search and list inline until measurement showed the call panel had no room
    /// left even before a single row loaded (see TransferWindowLauncher's
    /// docblock). Only the owner changed here — not the content.
    /// </summary>
    public partial class TransferDialog : Window
    {
        /// <summary>
        /// Carries the picked target — a list row or the manual-entry box — out of
        /// the window, the way <see cref="ScriptsDialog.ScriptSelected"/> does for
        /// the script list.
        /// </summary>
        public event EventHandler<Models.TransferRequest>? TransferSelected;

        private readonly string _callerNumber;

        /// <summary>
        /// State for the operator/queue picker, fed to TransferTargetsPresenter.
        /// Loaded as soon as the window opens — unlike the inline panel, which
        /// deferred the fetch until the operator opened it, this window only ever
        /// exists because the operator already asked to transfer.
        /// </summary>
        private readonly TransferService _transferService = new();
        private Models.TransferTargets? _transferTargets;
        private bool _transferLoading;
        private bool _transferForbidden;
        private string? _transferError;
        private bool _transferQueuesTab;

        public TransferDialog() : this("") { }

        public TransferDialog(string callerNumber)
        {
            _callerNumber = callerNumber;
            InitializeComponent();

            var closeBtn = this.FindControl<Button>("CloseBtn");
            if (closeBtn != null) closeBtn.Click += (_, __) => Close();

            var transferConfirm = this.FindControl<Button>("TransferConfirmBtn");
            if (transferConfirm != null)
                transferConfirm.Click += (_, __) => ConfirmManualEntry();

            var transferTabOperators = this.FindControl<Button>("TransferTabOperatorsBtn");
            if (transferTabOperators != null)
                transferTabOperators.Click += (_, __) => SelectTransferTab(queues: false);

            var transferTabQueues = this.FindControl<Button>("TransferTabQueuesBtn");
            if (transferTabQueues != null)
                transferTabQueues.Click += (_, __) => SelectTransferTab(queues: true);

            var transferSearch = this.FindControl<TextBox>("TransferSearchBox");
            if (transferSearch != null)
                transferSearch.TextChanged += (_, __) => RenderTransferList();

            var transferRetry = this.FindControl<Button>("TransferRetryBtn");
            if (transferRetry != null)
                transferRetry.Click += SafeHandler.Click("Transfer", LoadTransferTargetsAsync);

            this.EnableDrag(this.FindControl<Border>("HeaderBar"));

            KeyDown += OnDialogKeyDown;
            Opened += (_, __) => transferSearch?.Focus();

            // ActiveCallView never disposed this service; this window now owns the
            // instance for its whole life, so it is the one that must let it go —
            // same spot SmsComposeDialog releases its own owned session, on Closed.
            Closed += (_, __) => _transferService.Dispose();

            _ = LoadTransferTargetsAsync();
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

        private async Task LoadTransferTargetsAsync()
        {
            _transferLoading = true;
            _transferError = null;
            _transferForbidden = false;
            RenderTransferList();

            var response = await _transferService.GetTargetsAsync();

            _transferLoading = false;
            _transferTargets = response.Targets;
            _transferForbidden = response.Forbidden;
            _transferError = response.Error;
            RenderTransferList();
        }

        /// <summary>Paints the tab toggle. Colours are the two states already declared
        /// in TransferDialog.axaml for TransferTabOperatorsBtn/TransferTabQueuesBtn —
        /// swapped here, not invented.</summary>
        private void SelectTransferTab(bool queues)
        {
            _transferQueuesTab = queues;

            var operatorsBtn = this.FindControl<Button>("TransferTabOperatorsBtn");
            if (operatorsBtn != null)
            {
                operatorsBtn.Background = new SolidColorBrush(Color.Parse(queues ? "#152132" : "#1E4270"));
                operatorsBtn.Foreground = new SolidColorBrush(Color.Parse(queues ? "#7B92AA" : "#DDE7F3"));
            }

            var queuesBtn = this.FindControl<Button>("TransferTabQueuesBtn");
            if (queuesBtn != null)
            {
                queuesBtn.Background = new SolidColorBrush(Color.Parse(queues ? "#1E4270" : "#152132"));
                queuesBtn.Foreground = new SolidColorBrush(Color.Parse(queues ? "#DDE7F3" : "#7B92AA"));
            }

            RenderTransferList();
        }

        /// <summary>
        /// Paints the target list from TransferTargetsPresenter's decision. All of
        /// the branching over what to show lives there — same split as
        /// LeadCallPanelPresenter/ApplyLeadPanelState — this only draws the state it
        /// is handed.
        /// </summary>
        private void RenderTransferList()
        {
            var list = this.FindControl<StackPanel>("TransferList");
            if (list == null) return;

            var status = this.FindControl<TextBlock>("TransferStatusLabel");
            var tabs = this.FindControl<Grid>("TransferTabs");
            var search = this.FindControl<TextBox>("TransferSearchBox");
            var i18n = I18nService.Instance;

            var state = TransferTargetsPresenter.SelectState(
                _transferTargets, _transferLoading, _transferError, _transferForbidden);

            list.Children.Clear();
            SetVisible<Button>("TransferRetryBtn", state == TransferPanelState.Error);

            // Forbidden (403/404) means no list will ever come back, so tabs and
            // search would only advertise a picker that can never fill — both fall
            // back to plain manual entry, same as a load Error.
            var listUsable = state is TransferPanelState.Ready or TransferPanelState.Empty or TransferPanelState.Loading;
            if (tabs != null) tabs.IsVisible = listUsable;
            if (search != null) search.IsVisible = state == TransferPanelState.Ready;

            if (status != null)
            {
                status.Text = state switch
                {
                    TransferPanelState.Loading => i18n.Get("ScriptsLoading"),
                    TransferPanelState.Error => i18n.Get("TransferLoadFailed"),
                    TransferPanelState.Empty => i18n.Get("TransferNoTargets"),
                    _ => string.Empty,
                };
                status.IsVisible = status.Text.Length > 0;
            }

            if (state != TransferPanelState.Ready) return;

            var query = search?.Text ?? string.Empty;
            if (_transferQueuesTab)
            {
                foreach (var queue in TransferTargetsPresenter.FilterQueues(_transferTargets, query))
                    list.Children.Add(BuildQueueRow(queue));
            }
            else
            {
                // The widget's own SIP identity, so FilterOperators can exclude this
                // operator from their own transfer list — same identifier space as
                // TransferOperatorTarget.Extension (both trace back to the backend's
                // sipEndpointId; see LoginView.axaml.cs, which fills this in from
                // GET /api/auth/sip-credentials).
                var ownExtension = App.SipService?.CurrentSettings?.Username;
                foreach (var op in TransferTargetsPresenter.FilterOperators(_transferTargets, query, ownExtension))
                    list.Children.Add(BuildOperatorRow(op));
            }
        }

        private void SetVisible<T>(string name, bool visible) where T : Control
        {
            var control = this.FindControl<T>(name);
            if (control != null) control.IsVisible = visible;
        }

        private Button BuildOperatorRow(Models.TransferOperatorTarget target)
        {
            var button = new Button
            {
                Background = new SolidColorBrush(Color.Parse("#152132")),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock
                {
                    Text = $"{target.FullName}  ·  {target.Extension}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#F8FAFC")),
                },
            };
            button.Click += (_, __) => RequestTransfer(Models.TransferTargetKind.Extension, target.Extension);
            return button;
        }

        private Button BuildQueueRow(Models.TransferQueueTarget target)
        {
            var disabledKey = TransferTargetsPresenter.QueueDisabledKey(target.DisabledReason);
            var enabled = disabledKey == null;
            var i18n = I18nService.Instance;

            var caption = enabled
                ? (string.IsNullOrEmpty(target.Description) ? target.Name : $"{target.Name}  ·  {target.Description}")
                : $"{target.Name}  ·  {i18n.Get(disabledKey!)}";

            var button = new Button
            {
                Background = new SolidColorBrush(Color.Parse("#152132")),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6),
                IsEnabled = enabled,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock
                {
                    Text = caption,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse(enabled ? "#F8FAFC" : "#5B6D82")),
                },
            };
            // A disabled row (unsafe queue name) stays in the list as a greyed-out
            // explanation rather than a hidden or a live tap target — see
            // TransferTargetsPresenter.FilterQueues.
            if (enabled)
                button.Click += (_, __) => RequestTransfer(Models.TransferTargetKind.Queue, target.Name);
            return button;
        }

        // ── Confirm ──────────────────────────────────────────────────────────

        /// <summary>
        /// Raises TransferSelected for a row the operator picked from the list, then
        /// closes — the picker's job ends the moment a target is chosen, same as
        /// ScriptsDialog.Confirm(). Performing the transfer is not this window's
        /// job; the launcher's caller decides what happens with the event.
        /// </summary>
        private void RequestTransfer(Models.TransferTargetKind kind, string value)
        {
            AppLogger.Log("Transfer", $"Transfer requested: {kind} -> {value}");

            // Handed over before Close(): the launcher's Closed handler is what
            // releases its slot and lets a new window open, so raising afterwards
            // would race a re-open.
            TransferSelected?.Invoke(this, new Models.TransferRequest(kind, value, _callerNumber));
            Close();
        }

        private void ConfirmManualEntry()
        {
            var box = this.FindControl<TextBox>("TransferNumberBox");
            var number = box?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(number)) return;

            // Manual entry is always an extension: there is no free-text way to name
            // a queue safely, and this box existed long before queues did.
            TransferSelected?.Invoke(this, new Models.TransferRequest(
                Models.TransferTargetKind.Extension, number, _callerNumber));
            Close();
        }
    }
}
