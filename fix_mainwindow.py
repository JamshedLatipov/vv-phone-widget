import re

with open('OrbitalSIP/MainWindow.axaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Add ShowRecents
show_recents_injection = """
        private void ShowRecents()
        {
            var r = new Views.RecentsView();
            r.OnCloseRequested += (_, __) => ToggleExpanded();
            r.OnSettingsRequested += (_, __) => ShowSettings();
            r.OnDialerRequested += (_, __) => ShowDialer();
            r.OutgoingCallRequested += (_, num) =>
            {
                ShowDialer();
                var sip = App.SipService;
                if (sip.State == CallState.Idle && !string.IsNullOrWhiteSpace(num))
                    sip.Call(num);
            };

            SetMainContent(r);
        }
"""
if "ShowRecents()" not in content:
    content = content.replace("        private void ShowSettings()", f"{show_recents_injection}\n        private void ShowSettings()")

# Add wiring in ExpandedView and SettingsView
content = content.replace("ev.OnSettingsRequested += (_, __) => ShowSettings();", "ev.OnSettingsRequested += (_, __) => ShowSettings();\n            ev.OnRecentsRequested += (_, __) => ShowRecents();")
content = content.replace("sv.OnBackRequested += (_, __) => ShowDialer();", "sv.OnBackRequested += (_, __) => ShowDialer();\n            sv.OnRecentsRequested += (_, __) => ShowRecents();")


with open('OrbitalSIP/MainWindow.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)
