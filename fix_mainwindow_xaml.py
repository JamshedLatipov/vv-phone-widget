file_path = 'OrbitalSIP/MainWindow.axaml'
with open(file_path, 'r') as f:
    content = f.read()

old_overlay = """    <ContentControl Name="OverlayHost"
                    Opacity="0"
                    IsHitTestVisible="False" />"""

new_overlay = """    <ContentControl Name="OverlayHost"
                    Opacity="0"
                    IsVisible="False"
                    IsHitTestVisible="False" />"""

if old_overlay in content:
    new_content = content.replace(old_overlay, new_overlay)
    with open(file_path, 'w') as f:
        f.write(new_content)
    print("Successfully patched MainWindow.axaml")
else:
    print("OverlayHost already patched or not found")
