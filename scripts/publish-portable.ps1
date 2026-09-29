# Publishes a self-contained portable Optim release (unpackaged app, no
# installer needed — unzip and run elevated).
param(
    [string]$Configuration = "Release",
    [string]$OutDir = "$PSScriptRoot\..\release",
    # Overrides the csproj version for the output names. CI passes the release
    # tag so the files always match the release they are attached to, instead
    # of silently drifting when the csproj lags a tag.
    [string]$Version
)

$ErrorActionPreference = "Stop"
$env:PATH = "C:\Program Files\dotnet;" + $env:PATH
$root = Split-Path -Parent $PSScriptRoot
$version = if (-not [string]::IsNullOrWhiteSpace($Version)) { $Version } else {
    (Select-Xml -LiteralPath "$root\src\Optim.App\Optim.App.csproj" -XPath "/Project/PropertyGroup/Version").Node.InnerText
}
if ([string]::IsNullOrWhiteSpace($version)) { $version = "1.0.0" }

$publishDir = Join-Path $OutDir "Optim-portable-$version"
Write-Output "Publishing Optim $version ($Configuration)..."
# Start clean: a reused folder keeps stale files (e.g. test binaries from a
# solution-level publish) inside the shipped artifact.
if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
# Publish the app project directly (not the solution): solution-level -o
# mixes test binaries into the artifact, and Platform=x64 is only valid
# at project level (the .slnx config does not define it).
# ReadyToRun precompiles the managed code, so opening a page the first time does
# not pay for JIT compiling it: that first-use compilation is what makes the
# first visit to each page feel heavier than every visit after it. It costs
# publish time and some artifact size, and changes no behaviour.
& dotnet publish "$root\src\Optim.App\Optim.App.csproj" -c $Configuration -p:Platform=x64 -p:PublishReadyToRun=true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$zip = Join-Path $OutDir "Optim-portable-$version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip
Write-Output "Wrote $zip"
