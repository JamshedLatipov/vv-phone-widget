import sys

filename = 'OrbitalSIP/Views/ActiveCallView.axaml.cs'
with open(filename, 'r') as f:
    content = f.read()

# Add debug logging
content = content.replace(
    'var callerNumber = this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? string.Empty;',
    'var callerNumber = this.FindControl<TextBlock>("CallerNumberLabel")?.Text?.Trim() ?? string.Empty;\n            Console.WriteLine($"[CreateLeadAsync] Extracted callerNumber: \'{callerNumber}\'");'
)

with open(filename, 'w') as f:
    f.write(content)
