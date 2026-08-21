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
    /// screen never wired the dialer, and Contacts was wired by nobody at all. Routing
    /// now lives in MainWindow.NavigateTo, in one switch nobody can partially implement.
    /// </summary>
    public partial class BottomNavControl : UserControl
    {
        /// <summary>Raised on every tab press, including a press on the active tab.</summary>
        public event EventHandler<NavTab>? TabSelected;

        private readonly Dictionary<NavTab, Button> _buttons = new();
        private NavTab _activeTab = NavTab.Dialer;
        private bool _inCall;
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
        ///
        /// The pulse needs no such release: it belongs to a style, and leaving the tree
        /// detaches the styles with everything they are running.
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

        /// <summary>Which tab reads as current. Set by MainWindow, never inferred here.</summary>
        public NavTab ActiveTab
        {
            get => _activeTab;
            set
            {
                _activeTab = value;
                foreach (var (tab, button) in _buttons)
                    button.Classes.Set("active", tab == value);
                RefreshTabVisuals();
            }
        }

        /// <summary>
        /// Swaps the Dialer tab to a "you are on a call" affordance.
        ///
        /// ShowDialer() has always redirected to the call screen while a call is up, so the
        /// tab already meant "back to the call" — it just never said so, and the highlight
        /// claimed the operator was looking at a dialpad they could not reach.
        /// </summary>
        public void SetInCall(bool inCall)
        {
            _inCall = inCall;
            RefreshTabVisuals();
        }

        /// <summary>
        /// Redraws everything about the Dialer tab that depends on state, from the state
        /// itself — so it does not matter which of ActiveTab, SetInCall and SetLoginMode
        /// ran last. The icon in particular used to be left wherever the previous caller
        /// put it, which made leaving login mode depend on the order MainWindow happened
        /// to call them in.
        ///
        /// Named for the tab rather than the call because login mode is decided here too;
        /// nobody would look inside a RefreshInCallVisuals for it.
        /// </summary>
        private void RefreshTabVisuals()
        {
            if (!_buttons.TryGetValue(NavTab.Dialer, out var dialerBtn)) return;
            var icon = this.FindControl<MaterialIcon>("DialerIcon");

            // Login mode wins over the call state in all four decisions below, not just on
            // the icon. A signed-out operator has no call to be taken back to, so a back
            // arrow tinted call-green, tooltipped as a conversation, or breathing to invite
            // them into one would each be a lie on its own.
            var inCall = _inCall && !_loginMode;

            var kind = NavTabIcon.ForDialerTab(_loginMode, _inCall);

            dialerBtn.Classes.Set("in-call", inCall);
            if (icon != null)
                icon.Kind = kind;

            // Worded from the glyph, so the two agree by construction: a back arrow reads
            // "Back", never "Dialer". The in-call wording is NavInCall and not the InCall
            // the status line uses — that one is an all-caps status label, which reads as a
            // shout in a tooltip. Assigned rather than bound; see the comment on DialerBtn.
            ToolTip.SetTip(dialerBtn, I18nService.Instance.Get(NavTabIcon.TooltipKeyFor(kind)));

            SetPulse(dialerBtn, NavPulse.ShouldPulse(inCall, _activeTab));
        }

        /// <summary>
        /// Breathes the tab while a call runs off-screen.
        ///
        /// A Transition would be wrong here: it animates a value that changes, and this
        /// value does not. It takes an Animation with an infinite iteration count, and in
        /// Avalonia 11 only a style can start one of those — Animation.RunAsync throws on
        /// IterationCount.Infinite ("Looping animations must not use the Run method") and
        /// Animation.Apply, the call the style system makes on its behalf, is internal.
        ///
        /// So the class is the switch, and clearing it is what stops the animation: left
        /// running it keeps the compositor busy on a window that is otherwise perfectly
        /// still. The resting opacity comes back from the styles rather than from an
        /// assignment here, which is why a local Opacity is never written — one would
        /// outrank :pointerover for the rest of this control's life.
        /// </summary>
        private static void SetPulse(Button button, bool pulse) =>
            button.Classes.Set("pulse", pulse);

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
