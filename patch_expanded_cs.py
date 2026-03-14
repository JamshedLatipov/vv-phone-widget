import re

with open("OrbitalSIP/Views/ExpandedView.axaml.cs", "r") as f:
    content = f.read()

# Replace constructor body
c_start = content.find("        public ExpandedView()")
c_end = content.find("        private void InitializeComponent()")
if c_start != -1 and c_end != -1:
    new_c = """        public ExpandedView()
        {
            InitializeComponent();
            WireButtons();
        }

"""
    content = content[:c_start] + new_c + content[c_end:]

# Remove UpdateStatus
u_start = content.find("        private void UpdateStatus(RegistrationState state)")
if u_start != -1:
    u_end = content.find("        private void WireButtons()", u_start)
    if u_end != -1:
        content = content[:u_start] + content[u_end:]

# Replace CloseBtn binding
content = content.replace(
    '            Bind("CloseBtn",    () => OnCloseRequested?.Invoke(this, EventArgs.Empty));',
    '            var topBar = this.FindControl<TopBarControl>("TopBar");\n            if (topBar != null) topBar.OnMinimizeRequested += (_, __) => OnCloseRequested?.Invoke(this, EventArgs.Empty);'
)

# Clean up empty block `        }\n\n        }` that got left behind
content = content.replace('        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);\n\n\n        }', '        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);')

with open("OrbitalSIP/Views/ExpandedView.axaml.cs", "w") as f:
    f.write(content)
