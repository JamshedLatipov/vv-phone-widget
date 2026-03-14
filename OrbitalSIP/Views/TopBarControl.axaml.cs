using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia;
using Avalonia.Threading;
using OrbitalSIP.Services;
using OrbitalSIP.Models;

namespace OrbitalSIP.Views
{
    public partial class TopBarControl : UserControl
    {
        private Action<RegistrationState>? _statusChangedHandler;
        private Action<StatusState>? _queueStateChangedHandler;

        public event EventHandler? OnMinimizeRequested;
        public event EventHandler? OnAvatarClicked;

        public TopBarControl()
        {
            InitializeComponent();
            WireButtons();

            var sip = App.SipService;
            var statusSvc = App.StatusService;

            _statusChangedHandler = state => Dispatcher.UIThread.InvokeAsync(() => UpdateStatus(state));
            sip.RegistrationStatusChanged += _statusChangedHandler;

            _queueStateChangedHandler = state => Dispatcher.UIThread.InvokeAsync(() => UpdateStatus(sip.RegistrationStatus));
            statusSvc.StateChanged += _queueStateChangedHandler;

            UpdateStatus(sip.RegistrationStatus);

            var settings = sip.CurrentSettings;
            var usernameLabel = this.FindControl<TextBlock>("UsernameLabel");
            if (usernameLabel != null && settings != null)
            {
                var displayUser = settings.DecodedToken?.Username ?? settings.Username;
                if (!string.IsNullOrWhiteSpace(displayUser))
                {
                    usernameLabel.Text = displayUser;
                }
            }
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void UpdateStatus(RegistrationState state)
        {
            var dot = this.FindControl<Ellipse>("StatusDot");
            var lbl = this.FindControl<TextBlock>("StatusLabel");
            if (dot == null || lbl == null) return;

            var queueState = App.StatusService.CurrentState;
            bool isQueuePaused = queueState != null && queueState.Paused;

            switch (state)
            {
                case RegistrationState.Registered:
                    if (isQueuePaused)
                    {
                        dot.Fill = new SolidColorBrush(Color.Parse("#F59E0B")); // Amber
                        string reason = queueState?.ReasonPaused ?? "Paused";
                        lbl.Text = char.ToUpper(reason[0]) + reason.Substring(1); // Capitalize first letter
                    }
                    else
                    {
                        dot.Fill = new SolidColorBrush(Color.Parse("#10B981")); // Emerald
                        lbl.Text = "Available";
                    }
                    break;
                case RegistrationState.Failed:
                    dot.Fill = new SolidColorBrush(Color.Parse("#EF4444")); // Red
                    lbl.Text = "Connection Error";
                    break;
                case RegistrationState.Paused:
                    dot.Fill = new SolidColorBrush(Color.Parse("#F59E0B")); // Amber
                    lbl.Text = "Paused";
                    break;
                case RegistrationState.Unregistered:
                default:
                    dot.Fill = new SolidColorBrush(Color.Parse("#EF4444")); // Red for Offline
                    lbl.Text = "Offline";
                    break;
            }
        }

        public void SetTitle(string title)
        {
             var lbl = this.FindControl<TextBlock>("UsernameLabel");
             if (lbl != null)
             {
                 lbl.Text = title;
             }
        }

        private void WireButtons()
        {
            var minBtn = this.FindControl<Button>("MinimizeBtn");
            if (minBtn != null)
            {
                minBtn.Click += (_, __) => OnMinimizeRequested?.Invoke(this, EventArgs.Empty);
            }

            var avatarBtn = this.FindControl<Button>("AvatarBtn");
            if (avatarBtn != null)
            {
                avatarBtn.Click += (_, __) => OnAvatarClicked?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (_statusChangedHandler != null)
            {
                App.SipService.RegistrationStatusChanged -= _statusChangedHandler;
                _statusChangedHandler = null;
            }
            if (_queueStateChangedHandler != null)
            {
                App.StatusService.StateChanged -= _queueStateChangedHandler;
                _queueStateChangedHandler = null;
            }
        }
    }
}
