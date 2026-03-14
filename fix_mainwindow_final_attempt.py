import sys

file_path = 'OrbitalSIP/MainWindow.axaml.cs'
with open(file_path, 'r') as f:
    lines = f.readlines()

# Clean up PointerPressed mess
new_lines = []
skip = False
for line in lines:
    if 'private void MainWindow_PointerPressed' in line:
        new_lines.append("""        private void MainWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
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
        }
""")
        skip = True
        continue
    if skip:
        if 'private void MainWindow_PointerReleased' in line:
            skip = False
            new_lines.append(line)
        continue
    new_lines.append(line)

with open(file_path, 'w') as f:
    f.writelines(new_lines)
