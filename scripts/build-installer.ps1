# Builds the Optim installer: publishes the self-contained app, then compiles
# installer\Optim.iss with Inno Setup.
#
# Inno Setup is required (winget install JRSoftware.InnoSetup). It is not a
# project dependency, so this script only reports the download page when the
# compiler is missing.
param(
    [string]$Configuration = "Release",
    # CI reuses the publish step's artifact instead of publishing twice.
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$env:PATH = "C:\Program Files\dotnet;" + $env:PATH
$root = Split-Path -Parent $PSScriptRoot

$version = (Select-Xml -LiteralPath "$root\src\Optim.App\Optim.App.csproj" -XPath "/Project/PropertyGroup/Version").Node.InnerText
if ([string]::IsNullOrWhiteSpace($version)) { $version = "1.0.0" }

if (-not $SkipPublish) {
    & "$PSScriptRoot\publish-portable.ps1" -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "publish-portable.ps1 failed" }
}

$payload = Join-Path $root "release\Optim-portable-$version"
if (-not (Test-Path -LiteralPath $payload)) {
    throw "Publish payload not found at $payload. Run scripts\publish-portable.ps1 first."
}

# Inno Setup installs per-machine by default, so also probe the usual folders:
# a CI shim or a user-scope install may not be on this shell's PATH yet.
$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    foreach ($candidate in @(
        "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )) {
        if (Test-Path -LiteralPath $candidate) { $iscc = $candidate; break }
    }
}
if (-not $iscc) {
    throw "Inno Setup compiler (iscc.exe) not found. Install it with: winget install JRSoftware.InnoSetup"
}

Write-Output "Compiling installer for Optim $version with $iscc ..."
& $iscc "/DVersion=$version" "$root\installer\Optim.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$output = Join-Path $root "release\Optim-$version-setup.exe"
if (-not (Test-Path -LiteralPath $output)) { throw "Expected installer was not produced: $output" }
Write-Output "Wrote $output"
