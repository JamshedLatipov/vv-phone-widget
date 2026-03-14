import sys

file_path = 'OrbitalSIP/MainWindow.axaml.cs'
with open(file_path, 'r') as f:
    content = f.read()

search_text = """        private void MainWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }"""

replace_text = """        private void MainWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Only drag if we clicked on the background/border, not interactive controls
            var src = e.Source as Control;
            if (src is Button || src is TextBox || src is ComboBox || src is ComboBoxItem)
                return;

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }"""

if search_text in content:
    new_content = content.replace(search_text, replace_text)
    with open(file_path, 'w') as f:
        f.write(new_content)
    print("Successfully patched MainWindow.axaml.cs")
else:
    print("Could not find search text in MainWindow.axaml.cs")
    sys.exit(1)
