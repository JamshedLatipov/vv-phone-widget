#!/bin/bash

# Update ExpandedView
sed -i 's/<Grid ColumnDefinitions="\*,Auto" Background="#1E293B">/<Views:TopBarControl Name="TopBar" \/>/g' OrbitalSIP/Views/ExpandedView.axaml
sed -i '/<StackPanel Orientation="Horizontal"/,/<\/Grid>/d' OrbitalSIP/Views/ExpandedView.axaml

# Update ActiveCallView
sed -i 's/<Grid ColumnDefinitions="\*,Auto" Background="#1E293B" Margin="0,0,0,12">/<Views:TopBarControl Name="TopBar" Margin="0,0,0,12" \/>/g' OrbitalSIP/Views/ActiveCallView.axaml
sed -i '/<StackPanel Orientation="Horizontal"/,/<\/Grid>/d' OrbitalSIP/Views/ActiveCallView.axaml

# Update SettingsView (add top bar instead of back button and header)
sed -i 's/<Grid Grid.Row="0" ColumnDefinitions="Auto,\*,Auto" Margin="0,0,0,20">/<Views:TopBarControl Name="TopBar" Margin="0,0,0,20" \/>/g' OrbitalSIP/Views/SettingsView.axaml
sed -i '/<Button Name="BackBtn"/,/<\/Grid>/d' OrbitalSIP/Views/SettingsView.axaml

# Update LoginView (maybe keep as is or add top bar, let's keep it as is because it's a login screen without registration status)
