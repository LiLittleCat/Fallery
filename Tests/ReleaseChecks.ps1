$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$extract = Join-Path $repoRoot 'tools/Get-ReleaseNotes.ps1'
$testRoot = Join-Path $repoRoot ('work/release-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$fixture = Join-Path $testRoot 'CHANGELOG.md'
$content = @'
# Changelog
## [Unreleased]
### Added
- Future feature.
## [0.2.0] - 2026-11-03
### Fixed
- Selected fix with [docs][guide].
## [0.1.0] - 2026-11-02
### Added
- Older feature.
[guide]: https://example.com/docs
'@
$content | Set-Content -LiteralPath $fixture -Encoding utf8
$notes = & $extract -Version 0.2.0 -ChangelogPath $fixture
if ($notes -notmatch 'Selected fix' -or $notes -match 'Future feature|Older feature|## \[Unreleased\]' -or $notes -notmatch '\[guide\]: https://example.com/docs') {
    throw 'Release notes must include only the selected version and preserve link definitions.'
}
$invalidEntries = @(
    $content.Replace('[0.2.0]', '[0.3.0]')
    ($content + "`n## [0.2.0] - 2026-11-04`n### Fixed`n- Duplicate.")
    "## [0.2.0] - 2026-11-03`n"
)
foreach ($invalid in $invalidEntries) {
    $invalid | Set-Content -LiteralPath $fixture -Encoding utf8
    $rejected = $false
    try { $null = & $extract -Version 0.2.0 -ChangelogPath $fixture }
    catch { $rejected = $true }
    if (!$rejected) { throw 'Missing, duplicate and empty changelog versions must be rejected.' }
}
[xml]$project = Get-Content (Join-Path $repoRoot 'Fallery.csproj')
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0].InnerText
$null = & $extract -Version $version
Write-Host 'Release notes checks passed: version isolation, references, invalid entries, and current project version.'
