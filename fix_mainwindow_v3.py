import sys
import re

file_path = 'OrbitalSIP/MainWindow.axaml.cs'
with open(file_path, 'r') as f:
    content = f.read()

# Add usings if missing
if 'using Avalonia.VisualTree;' not in content:
    content = 'using Avalonia.VisualTree;\n' + content
if 'using Avalonia.Controls.Primitives;' not in content:
    content = 'using Avalonia.Controls.Primitives;\n' + content

# Robust replacement for PointerPressed
pattern = re.compile(r'private void MainWindow_PointerPressed\(object\? sender, PointerPressedEventArgs e\)\s*\{.*?\}', re.DOTALL)
replacement = """private void MainWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Traverse the visual tree from the source to see if we clicked an interactive control
            var visual = e.Source as Visual;
            while (visual != null && visual != this)
            {
                if (visual is Button || visual is TextBox || visual is ComboBox ||
                    visual is ListBoxItem || visual is ScrollBar || visual is ScrollViewer)
                {
                    return; // Interactive control reached, do not drag
                }
                visual = visual.GetVisualParent();
            }

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }"""

content = pattern.sub(replacement, content)

with open(file_path, 'w') as f:
    f.write(content)
