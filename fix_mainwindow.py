import sys

with open('OrbitalSIP/MainWindow.axaml.cs', 'r') as f:
    lines = f.readlines()

new_lines = []
inserted_closing = False
for line in lines:
    new_lines.append(line)
    if 'this.DoubleTapped' in line and not inserted_closing:
        new_lines.append('            this.Closing += (s, e) => { e.Cancel = true; this.Hide(); };\n')
        inserted_closing = True

with open('OrbitalSIP/MainWindow.axaml.cs', 'w') as f:
    f.writelines(new_lines)
