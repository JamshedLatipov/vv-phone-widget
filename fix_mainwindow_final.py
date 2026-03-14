import sys

file_path = 'OrbitalSIP/MainWindow.axaml.cs'
with open(file_path, 'r') as f:
    lines = f.readlines()

# Add missing using directives
new_usings = [
    "using Avalonia.VisualTree;\n",
    "using Avalonia.Controls.Primitives;\n"
]

for u in new_usings:
    if u not in lines:
        lines.insert(0, u)

content = "".join(lines)

# Fix visual tree traversal
old_code = """            var visual = e.Source as Visual;
            while (visual != null && visual != this)
            {
                if (visual is Button || visual is TextBox || visual is ComboBox ||
                    visual is ListBoxItem || visual is ScrollBar || visual is ScrollViewer)
                {
                    return; // Interactive control reached, do not drag
                }
                visual = visual.VisualParent;
            }"""

new_code = """            var visual = e.Source as Visual;
            while (visual != null && visual != this)
            {
                if (visual is Button || visual is TextBox || visual is ComboBox ||
                    visual is ListBoxItem || visual is ScrollBar || visual is ScrollViewer)
                {
                    return; // Interactive control reached, do not drag
                }
                visual = visual.GetVisualParent();
            }"""

if old_code in content:
    content = content.replace(old_code, new_code)
else:
    print("Could not find the old code block for replacement")
    # Let's try to find it with slightly different formatting if any
    import re
    pattern = re.compile(r'var visual = e.Source as Visual;.*?visual = visual.VisualParent;.*?\}', re.DOTALL)
    content = pattern.sub(new_code.replace('visual.GetVisualParent()', 'visual.VisualParent'), content) # Wait, let's just use the robust pattern match

with open(file_path, 'w') as f:
    f.write(content)
