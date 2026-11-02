[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$')][string]$Version,
    [ValidateSet('stable', 'preview')][string]$Channel = 'stable',
    [Parameter(Mandatory)][string]$ArtifactsDirectory,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository = 'LiLittleCat/Fallery',
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Commit,
    [string]$ReleaseNotes,
    [switch]$Publish,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Channel -eq 'stable' -and $Version.Contains('-')) { throw '正式通道不能使用预览版本号。' }
if ($Channel -eq 'preview' -and !$Version.Contains('-')) { throw '预览通道必须使用预览版本号。' }
$artifactRoot = (Resolve-Path -LiteralPath $ArtifactsDirectory).Path
$manifests = @(Get-ChildItem -LiteralPath $artifactRoot -Filter 'build-*.json' -File -Recurse)
if ($manifests.Count -ne 2) { throw '一次发版必须同时包含 x64 与 ARM64 的构建清单。' }
$seen = @()
$uploadFiles = @()
foreach ($manifest in $manifests) {
    $build = Get-Content -LiteralPath $manifest.FullName -Raw | ConvertFrom-Json
    if ($build.version -ne $Version -or $build.channel -ne $Channel -or $build.commit -ne $Commit -or $build.architecture -notin @('x64', 'arm64') -or $build.architecture -in $seen) {
        throw '版本、通道、源码提交或架构不一致，拒绝发布。'
    }
    $seen += $build.architecture
    if ($build.feedChannel -ne "$Channel-win-$($build.architecture)") { throw '更新通道与架构不一致。' }
    if (!$ValidateOnly -and $build.sourceDirty) { throw '构建包含未提交改动，请提交源码后重新构建，再创建正式 Release。' }
    $directory = $manifest.DirectoryName
    $checksumFile = Join-Path $directory "SHA256SUMS-$($build.feedChannel).txt"
    foreach ($line in Get-Content -LiteralPath $checksumFile) {
        if ($line -notmatch '^([a-fA-F0-9]{64})  (.+)$') { throw '校验和文件格式错误。' }
        $hash = $Matches[1]; $name = $Matches[2]
        if ([IO.Path]::GetFileName($name) -ne $name) { throw '校验和文件包含非法路径。' }
        $path = Join-Path $directory $name
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hash) { throw "发布文件已变化：$name" }
        $uploadFiles += $path
    }
    $uploadFiles += $checksumFile
}
if (@($uploadFiles | ForEach-Object { [IO.Path]::GetFileName($_) } | Group-Object | Where-Object Count -gt 1).Count) { throw '两种架构的资产文件名重复。' }
if ($ValidateOnly) { Write-Host "本地发布校验通过：$($uploadFiles.Count) 个资产，两个架构。未上传文件。"; return }
if (!(Get-Command gh -ErrorAction SilentlyContinue)) { throw '请先安装 GitHub CLI 并登录，或在 CI 设置 GH_TOKEN。' }
if (!$ReleaseNotes) {
    $notes = & (Join-Path $PSScriptRoot 'Get-ReleaseNotes.ps1') -Version $Version
    $notesDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'work/release-notes'
    New-Item -ItemType Directory -Path $notesDirectory -Force | Out-Null
    $ReleaseNotes = Join-Path $notesDirectory "$Version.md"
    $notes | Set-Content -LiteralPath $ReleaseNotes -Encoding utf8
}
$ReleaseNotes = (Resolve-Path -LiteralPath $ReleaseNotes).Path
$tag = "v$Version"
function Invoke-Gh([string[]]$Arguments) {
    & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub 操作失败，退出码 $LASTEXITCODE" }
}
# 只允许重试同一提交的草稿，已经发布的版本必须使用新的版本号。
$existingJson = & gh release view $tag --repo $Repository --json isDraft,targetCommitish,isPrerelease 2>$null
if ($LASTEXITCODE -eq 0) {
    $existing = $existingJson | ConvertFrom-Json
    if (!$existing.isDraft -or $existing.targetCommitish -ne $Commit -or $existing.isPrerelease -ne ($Channel -eq 'preview')) {
        throw '已存在不可覆盖的发行版；只能重试同一提交、同一通道的草稿。'
    }
} else {
    $tagCommit = & gh api "repos/$Repository/commits/$tag" --jq .sha 2>$null
    if ($LASTEXITCODE -eq 0 -and $tagCommit -ne $Commit) { throw '现有标签指向不同的源码提交。' }
    $arguments = @('release', 'create', $tag, '--repo', $Repository, '--target', $Commit, '--title', "Fallery $Version", '--notes-file', $ReleaseNotes, '--draft')
    if ($Channel -eq 'preview') { $arguments += '--prerelease' }
    Invoke-Gh $arguments
}
Invoke-Gh (@('release', 'upload', $tag, '--repo', $Repository, '--clobber') + $uploadFiles)
Invoke-Gh @('release', 'edit', $tag, '--repo', $Repository, '--notes-file', $ReleaseNotes)
if ($Publish) { Invoke-Gh @('release', 'edit', $tag, '--repo', $Repository, '--draft=false') }
Write-Host "发行页面：https://github.com/$Repository/releases"
