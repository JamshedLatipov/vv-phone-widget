# build.ps1 — increment patch version, publish, build installer
$root        = $PSScriptRoot
$versionFile = "$root\version.txt"
$csproj      = "$root\OrbitalSIP\OrbitalSIP.csproj"
$issFile     = "$root\installer\OrbitalSIP.iss"
$publishDir  = "$root\publish\win-x64"
$iscc        = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

# ── Read & increment version ──────────────────────────────────────────────────
$version = (Get-Content $versionFile -Raw).Trim()
$parts   = $version -split '\.'
if ($parts.Count -lt 3) { $parts += '0' }
$parts[2] = [int]$parts[2] + 1
$newVersion = $parts -join '.'

Set-Content $versionFile $newVersion -NoNewline
Write-Host "Building version $newVersion ..." -ForegroundColor Cyan

# ── Publish ───────────────────────────────────────────────────────────────────
dotnet publish $csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$newVersion `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish FAILED." -ForegroundColor Red
    exit 1
}

# ── Installer ─────────────────────────────────────────────────────────────────
& $iscc /DMyAppVersion=$newVersion $issFile

if ($LASTEXITCODE -eq 0) {
    Write-Host "Done! dist\OrbitalSIP-Setup-$newVersion.exe" -ForegroundColor Green
} else {
    Write-Host "Inno Setup FAILED." -ForegroundColor Red
    exit 1
}
