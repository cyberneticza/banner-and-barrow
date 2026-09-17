# Builds a self-contained Windows release of Banner & Barrow that runs without .NET installed.
#
#   pwsh scripts/publish.ps1                 # -> artifacts/publish/win-x64
#   pwsh scripts/publish.ps1 -Version 0.2.0  # also stamps the version
#   pwsh scripts/publish.ps1 -Zip            # also writes artifacts/BannerAndBarrow-<version>-win-x64.zip
#   pwsh scripts/publish.ps1 -Version 0.2.0 -Pack   # also builds the Velopack installer in artifacts/releases
#
# Notes:
# - Single-file, but SDL2.dll and openal.dll stay as loose files next to the exe: MonoGame DesktopGL loads them by
#   path and fails ("Failed to load library: SDL2.dll") if they are bundled into the exe.
# - Content/, config/ and Assets/ are folders next to the exe. User settings are not shipped; the game writes them
#   to %APPDATA%\BannerAndBarrow\settings.json.
# - -Pack runs Velopack's vpk (a local dotnet tool). VelopackApp.Build().Run() must stay the first line of Program.cs.
#   See docs/distribution.md.
param(
    [string]$Version = "0.1.0",
    [string]$Runtime = "win-x64",
    [switch]$Zip,
    [switch]$Pack
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts/publish/$Runtime"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }

dotnet publish (Join-Path $root "src/BannerAndBarrow.Game") `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:DebugType=None `
    -p:Version=$Version `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

foreach ($required in @("BannerAndBarrow.exe", "SDL2.dll", "Content", "config", "Assets")) {
    if (-not (Test-Path (Join-Path $out $required))) { throw "Publish output is missing $required" }
}

$size = (Get-ChildItem $out -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("Published {0} {1} to {2} ({3:N0} MB)" -f $Version, $Runtime, $out, $size)

if ($Zip) {
    $zipPath = Join-Path $root "artifacts/BannerAndBarrow-$Version-$Runtime.zip"
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zipPath
    Write-Host "Zipped to $zipPath"
}

if ($Pack) {
    # Note: packTitle has no "&" - an ampersand breaks the package metadata XML.
    # Per-user installer (no admin): Setup.exe installs to %LocalAppData%\BannerAndBarrow with Start Menu and Desktop
    # shortcuts. The releases folder also holds the portable zip and the packages Velopack uses for updates.
    $releases = Join-Path $root "artifacts/releases"
    Push-Location $root
    try {
        dotnet tool restore | Out-Null
        dotnet vpk pack `
            --packId BannerAndBarrow `
            --packVersion $Version `
            --packDir $out `
            --mainExe BannerAndBarrow.exe `
            --packTitle "Banner and Barrow" `
            --packAuthors "Cyberneticza" `
            --icon (Join-Path $root "src/BannerAndBarrow.Game/Icon.ico") `
            --outputDir $releases
        if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }
    }
    finally {
        Pop-Location
    }
    Get-ChildItem $releases | ForEach-Object { Write-Host ("  {0} ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }
}
