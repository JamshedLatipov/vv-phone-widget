with open('OrbitalSIP/Views/SettingsView.axaml.cs', 'r', encoding='utf-8') as f:
    content = f.read()

if "OnRecentsRequested" not in content:
    content = content.replace("public event EventHandler? OnBackRequested;", "public event EventHandler? OnBackRequested;\n        public event EventHandler? OnRecentsRequested;")
    content = content.replace("nav.OnDialerRequested += (_, __) => OnBackRequested?.Invoke(this, EventArgs.Empty);", "nav.OnDialerRequested += (_, __) => OnBackRequested?.Invoke(this, EventArgs.Empty);\n                nav.OnRecentsRequested += (_, __) => OnRecentsRequested?.Invoke(this, EventArgs.Empty);")

with open('OrbitalSIP/Views/SettingsView.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(content)
