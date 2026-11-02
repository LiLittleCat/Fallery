[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$')][string]$Version,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [ValidateSet('stable', 'preview')][string]$Channel = 'stable',
    [string]$UpdateSource = 'https://github.com/LiLittleCat/Fallery',
    [string]$OutputRoot,
    [string]$ReleaseNotes,
    [switch]$SkipChecks
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
if ($Channel -eq 'stable' -and $Version.Contains('-')) { throw '正式通道不能使用预览版本号。' }
if ($Channel -eq 'preview' -and !$Version.Contains('-')) { throw '预览通道须使用例如 0.1.0-preview.1 的版本号。' }
$uri = $null
if (![Uri]::TryCreate($UpdateSource, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) {
    throw '更新源必须是没有凭据、查询参数或片段的 HTTPS 地址。'
}
if ($uri.Host -eq 'github.com' -and $uri.AbsolutePath.Trim('/').Split('/').Count -ne 2) { throw 'GitHub 更新源必须指向仓库首页。' }
if (!$OutputRoot) { $OutputRoot = Join-Path $repoRoot 'work/releases' }
$releaseDirectory = [IO.Path]::GetFullPath((Join-Path $OutputRoot "$Channel/$Version/win-$Architecture"))
if ((Test-Path -LiteralPath $releaseDirectory) -and (Get-ChildItem -LiteralPath $releaseDirectory -Force | Select-Object -First 1)) {
    throw "输出目录非空，避免混入旧包，请指定新的 OutputRoot：$releaseDirectory"
}
if ($ReleaseNotes) { $ReleaseNotes = (Resolve-Path -LiteralPath $ReleaseNotes).Path }
else { $ReleaseNotes = Join-Path $repoRoot 'CHANGELOG.md' }
$buildRoot = Join-Path $repoRoot ('work/release-builds/' + [Guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $buildRoot 'publish'
$packageId = if ($Channel -eq 'stable') { 'Fallery.App' } else { 'Fallery.Preview.App' }
$mainExe = if ($Channel -eq 'stable') { 'Fallery.exe' } else { 'Fallery.Preview.exe' }
$title = if ($Channel -eq 'stable') { 'Fallery' } else { 'Fallery Preview' }
$feedChannel = "$Channel-win-$Architecture"

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet 操作失败，退出码 $LASTEXITCODE" }
}

Push-Location $repoRoot
try {
    Invoke-DotNet @('tool', 'restore')
    if (!$SkipChecks) {
        Invoke-DotNet @('run', '--project', 'Tests/CoreChecks/CoreChecks.csproj', '--', $repoRoot)
        Invoke-DotNet @('run', '--project', 'Tests/UpdateChecks/UpdateChecks.csproj')
    }
    $platform = if ($Architecture -eq 'arm64') { 'ARM64' } else { 'x64' }
    Invoke-DotNet @('publish', 'Fallery.csproj', '-c', 'Release', "-p:Platform=$platform", '-r', "win-$Architecture",
        '--self-contained', 'true', '--artifacts-path', (Join-Path $buildRoot 'artifacts'), '-o', $publishDirectory,
        "-p:Version=$Version", "-p:FalleryChannel=$Channel", "-p:FalleryUpdateSource=$UpdateSource")
    if (!(Test-Path -LiteralPath (Join-Path $publishDirectory $mainExe))) { throw "缺少入口程序 $mainExe" }
    New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
    $packArguments = @('tool', 'run', 'vpk', '--', 'pack', '--packId', $packageId, '--packVersion', $Version,
        '--packTitle', $title, '--packAuthors', 'LiLittleCat', '--packDir', $publishDirectory, '--mainExe', $mainExe,
        '--channel', $feedChannel, '--runtime', "win-$Architecture", '--outputDir', $releaseDirectory,
        '--icon', (Join-Path $repoRoot 'Assets/Fallery.ico'), '--releaseNotes', $ReleaseNotes, '--delta', 'None')
    # 签名参数通过 VPK_SIGN_PARAMS / VPK_AZURE_TRUSTED_SIGN_FILE 由打包工具读取，不写入仓库或日志。
    Invoke-DotNet $packArguments
    $feedPath = Join-Path $releaseDirectory "releases.$feedChannel.json"
    if (!(Test-Path -LiteralPath $feedPath)) { throw '打包未生成更新清单。' }
    $assets = @((Get-Content -LiteralPath $feedPath -Raw | ConvertFrom-Json).Assets)
    if (!$assets.Count -or @($assets | Where-Object { $_.PackageId -ne $packageId -or $_.Version -ne $Version }).Count) { throw '更新清单的产品或版本不一致。' }
    foreach ($asset in $assets) {
        if ([IO.Path]::GetFileName($asset.FileName) -ne $asset.FileName) { throw '更新清单包含非法包路径。' }
        $package = Join-Path $releaseDirectory $asset.FileName
        if ((Get-Item -LiteralPath $package).Length -ne $asset.Size -or (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $asset.SHA256) { throw '生成的更新包校验失败。' }
    }
    $commit = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw '无法取得源码提交号。' }
    $sourceChanges = @(& git status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw '无法检查源码工作区。' }
    [ordered]@{ version = $Version; channel = $Channel; architecture = $Architecture; packageId = $packageId;
        feedChannel = $feedChannel; updateSource = $UpdateSource; commit = $commit; sourceDirty = ($sourceChanges.Count -gt 0); builtUtc = [DateTime]::UtcNow.ToString('O') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseDirectory "build-$feedChannel.json") -Encoding utf8
    Get-ChildItem -LiteralPath $releaseDirectory -File | Sort-Object Name | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    } | Set-Content -LiteralPath (Join-Path $releaseDirectory "SHA256SUMS-$feedChannel.txt") -Encoding utf8
    Write-Host "安装包与更新文件：$releaseDirectory"
    Write-Host "发布源码目录：$publishDirectory"
}
finally { Pop-Location }
