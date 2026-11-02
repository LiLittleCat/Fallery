[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$')][string]$Version,
    [string]$ChangelogPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'
$changelog = Get-Content -LiteralPath $ChangelogPath -Raw
$heading = '(?m)^## \[' + [regex]::Escape($Version) + '\] - \d{4}-\d{2}-\d{2}\s*$'
$matchesForVersion = [regex]::Matches($changelog, $heading)
if ($matchesForVersion.Count -ne 1) { throw "CHANGELOG.md must contain exactly one dated entry for $Version." }
$entry = $matchesForVersion[0]
$remaining = $changelog.Substring($entry.Index + $entry.Length)
$end = [regex]::Match($remaining, '(?m)^## |^\[[^\r\n]+\]:')
if ($end.Success) { $remaining = $remaining.Substring(0, $end.Index) }
$body = $remaining.Trim()
if ($body -notmatch '(?m)^### (Added|Changed|Deprecated|Removed|Fixed|Security)\r?$' -or $body -notmatch '(?m)^- \S') {
    throw "CHANGELOG.md entry for $Version must contain categorized changes."
}
# 保留引用式链接定义，但不把 Unreleased 或其他版本正文放进更新说明。
$references = [regex]::Matches($changelog, '(?m)^\[[^\r\n]+\]:[^\r\n]+') | ForEach-Object { $_.Value }
"# Fallery $Version`n`n$body`n`n$($references -join "`n")"
