import re

with open("OrbitalSIP/Views/ActiveCallView.axaml.cs", "r") as f:
    content = f.read()

# Replace MinimizeBtn binding with TopBar binding
old_min = r'''            var minimize = this.FindControl<Button>\("MinimizeBtn"\);
            if \(minimize != null\)
                minimize.Click \+= \(_, __\) => OnMinimizeRequested\?\.Invoke\(this, EventArgs\.Empty\);'''

new_min = r'''            var topBar = this.FindControl<TopBarControl>("TopBar");
            if (topBar != null)
                topBar.OnMinimizeRequested += (_, __) => OnMinimizeRequested?.Invoke(this, EventArgs.Empty);'''

content = re.sub(old_min, new_min, content)

with open("OrbitalSIP/Views/ActiveCallView.axaml.cs", "w") as f:
    f.write(content)
