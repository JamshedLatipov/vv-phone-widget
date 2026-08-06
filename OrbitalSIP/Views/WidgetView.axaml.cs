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
        /// <summary>
        /// Pulse cadence. This is the app's resting state — the widget sits on screen all day, the
        /// window is composited with transparency, so every tick here is an alpha blend against the
        /// desktop whether or not anything is happening. The ring breathes on a ~2.9 s sine, which
        /// 20 samples a second render indistinguishably from the 60 this used to run at.
        /// </summary>
        private static readonly TimeSpan PulseInterval = TimeSpan.FromMilliseconds(50);

        /// <summary>Below these the change would not survive rounding to a pixel or an 8-bit alpha.</summary>
        private const double OpacityEpsilon   = 1.0 / 255.0;
        private const double ThicknessEpsilon = 0.05;

        /// <summary>
        /// How the ring sits when registration is healthy and the queue is not paused: solid and
        /// thin. Nothing animates in that state, which is where the app spends the shift, so the
        /// pulse now reads as "look at me" rather than as decoration that happens to always run.
        /// </summary>
        private const double RestingOpacity   = 1.0;
        private const double RestingThickness = 4.0;

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
                    PulseInterval, DispatcherPriority.Render, OnPulseTick);
            }

            // Left stopped on purpose — the UpdateStatus call at the end of this method decides
            // whether the current registration state is worth animating for.

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

        /// <summary>
        /// Runs the pulse only while something is actually wrong. Registered and unpaused is the
        /// resting state, and there the timer does not run at all.
        /// </summary>
        private void SetPulseEnabled(bool enabled)
        {
            if (_pulseTimer == null || _strokeRing == null) return;

            if (enabled)
            {
                if (!_pulseTimer.IsEnabled)
                {
                    // Restart so the pulse always begins at the same point of the sine rather than
                    // wherever the clock happened to be.
                    _stopwatch?.Restart();
                    _pulseTimer.Start();
                }
                return;
            }

            _pulseTimer.Stop();
            _strokeRing.Opacity         = RestingOpacity;
            _strokeRing.StrokeThickness = RestingThickness;
        }

        private void OnPulseTick(object? sender, EventArgs e)
        {
            if (_stopwatch == null || _strokeRing == null) return;

            var pulse = (Math.Sin(_stopwatch.Elapsed.TotalSeconds * 2.2) + 1.0) / 2.0;
            var opacity   = 0.35 + pulse * 0.65;
            var thickness = 4.0 + pulse * 4.0;

            // Near the top and bottom of the sine the value barely moves, and assigning it anyway
            // costs a visual invalidation and a repaint of a transparent window for nothing.
            if (Math.Abs(_strokeRing.Opacity - opacity) >= OpacityEpsilon)
                _strokeRing.Opacity = opacity;

            if (Math.Abs(_strokeRing.StrokeThickness - thickness) >= ThicknessEpsilon)
                _strokeRing.StrokeThickness = thickness;
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
            bool isSupervisorPaused = queueState != null && queueState.IsSupervisorPaused;

            SetPulseEnabled(Services.WidgetPulse.ShouldPulse(state, queueState));

            switch (state)
            {
                case RegistrationState.Registered:
                    if (isQueuePaused)
                    {
                        color = Color.Parse("#F59E0B"); // Amber
                        pulseColorStart = Color.Parse("#FBBF24");
                        pulseColorEnd   = Color.Parse("#D97706");
                        label = isSupervisorPaused
                            ? Services.I18nService.Instance.Get("SupervisorPaused")
                            : PresenceLabel(queueState?.ReasonPaused);
                    }
                    else
                    {
                        color = Color.Parse("#10B981"); // Emerald
                        pulseColorStart = Color.Parse("#17E0A0");
                        pulseColorEnd   = Color.Parse("#00BFA5");
                        label = Services.I18nService.Instance.Get("Registered");
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
                    label = Services.I18nService.Instance.Get("ErrorPaused");
                    break;
                case RegistrationState.Unregistered:
                default:
                    color = Color.Parse("#EF4444"); // Red for Offline
                    pulseColorStart = Color.Parse("#F87171");
                    pulseColorEnd   = Color.Parse("#DC2626");
                    label = Services.I18nService.Instance.Get("Offline");
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

        private static string PresenceLabel(string? status)
        {
            var i18n = Services.I18nService.Instance;
            return (status?.ToLower() ?? "") switch
            {
                "break"    => i18n.Get("Break"),
                "meeting"  => i18n.Get("Meeting"),
                "training" => i18n.Get("Training"),
                "dnd"      => i18n.Get("Dnd"),
                "pause"    => i18n.Get("Pause"),
                "offline"  => i18n.Get("Offline"),
                _          => i18n.Get("ErrorPaused")
            };
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
                    tip.Text = queueState.IsSupervisorPaused
                        ? Services.I18nService.Instance.Get("SupervisorPaused")
                        : PresenceLabel(queueState.ReasonPaused);
                }
                else
                {
                    tip.Text = state switch
                    {
                        RegistrationState.Registered => Services.I18nService.Instance.Get("Registered"),
                        RegistrationState.Failed => Services.I18nService.Instance.Get("ErrorFailed"),
                        RegistrationState.Paused => Services.I18nService.Instance.Get("ErrorPaused"),
                        _ => Services.I18nService.Instance.Get("Offline")
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
