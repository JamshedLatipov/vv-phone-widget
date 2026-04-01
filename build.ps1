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

    # ── Publish GitHub Release ────────────────────────────────────────────────
    # Requires GitHub CLI (winget install --id GitHub.cli) authenticated via `gh auth login`.
    # The release tag must match the version string used in UpdateService.cs (e.g. v1.0.8).
    $installer = "$root\dist\OrbitalSIP-Setup-$newVersion.exe"
    $tag       = "v$newVersion"

    if (Get-Command gh -ErrorAction SilentlyContinue) {
        Write-Host "Creating GitHub release $tag ..." -ForegroundColor Cyan
        gh release create $tag $installer `
            --title $tag `
            --generate-notes

        if ($LASTEXITCODE -eq 0) {
            Write-Host "GitHub release $tag published." -ForegroundColor Green
        } else {
            Write-Host "GitHub release publish FAILED (exit $LASTEXITCODE). Upload $installer manually." -ForegroundColor Yellow
        }
    } else {
        Write-Host "GitHub CLI (gh) not found. Install: winget install --id GitHub.cli" -ForegroundColor Yellow
        Write-Host "Then run: gh release create $tag $installer --title $tag --generate-notes" -ForegroundColor Yellow
    }
} else {
    Write-Host "Inno Setup FAILED." -ForegroundColor Red
    exit 1
}
