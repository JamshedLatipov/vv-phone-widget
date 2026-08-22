using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// The panel's chrome: the top bar, the return-to-call strip, the content and the
    /// bottom bar.
    ///
    /// Until now the top bar and the bottom bar were each duplicated in the markup of
    /// five screens, and the strip would have been a third thing copied five times —
    /// five places that would each have to be taught when to show it and when to hide it,
    /// and five places to get that wrong in.
    ///
    /// The XAML names are prefixed and the properties below are not, on purpose. Avalonia's
    /// name generator emits an internal field per Name, and a property here of the same name
    /// collides with it. Those fields are dead weight in this project anyway — every view
    /// declares its own InitializeComponent(), so the generated one that would populate them
    /// never runs, which is why the whole codebase reaches for FindControl instead.
    /// </summary>
    public partial class PanelShellView : UserControl
    {
        public PanelShellView() => InitializeComponent();

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public object? Body
        {
            get => this.FindControl<ContentControl>("ShellBody")?.Content;
            set { var host = this.FindControl<ContentControl>("ShellBody"); if (host != null) host.Content = value; }
        }

        public TopBarControl?    TopBar => this.FindControl<TopBarControl>("ShellTopBar");
        public BottomNavControl? Nav    => this.FindControl<BottomNavControl>("ShellNav");

        public void SetReturnStrip(bool visible, string caller, DateTime? startedAt)
        {
            var strip = this.FindControl<CallReturnStrip>("ShellStrip");
            if (strip == null) return;

            strip.IsVisible = visible;
            if (visible) strip.Show(caller, startedAt);
            else         strip.Stop();
        }
    }
}
