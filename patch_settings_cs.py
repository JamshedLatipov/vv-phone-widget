import re

with open("OrbitalSIP/Views/SettingsView.axaml.cs", "r") as f:
    content = f.read()

# Replace BackBtn bindings with TopBar binding
old_back = r'''            var back = this.FindControl<Button>\("BackBtn"\);
            var bottomNav = this.FindControl<BottomNavControl>\("BottomNav"\);
            if \(bottomNav != null\) \{ bottomNav.OnSettingsRequested \+= \(_, __\) => OnBackRequested\?\.Invoke\(this, System.EventArgs\.Empty\); bottomNav.SetActiveTab\("Settings"\); \}
            if \(back != null\)
                back.Click \+= \(_, __\) => OnBackRequested\?\.Invoke\(this, System.EventArgs\.Empty\);'''

new_back = r'''            var topBar = this.FindControl<TopBarControl>("TopBar");
            if (topBar != null)
            {
                topBar.SetTitle("Settings");
                topBar.OnMinimizeRequested += (_, __) => OnBackRequested?.Invoke(this, System.EventArgs.Empty);
            }
            var bottomNav = this.FindControl<BottomNavControl>("BottomNav");
            if (bottomNav != null) { bottomNav.OnSettingsRequested += (_, __) => OnBackRequested?.Invoke(this, System.EventArgs.Empty); bottomNav.SetActiveTab("Settings"); }'''

content = re.sub(old_back, new_back, content)

with open("OrbitalSIP/Views/SettingsView.axaml.cs", "w") as f:
    f.write(content)
