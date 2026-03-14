using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using System.Diagnostics;
using OrbitalSIP.Services;
using OrbitalSIP.Models;
using System;
using Avalonia;

namespace OrbitalSIP.Views
{
    public partial class WidgetView : UserControl
    {
        private DispatcherTimer? _pulseTimer;
        private Stopwatch?       _stopwatch;
        private Ellipse?         _strokeRing;
        private Ellipse?         _statusDot;
        private Action<StatusState>? _queueStateChangedHandler;
        private Action<RegistrationState>? _statusChangedHandler;
        private Action<string>? _registrationErrorHandler;

        public WidgetView()
        {
            InitializeComponent();
            _strokeRing = this.FindControl<Ellipse>("StrokeRing");
            _statusDot  = this.FindControl<Ellipse>("StatusDot");
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            _stopwatch  = Stopwatch.StartNew();
            if (_pulseTimer == null)
            {
                _pulseTimer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(30), DispatcherPriority.Normal, OnPulseTick);
            }
            _pulseTimer.Start();

            var sip = App.SipService;
            var statusSvc = App.StatusService;

            if (_statusChangedHandler == null)
            {
                _statusChangedHandler = state =>
                    Dispatcher.UIThread.InvokeAsync(() => UpdateStatus(state));
                sip.RegistrationStatusChanged += _statusChangedHandler;
            }

            if (_registrationErrorHandler == null)
            {
                _registrationErrorHandler = reason =>
                    Dispatcher.UIThread.InvokeAsync(() => UpdateStatusTip(sip.RegistrationStatus, reason));
                sip.RegistrationError += _registrationErrorHandler;
            }

            if (_queueStateChangedHandler == null)
            {
                _queueStateChangedHandler = state => Dispatcher.UIThread.InvokeAsync(() => UpdateStatus(sip.RegistrationStatus));
                statusSvc.StateChanged += _queueStateChangedHandler;
            }

            UpdateStatus(sip.RegistrationStatus);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            _pulseTimer?.Stop();
            _stopwatch?.Stop();

            if (_statusChangedHandler != null)
            {
                App.SipService.RegistrationStatusChanged -= _statusChangedHandler;
                _statusChangedHandler = null;
            }

            if (_registrationErrorHandler != null)
            {
                App.SipService.RegistrationError -= _registrationErrorHandler;
                _registrationErrorHandler = null;
            }

            if (_queueStateChangedHandler != null)
            {
                App.StatusService.StateChanged -= _queueStateChangedHandler;
                _queueStateChangedHandler = null;
            }
        }

        private void OnPulseTick(object? sender, EventArgs e)
        {
            if (_stopwatch == null || _strokeRing == null) return;

            var pulse = (Math.Sin(_stopwatch.Elapsed.TotalSeconds * 2.2) + 1.0) / 2.0;
            _strokeRing.Opacity        = 0.35 + pulse * 0.65;
            _strokeRing.StrokeThickness = 4.0 + pulse * 4.0;
        }

        private void UpdateStatus(RegistrationState state)
        {
            if (_statusDot == null || _strokeRing == null) return;

            Color color;
            Color pulseColorStart;
            Color pulseColorEnd;
            string label;

            var queueState = App.StatusService.CurrentState;
            bool isQueuePaused = queueState != null && queueState.Paused;

            switch (state)
            {
                case RegistrationState.Registered:
                    if (isQueuePaused)
                    {
                        color = Color.Parse("#F59E0B"); // Amber
                        pulseColorStart = Color.Parse("#FBBF24");
                        pulseColorEnd   = Color.Parse("#D97706");
                        label = queueState?.ReasonPaused ?? "Paused";
                    }
                    else
                    {
                        color = Color.Parse("#10B981"); // Emerald
                        pulseColorStart = Color.Parse("#17E0A0");
                        pulseColorEnd   = Color.Parse("#00BFA5");
                        label = "Registered";
                    }
                    break;
                case RegistrationState.Failed:
                    color = Color.Parse("#EF4444"); // Red
                    pulseColorStart = Color.Parse("#F87171");
                    pulseColorEnd   = Color.Parse("#DC2626");
                    label = App.SipService.LastRegistrationError;
                    break;
                case RegistrationState.Paused:
                    color = Color.Parse("#F59E0B"); // Amber
                    pulseColorStart = Color.Parse("#FBBF24");
                    pulseColorEnd   = Color.Parse("#D97706");
                    label = "Paused";
                    break;
                case RegistrationState.Unregistered:
                default:
                    color = Color.Parse("#EF4444"); // Red for Offline
                    pulseColorStart = Color.Parse("#F87171");
                    pulseColorEnd   = Color.Parse("#DC2626");
                    label = "Offline";
                    break;
            }

            _statusDot.Fill = new SolidColorBrush(color);

            _strokeRing.Stroke = new LinearGradientBrush
            {
                StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
                EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(pulseColorStart, 0),
                    new GradientStop(pulseColorEnd, 1)
                }
            };

            UpdateStatusTip(state, label);
        }

        private void UpdateStatusTip(RegistrationState state, string message)
        {
            var tip = this.FindControl<Avalonia.Controls.TextBlock>("StatusTip");
            if (tip == null) return;

            if (string.IsNullOrWhiteSpace(message))
            {
                var queueState = App.StatusService.CurrentState;
                if (state == RegistrationState.Registered && queueState != null && queueState.Paused)
                {
                    tip.Text = queueState.ReasonPaused ?? "Paused";
                }
                else
                {
                    tip.Text = state switch
                    {
                        RegistrationState.Registered => "Registered",
                        RegistrationState.Failed => "Registration Failed",
                        RegistrationState.Paused => "Paused",
                        _ => "Offline"
                    };
                }
            }
            else
            {
                tip.Text = message;
            }
        }
    }
}
