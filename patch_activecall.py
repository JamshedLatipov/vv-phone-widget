with open("OrbitalSIP/Views/ActiveCallView.axaml", "r") as f:
    content = f.read()

start_str = '        <Grid ColumnDefinitions="*,Auto" Background="#1E293B" Margin="0,0,0,12">'
end_str = '          </StackPanel>\n        </Grid>'

if start_str in content and end_str in content:
    start_idx = content.find(start_str)
    end_idx = content.find(end_str) + len('          </StackPanel>\n        </Grid>')

    new_content = content[:start_idx] + '        <Views:TopBarControl Name="TopBar" Margin="0,0,0,12" />' + content[end_idx:]
    with open("OrbitalSIP/Views/ActiveCallView.axaml", "w") as f:
        f.write(new_content)
else:
    print("Could not find blocks.")
