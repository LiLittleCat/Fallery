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

# 直接执行生产项目的 MSBuild 资源目标，不编译应用；隔离夹具覆盖 publish 漏资源的回归。
$resourceRoot = Join-Path $testRoot 'resources'
$buildDirectory = Join-Path $resourceRoot 'build'
$publishDirectory = Join-Path $resourceRoot 'publish'
$resourcePaths = @('App.xbf', 'Views/MainWindow/MainWindow.xbf', 'Controls/MediaTile.xbf', 'Themes/Icons.xbf')
foreach ($relativePath in $resourcePaths) {
    $resourcePath = Join-Path $buildDirectory $relativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $resourcePath) -Force | Out-Null
    "fixture:$relativePath" | Set-Content -LiteralPath $resourcePath
}
$resourceTargets = @($project.Project.Target | Where-Object { $_.Name -in 'IncludeFalleryPublishResources', 'ValidateFalleryPublishedResources' })
if ($resourceTargets.Count -ne 2) { throw 'Both production publish resource targets must be present.' }
$fixtureProject = Join-Path $resourceRoot 'PublishResources.proj'
$fixtureXml = @'
<Project>
  <PropertyGroup>
    <TargetDir>$(MSBuildThisFileDirectory)build/</TargetDir>
    <PublishDir>$(MSBuildThisFileDirectory)publish/</PublishDir>
  </PropertyGroup>
  <ItemGroup>
    <ApplicationDefinition Include="App.xaml" />
    <Page Include="Views/MainWindow/MainWindow.xaml;Controls/MediaTile.xaml;Themes/Icons.xaml" />
  </ItemGroup>
  <Target Name="ComputeFilesToPublish" />
  <Target Name="Publish" DependsOnTargets="ComputeFilesToPublish">
    <Copy SourceFiles="@(ResolvedFileToPublish)" DestinationFiles="@(ResolvedFileToPublish->'$(PublishDir)%(RelativePath)')" />
  </Target>
  RESOURCE_TARGETS
</Project>
'@
$fixtureXml.Replace('RESOURCE_TARGETS', ($resourceTargets.OuterXml -join "`n")) | Set-Content -LiteralPath $fixtureProject
function Invoke-ResourceFixture([string]$AssemblyName, [string]$ExpectedError) {
    $result = & dotnet msbuild $fixtureProject -nologo -v:minimal -t:Publish "-p:AssemblyName=$AssemblyName" 2>&1
    $code = $LASTEXITCODE
    if ($ExpectedError) {
        if ($code -eq 0 -or ($result -join "`n") -notmatch [regex]::Escape($ExpectedError)) {
            throw "Expected resource failure '$ExpectedError', got exit ${code}: $result"
        }
    }
    elseif ($code -ne 0) { throw "Resource publish fixture failed: $result" }
}
foreach ($assemblyName in @('Fallery', 'Fallery.Preview')) {
    'fixture:pri' | Set-Content -LiteralPath (Join-Path $buildDirectory "$assemblyName.pri")
    Invoke-ResourceFixture $assemblyName
    foreach ($relativePath in @($resourcePaths) + @("$assemblyName.pri")) {
        $sourceHash = (Get-FileHash -LiteralPath (Join-Path $buildDirectory $relativePath)).Hash
        $publishedHash = (Get-FileHash -LiteralPath (Join-Path $publishDirectory $relativePath)).Hash
        if ($sourceHash -ne $publishedHash) { throw "Published resource differs: $relativePath" }
    }
}
Invoke-ResourceFixture 'MissingIndex' 'Missing WinUI resource index'
Remove-Item -LiteralPath (Join-Path $buildDirectory 'Views/MainWindow/MainWindow.xbf')
Invoke-ResourceFixture 'Fallery' 'Missing compiled XAML'
# 最后一项预期失败不能作为整个检查脚本的退出码传给 GitHub Actions。
$global:LASTEXITCODE = 0
Write-Host 'Publish resource checks passed: stable/preview PRI, nested XBF paths and bytes, missing index, and missing main window.'
