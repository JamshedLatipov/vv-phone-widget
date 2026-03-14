import sys

# 1. Update MainWindow.axaml.cs
file_path = 'OrbitalSIP/MainWindow.axaml.cs'
with open(file_path, 'r') as f:
    content = f.read()

# Improved pointer pressed logic
new_pointer_pressed = """        private void MainWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
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
                visual = visual.VisualParent;
            }

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }"""

# Find the existing method (it might be the original one or the patched one)
import re
pattern = re.compile(r'private void MainWindow_PointerPressed\(object\? sender, PointerPressedEventArgs e\)\s*\{.*?\}', re.DOTALL)
content = pattern.sub(new_pointer_pressed, content)

# Also update CompleteAnimatedContentSwap to set IsVisible=false on OverlayHost
complete_swap_old = """        private void CompleteAnimatedContentSwap()
        {
            var host = this.FindControl<ContentControl>("Host");
            var overlay = this.FindControl<ContentControl>("OverlayHost");
            object? nextContent = overlay?.Content;
            if (overlay != null) overlay.Content = null;
            if (host != null && nextContent != null) { host.Content = nextContent; host.Opacity = 1; }
            else if (host != null) host.Opacity = 1;
            if (overlay != null) overlay.Opacity = 0;
            _pendingContent = null;
        }"""

complete_swap_new = """        private void CompleteAnimatedContentSwap()
        {
            var host = this.FindControl<ContentControl>("Host");
            var overlay = this.FindControl<ContentControl>("OverlayHost");
            object? nextContent = overlay?.Content;
            if (overlay != null) overlay.Content = null;
            if (host != null && nextContent != null) { host.Content = nextContent; host.Opacity = 1; }
            else if (host != null) host.Opacity = 1;
            if (overlay != null) { overlay.Opacity = 0; overlay.IsVisible = false; }
            _pendingContent = null;
        }"""

if complete_swap_old in content:
    content = content.replace(complete_swap_old, complete_swap_new)

# Update StartAnimation to set IsVisible=true
start_anim_old = """            if (overlay != null) { overlay.Content = nextContent; overlay.Opacity = 0; }"""
start_anim_new = """            if (overlay != null) { overlay.Content = nextContent; overlay.Opacity = 0; overlay.IsVisible = true; }"""
content = content.replace(start_anim_old, start_anim_new)

with open(file_path, 'w') as f:
    f.write(content)

# 2. Update App.axaml
app_path = 'OrbitalSIP/App.axaml'
app_content = """<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:fluent="clr-namespace:Avalonia.Themes.Fluent;assembly=Avalonia.Themes.Fluent"
             x:Class="OrbitalSIP.App"
             RequestedThemeVariant="Dark">
  <Application.Styles>
    <fluent:FluentTheme />

    <!-- Custom styles to fix focus/hover issues in our Dark theme -->
    <Style Selector="TextBox:focus /template/ Border#PART_BorderElement">
      <Setter Property="Background" Value="Transparent" />
      <Setter Property="BorderBrush" Value="Transparent" />
      <Setter Property="BorderThickness" Value="0" />
    </Style>

    <Style Selector="TextBox:pointerover /template/ Border#PART_BorderElement">
      <Setter Property="Background" Value="Transparent" />
      <Setter Property="BorderBrush" Value="Transparent" />
      <Setter Property="BorderThickness" Value="0" />
    </Style>

    <Style Selector="ComboBoxItem">
      <Setter Property="Padding" Value="12,8" />
      <Setter Property="Foreground" Value="#FFFFFF" />
    </Style>

    <Style Selector="ComboBox:pointerover /template/ Border#PART_ButtonLayoutBorder">
      <Setter Property="Background" Value="#2D3A4F" />
      <Setter Property="BorderBrush" Value="#3B82F6" />
    </Style>

    <Style Selector="ComboBox:focus /template/ Border#PART_ButtonLayoutBorder">
      <Setter Property="Background" Value="#1E293B" />
      <Setter Property="BorderBrush" Value="#3B82F6" />
    </Style>
  </Application.Styles>

  <TrayIcon.Icons>
    <TrayIcons>
      <TrayIcon Icon="/Assets/icon.png"
                ToolTipText="Orbital SIP"
                Clicked="TrayIcon_Clicked">
        <TrayIcon.Menu>
          <NativeMenu>
            <NativeMenuItem Header="Show" Click="MenuShow_Click" />
            <NativeMenuItem Header="Hide" Click="MenuHide_Click" />
            <NativeMenuItemSeparator />
            <NativeMenuItem Header="Exit" Click="MenuExit_Click" />
          </NativeMenu>
        </TrayIcon.Menu>
      </TrayIcon>
    </TrayIcons>
  </TrayIcon.Icons>
</Application>"""

with open(app_path, 'w') as f:
    f.write(app_content)

print("Applied comprehensive fixes to MainWindow and App styling")
