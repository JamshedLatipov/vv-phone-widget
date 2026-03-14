with open("OrbitalSIP/Views/ExpandedView.axaml", "r") as f:
    content = f.read()

# Let's use substring replacement
start_str = '      <!-- Header -->\n      <Grid ColumnDefinitions="*,Auto" Background="#1E293B">'
end_str = '        </Button>\n      </Grid>\n\n      <!-- Dialer display -->'

if start_str in content and end_str in content:
    start_idx = content.find(start_str)
    end_idx = content.find(end_str) + len('        </Button>\n      </Grid>')

    new_content = content[:start_idx] + '      <!-- Header -->\n      <Views:TopBarControl Name="TopBar" />' + content[end_idx:]
    with open("OrbitalSIP/Views/ExpandedView.axaml", "w") as f:
        f.write(new_content)
else:
    print("Could not find blocks. Printing parts:")
    print("start_str found:", start_str in content)
    print("end_str found:", end_str in content)
