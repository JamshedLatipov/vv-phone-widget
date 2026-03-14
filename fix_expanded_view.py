with open('OrbitalSIP/Views/ExpandedView.axaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

if "OnRecentsRequested" not in content:
    content = content.replace("public event System.EventHandler?        OnSettingsRequested;", "public event System.EventHandler?        OnSettingsRequested;\n        public event System.EventHandler?        OnRecentsRequested;")
    content = content.replace("bottomNav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);", "bottomNav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);\n            bottomNav.OnRecentsRequested += (_, __) => OnRecentsRequested?.Invoke(this, EventArgs.Empty);")

with open('OrbitalSIP/Views/ExpandedView.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)
