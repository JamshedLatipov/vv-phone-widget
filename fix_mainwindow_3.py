import re

with open('OrbitalSIP/MainWindow.axaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

show_recents_injection = """
        private void ShowRecents()
        {
            var r = new Views.RecentsView();
            r.OnCloseRequested += (_, __) => ToggleExpanded();
            r.OnSettingsRequested += (_, __) => ShowSettings();
            r.OnDialerRequested += (_, __) => ShowDialer();
            r.OutgoingCallRequested += (sender, num) =>
            {
                var sip = App.SipService;
                if (sip.State == CallState.Idle && !string.IsNullOrWhiteSpace(num))
                    sip.Call(num);
                ShowDialer();
            };

            SetMainContent(r);
        }
"""

if "ShowRecents()" not in content:
    content = content.replace("        private void ShowDialer()", f"{show_recents_injection}\n        private void ShowDialer()")


content = content.replace("dialer.OnSettingsRequested += (_, __) => ShowSettings();", "dialer.OnSettingsRequested += (_, __) => ShowSettings();\n            dialer.OnRecentsRequested += (_, __) => ShowRecents();")
content = content.replace("settingsView.OnBackRequested += (_, __) => ShowDialer();", "settingsView.OnBackRequested += (_, __) => ShowDialer();\n            settingsView.OnRecentsRequested += (_, __) => ShowRecents();")
content = content.replace("callView.OnSettingsRequested += (_, __) => ShowSettings();", "callView.OnSettingsRequested += (_, __) => ShowSettings();\n            callView.OnRecentsRequested += (_, __) => ShowRecents();")


with open('OrbitalSIP/MainWindow.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)
