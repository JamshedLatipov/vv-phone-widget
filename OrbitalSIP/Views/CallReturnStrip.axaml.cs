using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// "A call is running — go back to it." The only thing tying the operator to the
    /// conversation while they are looking at another tab.
    ///
    /// The timer counts from SipService.ActiveCallStartedAt rather than from a mark of its
    /// own: a private counter would drift from the one on the call screen by exactly the
    /// time the operator spent getting to this tab.
    /// </summary>
    public partial class CallReturnStrip : UserControl
    {
        private readonly DispatcherTimer _tick;
        private DateTime? _startedAt;

        public event EventHandler? OnReturnRequested;

        public CallReturnStrip()
        {
            InitializeComponent();

            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (_, __) => Redraw();

            if (this.FindControl<Border>("Root") is { } root)
                root.PointerPressed += (_, __) => OnReturnRequested?.Invoke(this, EventArgs.Empty);

            // A timer that outlived its screen would hold a reference to it for the rest
            // of the process.
            DetachedFromVisualTree += (_, __) => Stop();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public void Show(string caller, DateTime? startedAt)
        {
            _startedAt = startedAt;
            if (this.FindControl<TextBlock>("Caller") is { } c) c.Text = caller;
            Redraw();
            _tick.Start();
        }

        public void Stop() => _tick.Stop();

        private void Redraw()
        {
            if (this.FindControl<TextBlock>("Elapsed") is not { } label) return;

            var elapsed = _startedAt.HasValue ? DateTime.Now - _startedAt.Value : TimeSpan.Zero;
            label.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
        }
    }
}
