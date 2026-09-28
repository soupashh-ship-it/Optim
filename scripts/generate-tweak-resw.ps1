# Regenerates the Tweak_* strings in src/Optim.App/Strings/en-US/Resources.resw
# from TweakCatalog, so the 111 tweak titles and descriptions can never drift
# from the code that renders them.
#
# The actual sync logic lives in TweakResourceSyncTests.Regenerate (it needs a
# compiled reference to Optim.Core, which a plain PowerShell script cannot
# load). This wrapper just builds the test project and runs that one test with
# OPTIM_SYNC_RESW=1; a following normal test run verifies the committed file.
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$env:PATH = "C:\Program Files\dotnet;" + $env:PATH
$root = Split-Path -Parent $PSScriptRoot

Write-Output "Building test project..."
& dotnet build "$root\tests\Optim.Core.Tests\Optim.Core.Tests.csproj" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Output "Regenerating Tweak_ resources..."
$env:OPTIM_SYNC_RESW = "1"
& dotnet test "$root\tests\Optim.Core.Tests\Optim.Core.Tests.csproj" -c $Configuration --nologo `
    --filter "FullyQualifiedName~TweakResourceSyncTests.Regenerate" --no-build
if ($LASTEXITCODE -ne 0) { throw "regeneration failed" }
Remove-Item Env:OPTIM_SYNC_RESW

Write-Output "Verifying..."
& dotnet test "$root\tests\Optim.Core.Tests\Optim.Core.Tests.csproj" -c $Configuration --nologo `
    --filter "FullyQualifiedName~TweakResourceSyncTests" --no-build
if ($LASTEXITCODE -ne 0) { throw "verification failed" }

Write-Output "Resources.resw is in sync with TweakCatalog."
