[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$')][string]$Version,
    [string]$ChangelogPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md'),
    [string]$ChineseChangelogPath
)

$ErrorActionPreference = 'Stop'
if (!$ChineseChangelogPath) { $ChineseChangelogPath = Join-Path (Split-Path -Parent $ChangelogPath) 'CHANGELOG.zh-CN.md' }
function Read-VersionNotes([string]$Path) {
    $changelog = Get-Content -LiteralPath $Path -Raw
    $heading = '(?m)^## \[' + [regex]::Escape($Version) + '\] - (\d{4}-\d{2}-\d{2})\s*$'
    $entries = [regex]::Matches($changelog, $heading)
    if ($entries.Count -ne 1) { throw "$Path must contain exactly one dated entry for $Version." }
    $entry = $entries[0]
    $remaining = $changelog.Substring($entry.Index + $entry.Length)
    $end = [regex]::Match($remaining, '(?m)^## |^\[[^\r\n]+\]:')
    if ($end.Success) { $remaining = $remaining.Substring(0, $end.Index) }
    $body = $remaining.Trim()
    if ($body -notmatch '(?m)^### (Added|Changed|Deprecated|Removed|Fixed|Security)\r?$' -or $body -notmatch '(?m)^- \S') {
        throw "$Path entry for $Version must contain categorized changes."
    }
    # 每种语言独立携带引用，供客户端选择片段后解析；不包含其他版本正文。
    $references = [regex]::Matches($changelog, '(?m)^\[[^\r\n]+\]:[^\r\n]+') | ForEach-Object { $_.Value }
    return @{ Body = $body; References = $references -join "`n"; Date = $entry.Groups[1].Value }
}
$english = Read-VersionNotes $ChangelogPath
$chinese = Read-VersionNotes $ChineseChangelogPath
if ($english.Date -ne $chinese.Date) { throw 'English and Chinese release dates must match.' }
$categories = @{ Added = '新增'; Changed = '变更'; Deprecated = '即将弃用'; Removed = '移除'; Fixed = '修复'; Security = '安全' }
foreach ($category in $categories.Keys) {
    $chinese.Body = [regex]::Replace($chinese.Body, "(?m)^### $category\r?$", ('### ' + $categories[$category]))
}
@"
# Fallery $Version

## English

<!-- fallery-notes:en-US -->
$($english.Body)

$($english.References)
<!-- /fallery-notes -->

## 简体中文

<!-- fallery-notes:zh-CN -->
$($chinese.Body)

$($chinese.References)
<!-- /fallery-notes -->
"@
