# Generates minimal release notes from the commits between the previous tag
# and the given tag: one bullet per commit subject plus a compare link. The
# release workflow feeds this to `gh release create/edit` on every tag build,
# so the release body always reflects the actual history instead of a
# hand-written file that drifts.
#
# Works locally too: pwsh scripts/generate-release-notes.ps1 -Tag v1.1.1
# prints the notes when -Out is omitted.
param(
    [Parameter(Mandatory)][string]$Tag,
    # Override for the lower end of the range; defaults to the nearest
    # preceding tag (or the initial commit for the first release).
    [string]$PreviousTag,
    [string]$Out = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

git -C $root rev-parse --verify --quiet "refs/tags/$Tag" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Tag '$Tag' does not exist in this repository." }

if ([string]::IsNullOrWhiteSpace($PreviousTag)) {
    # Nearest tag reachable from the commit before this one; a first release
    # has no predecessor, so the range starts at the root commit.
    $PreviousTag = git -C $root describe --tags --abbrev=0 "$Tag^" 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($PreviousTag)) {
        $PreviousTag = git -C $root rev-list --max-parents=0 "$Tag" | Select-Object -Last 1
    }
}

# NOTE: the format must be one joined '--format=%s' argument. Passing it as
# two ('--format', '%s') makes pwsh hand git a bare %s, which git parses as a
# revision and the whole log call fails.
$subjects = @(git -C $root log --reverse '--format=%s' "$PreviousTag..$Tag")
if ($LASTEXITCODE -ne 0) {
    throw "git log failed for range $PreviousTag..$Tag."
}
if ($subjects.Count -eq 0) {
    throw "No commits found between $PreviousTag and $Tag."
}

$remote = git -C $root remote get-url origin
$repo = ($remote -replace '^https://github\.com/', '') -replace '\.git$', ''

$lines = @( "## What's in $Tag", "" )
foreach ($s in $subjects) { $lines += "- $s" }
$lines += ""
$lines += "**Full changelog:** https://github.com/$repo/compare/$PreviousTag...$Tag"

$text = ($lines -join [Environment]::NewLine) + [Environment]::NewLine
if ([string]::IsNullOrWhiteSpace($Out)) {
    $text
} else {
    Set-Content -LiteralPath $Out -Value $text -NoNewline
    Write-Output "Wrote $Out (range $PreviousTag..$Tag, $($subjects.Count) entries)"
}
