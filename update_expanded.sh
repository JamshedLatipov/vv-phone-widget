awk '
BEGIN { in_header = 0 }
/<!-- Header -->/ {
    print
    print "      <Views:TopBarControl Name=\"TopBar\" />"
    in_header = 1
    next
}
in_header && /<\/Grid>/ {
    in_header = 0
    next
}
in_header { next }
{ print }
' OrbitalSIP/Views/ExpandedView.axaml > tmp.axaml && mv tmp.axaml OrbitalSIP/Views/ExpandedView.axaml
