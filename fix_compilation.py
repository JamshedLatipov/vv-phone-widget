import re

# Fix 1: SipService CallAsync
with open('OrbitalSIP/MainWindow.axaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace("sip.Call(num);", "sip.CallAsync(num);")

with open('OrbitalSIP/MainWindow.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)

# Fix 2: ExpandedView possible null reference warning
with open('OrbitalSIP/Views/ExpandedView.axaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace("bottomNav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);", "if (bottomNav != null) bottomNav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);")
content = content.replace("bottomNav.OnRecentsRequested += (_, __) => OnRecentsRequested?.Invoke(this, EventArgs.Empty);", "if (bottomNav != null) bottomNav.OnRecentsRequested += (_, __) => OnRecentsRequested?.Invoke(this, EventArgs.Empty);")


with open('OrbitalSIP/Views/ExpandedView.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)
