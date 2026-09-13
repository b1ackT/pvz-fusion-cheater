# 完全卸载 PvZ 融合版修改器（删除载入器新增的文件，游戏本体不动）
#
#   .\uninstall.ps1 -GameRoot "D:\Games\PlantsVsZombiesRH"
#
param(
    [Parameter(Mandatory = $true)][string]$GameRoot
)

$ErrorActionPreference = 'Continue'

# BepInEx + Doorstop 安装时新增的条目（游戏本体文件不在此列）
$installed = @(
    'BepInEx', 'dotnet', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt'
)

Write-Host "=== 删除载入器文件 ===" -ForegroundColor Cyan
foreach ($i in $installed) {
    $p = Join-Path $GameRoot $i
    if (Test-Path $p) {
        Remove-Item -Recurse -Force $p -ErrorAction SilentlyContinue
        Write-Host "  已删除: $i"
    } else {
        Write-Host "  本来就没有: $i"
    }
}

Write-Host ""
Write-Host "=== 校验游戏本体完整 ===" -ForegroundColor Cyan
$expect = @{
    'GameAssembly.dll'        = 57717248
    'UnityPlayer.dll'         = 31105872
    'PlantsVsZombiesRH.exe'   = 666624
    'baselib.dll'             = 419152
    'UnityCrashHandler64.exe' = 1184592
}
foreach ($k in $expect.Keys) {
    $p = Join-Path $GameRoot $k
    if (-not (Test-Path $p)) { Write-Host ("  {0,-26} 缺失!" -f $k) -ForegroundColor Red; continue }
    $len = (Get-Item $p).Length
    $ok = if ($len -eq $expect[$k]) { 'OK' } else { '大小不符' }
    Write-Host ("  {0,-26} {1,10}  {2}" -f $k, $len, $ok)
}

Write-Host ""
Write-Host "存档目录（如需还原，从你的备份覆盖回去）:" -ForegroundColor Yellow
Write-Host "  %USERPROFILE%\AppData\LocalLow\LanPiaoPiao\PlantsVsZombiesRH\"
