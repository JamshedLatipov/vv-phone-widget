using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using Material.Icons;
using Material.Icons.Avalonia;
using OrbitalSIP.Models;
using OrbitalSIP.Services;

namespace OrbitalSIP.Views
{
    /// <summary>
    /// The bottom tab bar. It reports which tab was pressed and draws the state it is
    /// given; it does not know what any tab leads to.
    ///
    /// It used to raise four separate OnXxxRequested events, and each screen wired up
    /// whichever subset its author remembered. Settings never wired Recents, the call
    /// screen never wired the dialer, and Contacts was wired by nobody at all. A press is
    /// one event now, and where it leads is ShellRouter's answer — a table nobody can
    /// partially implement, reached through the single Dispatch in MainWindow.
    /// </summary>
    public partial class BottomNavControl : UserControl
    {
        /// <summary>Raised on every tab press, including a press on the active tab.</summary>
        public event EventHandler<NavTab>? TabSelected;

        private readonly Dictionary<NavTab, Button> _buttons = new();
        private NavTab? _activeTab;
        private bool _loginMode;

        public BottomNavControl()
        {
            InitializeComponent();
            WireButtons();

            // Show dot immediately if the silent startup check already found an update.
            if (App.Updater.HasUpdate)
                ShowUpdateDot(true);

            // Show dot if the update is discovered while this control is on screen.
            App.Updater.UpdateAvailable += OnUpdateAvailable;

            // The Dialer tooltip is the one text on this bar that is assigned rather than
            // bound, so it is the one that does not refresh itself. Settings changes the
            // language in place, with this bar on screen, so that is not hypothetical.
            I18nService.Instance.LanguageChanged += OnLanguageChanged;
        }

        /// <summary>
        /// Releases the two subscriptions above. App.Updater and I18nService.Instance both
        /// live for the whole process, and MainWindow builds a fresh view — and therefore a
        /// fresh one of these — on every screen change, so without this each navigation
        /// pinned an entire control tree to a static event for the rest of the shift. That
        /// is hundreds over a shift, and the active-call panel among them holds the caller's
        /// lead, name and number in its own fields, which ForgetCachedCall does not reach.
        /// </summary>
        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            App.Updater.UpdateAvailable -= OnUpdateAvailable;
            I18nService.Instance.LanguageChanged -= OnLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>
        /// The animated screen swap parents a view into OverlayHost and then moves it to
        /// Host — a detach/attach pair — so the detach above is not always the end of this
        /// control's life. Re-subscribe, and keep it idempotent: -= on an absent handler is
        /// a no-op, but += twice would fire twice.
        /// </summary>
        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            App.Updater.UpdateAvailable -= OnUpdateAvailable;
            App.Updater.UpdateAvailable += OnUpdateAvailable;
            I18nService.Instance.LanguageChanged -= OnLanguageChanged;
            I18nService.Instance.LanguageChanged += OnLanguageChanged;
            if (App.Updater.HasUpdate) ShowUpdateDot(true);
        }

        private void OnUpdateAvailable()
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ShowUpdateDot(true));
        }

        /// <summary>Re-renders the one tooltip on this bar that markup cannot keep current.</summary>
        private void OnLanguageChanged() =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(RefreshTabVisuals);

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void WireButtons()
        {
            Register(NavTab.Dialer,   "DialerBtn");
            Register(NavTab.Recents,  "RecentsBtn");
            Register(NavTab.Tasks,    "TasksBtn");
            Register(NavTab.Settings, "SettingsBtn");

            void Register(NavTab tab, string name)
            {
                var button = this.FindControl<Button>(name);
                if (button == null) return;
                _buttons[tab] = button;
                button.Click += (_, __) => TabSelected?.Invoke(this, tab);
            }
        }

        /// <summary>
        /// Which tab reads as current, or null when the operator is on none of the four:
        /// the call screen is reached only through the return strip and has no slot on
        /// this bar.
        /// </summary>
        public NavTab? ActiveTab
        {
            get => _activeTab;
            set
            {
                _activeTab = value;
                foreach (var (tab, button) in _buttons)
                    button.Classes.Set("active", value.HasValue && tab == value.Value);
            }
        }

        /// <summary>
        /// Redraws the Dialer tab's glyph and tooltip from the login mode, so it does not
        /// matter whether SetLoginMode or a language change ran last. The icon used to be
        /// left wherever the previous caller put it, which made leaving login mode depend
        /// on the order MainWindow happened to call things in.
        ///
        /// Two decisions, where there were four: the call-green tint and the breathing
        /// animation went when the return strip took over saying a call is running, and
        /// the slot went back to being a dial pad and nothing else.
        /// </summary>
        private void RefreshTabVisuals()
        {
            if (!_buttons.TryGetValue(NavTab.Dialer, out var dialerBtn)) return;
            var icon = this.FindControl<MaterialIcon>("DialerIcon");

            var kind = NavTabIcon.ForDialerTab(_loginMode);

            if (icon != null)
                icon.Kind = kind;

            // Worded from the glyph, so the two agree by construction: a back arrow reads
            // "Back", never "Dialer". Assigned rather than bound; see the comment on
            // DialerBtn.
            ToolTip.SetTip(dialerBtn, I18nService.Instance.Get(NavTabIcon.TooltipKeyFor(kind)));
        }

        /// <summary>
        /// Disables what a signed-out operator cannot reach.
        ///
        /// Settings is reachable from the login screen, and from there Recents, Tasks and
        /// the dialer all lead nowhere. The Dialer slot becomes a back arrow instead;
        /// MainWindow routes any tab press back to login while this is on.
        /// </summary>
        public void SetLoginMode(bool loginMode)
        {
            _loginMode = loginMode;

            if (_buttons.TryGetValue(NavTab.Recents, out var recents)) recents.IsEnabled = !loginMode;
            if (_buttons.TryGetValue(NavTab.Tasks, out var tasks)) tasks.IsEnabled = !loginMode;

            RefreshTabVisuals();
        }

        /// <summary>Shows the count pill on a tab. Zero or less hides it.</summary>
        public void SetBadge(NavTab tab, int count, bool alert)
        {
            var (badgeName, textName) = tab switch
            {
                NavTab.Recents => ("RecentsBadge", "RecentsBadgeText"),
                NavTab.Tasks   => ("TasksBadge", "TasksBadgeText"),
                _              => (string.Empty, string.Empty),
            };
            if (badgeName.Length == 0) return;

            var badge = this.FindControl<Border>(badgeName);
            var text = this.FindControl<TextBlock>(textName);
            if (badge == null || text == null) return;

            var label = NavBadgeState.FormatCount(count);
            text.Text = label;
            badge.Classes.Set("alert", alert);
            badge.IsVisible = label.Length > 0;
        }

        /// <summary>Show or hide the green update-available dot on the Settings button.</summary>
        private void ShowUpdateDot(bool visible)
        {
            var dot = this.FindControl<Ellipse>("UpdateDot");
            if (dot != null) dot.IsVisible = visible;
        }
    }
}
