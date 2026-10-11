param(
    [string]$PetsDirectory = '',
    [switch]$Update
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $repoRoot 'pets\zili-gpt-pet'
$sourceImage = Join-Path $sourceDir 'spritesheet.png'
$sourceMeta = Join-Path $sourceDir 'pet.json'
$expectedHash = '89F4C75FC5BD24CE9F66115FD643746ED6DC48004BC0FDDCCEF574C2E028D671'
if (!(Test-Path -LiteralPath $sourceImage) -or !(Test-Path -LiteralPath $sourceMeta)) {
    throw '请先完整解压交付包，再运行安装脚本。'
}
if ((Get-FileHash -LiteralPath $sourceImage -Algorithm SHA256).Hash -ne $expectedHash) {
    throw '图集校验不一致，未执行安装。请重新解压原始交付包。'
}
$meta = Get-Content -LiteralPath $sourceMeta -Raw -Encoding UTF8 | ConvertFrom-Json
if ($meta.spriteVersionNumber -ne 2 -or $meta.spritesheetPath -ne 'spritesheet.png' -or [string]::IsNullOrWhiteSpace($meta.displayName)) {
    throw '宠物描述格式不正确，未执行安装。'
}
if ([string]::IsNullOrWhiteSpace($PetsDirectory)) {
    if ([string]::IsNullOrWhiteSpace($env:CODEX_HOME)) {
        $petHomePath = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex'
    } else {
        $petHomePath = $env:CODEX_HOME
    }
    $PetsDirectory = Join-Path $petHomePath 'pets'
}
$petsRoot = [IO.Path]::GetFullPath($PetsDirectory).TrimEnd('\','/')
$targetDir = [IO.Path]::GetFullPath((Join-Path $petsRoot 'zili-gpt-pet'))
if (![string]::Equals([IO.Path]::GetDirectoryName($targetDir),$petsRoot,[StringComparison]::OrdinalIgnoreCase)) {
    throw '安装目录检查失败。'
}
if ([string]::Equals([IO.Path]::GetFullPath($sourceDir),$targetDir,[StringComparison]::OrdinalIgnoreCase)) {
    throw '目标已经是交付包中的源目录，请选择客户端实际的 pets 文件夹。'
}
if (Test-Path -LiteralPath $targetDir) {
    if (!$Update) { throw '该目录已存在，未覆盖。更新本交付版请运行“更新紫璃.cmd”，或使用 -Update。' }
    if ((Get-Item -LiteralPath $targetDir).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw '目标是链接目录，未覆盖。请改用普通文件夹。'
    }
    $backupRoot = Join-Path ([IO.Path]::GetDirectoryName($petsRoot)) 'pet-backups'
    $backupDir = Join-Path $backupRoot ('zili-gpt-pet-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
    Copy-Item -LiteralPath $targetDir -Destination $backupDir -Recurse
    Write-Output ('原版本已备份到：' + $backupDir)
}
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
Copy-Item -LiteralPath $sourceImage -Destination (Join-Path $targetDir 'spritesheet.png') -Force
Copy-Item -LiteralPath $sourceMeta -Destination (Join-Path $targetDir 'pet.json') -Force
Write-Output ('紫璃 v3 已放入：' + $targetDir)
Write-Output '请回到原生客户端的宠物设置，点击 Refresh / 刷新，再选择“紫璃 v3”。'
Write-Output '安装只增加本地宠物文件，不会自动切换当前宠物，也不修改账号设置。'
