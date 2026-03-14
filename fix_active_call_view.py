with open('OrbitalSIP/Views/ActiveCallView.axaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

if "OnRecentsRequested" not in content:
    content = content.replace("public event EventHandler? OnSettingsRequested;", "public event EventHandler? OnSettingsRequested;\n        public event EventHandler? OnRecentsRequested;")
    content = content.replace("nav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);", "nav.OnSettingsRequested += (_, __) => OnSettingsRequested?.Invoke(this, EventArgs.Empty);\n                nav.OnRecentsRequested += (_, __) => OnRecentsRequested?.Invoke(this, EventArgs.Empty);")

with open('OrbitalSIP/Views/ActiveCallView.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)
