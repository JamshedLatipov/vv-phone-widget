with open("OrbitalSIP/Views/SettingsView.axaml", "r") as f:
    content = f.read()

start_str = '      <!-- Header -->\n      <Grid Grid.Row="0" ColumnDefinitions="Auto,*,Auto" Margin="0,0,0,20">'
end_str = '    </Grid>\n\n\n      <!-- Content -->'

if start_str in content and end_str in content:
    start_idx = content.find(start_str)
    end_idx = content.find(end_str) + len('    </Grid>')

    new_content = content[:start_idx] + '      <!-- Header -->\n      <Views:TopBarControl Name="TopBar" Grid.Row="0" Margin="-20,-20,-20,10" />' + content[end_idx:]
    with open("OrbitalSIP/Views/SettingsView.axaml", "w") as f:
        f.write(new_content)
else:
    print("Could not find blocks.")
