# 首次准备：安装 BepInEx 6 (IL2CPP) 并生成 Il2CppInterop 互操作程序集
# 说明：这一步必须联网下载 BepInEx。国内直连 GitHub Releases 常常失败，
#       本脚本用 PowerShell 会走 SChannel，若被拦截请改用 Python 或代理下载。
#
#   .\gen-interop.ps1 -GameRoot "D:\Games\PlantsVsZombiesRH"
#
param(
    [Parameter(Mandatory = $true)][string]$GameRoot
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $GameRoot 'GameAssembly.dll'))) {
    Write-Error "'$GameRoot' 看起来不是 IL2CPP 版 PvZ 融合版的游戏目录（找不到 GameAssembly.dll）"
}

if (-not (Test-Path (Join-Path $GameRoot 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'))) {
    Write-Host "尚未安装 BepInEx。" -ForegroundColor Yellow
    Write-Host "请下载 BepInEx 6 IL2CPP 的 win-x64 构建（注意：需要支持 IL2CPP 元数据 v31 的较新版本，"
    Write-Host "官方 releases 里的 v6.0.0-pre.2 太旧会报 'Unsupported metadata version 23-29, got 31'），"
    Write-Host "解压后把 BepInEx\ / dotnet\ / winhttp.dll / doorstop_config.ini / .doorstop_version"
    Write-Host "全部复制到游戏根目录，然后重新运行本脚本。"
    Write-Host ""
    Write-Host "较新构建可在 https://builds.bepinex.dev/projects/bepinex_be 找到" -ForegroundColor Cyan
    exit 1
}

$interop = Join-Path $GameRoot 'BepInEx\interop\Assembly-CSharp.dll'
if (Test-Path $interop) {
    Write-Host "互操作程序集已存在，无需重新生成:" -ForegroundColor Green
    Write-Host "  $interop"
    exit 0
}

Write-Host "启动游戏以生成互操作程序集（首次需要数分钟，会弹出游戏窗口）..." -ForegroundColor Cyan
$p = Start-Process -FilePath (Join-Path $GameRoot 'PlantsVsZombiesRH.exe') -WorkingDirectory $GameRoot -PassThru

$deadline = (Get-Date).AddMinutes(25)
$last = -1; $stable = 0
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 10
    $size = if (Test-Path $interop) { (Get-Item $interop).Length } else { 0 }
    Write-Host ("  Assembly-CSharp.dll = {0} bytes" -f $size)
    $zeros = @(Get-ChildItem (Join-Path $GameRoot 'BepInEx\interop') -File -ErrorAction SilentlyContinue |
               Where-Object { $_.Length -eq 0 }).Count
    if ($size -gt 0 -and $zeros -eq 0) {
        if ($size -eq $last) { $stable += 10 } else { $stable = 0 }
        if ($stable -ge 20) { break }
    } else { $stable = 0 }
    $last = $size
    if ($p.HasExited) { break }
}

if (-not $p.HasExited) {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Get-Process -Name 'PlantsVsZombiesRH' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

if (Test-Path $interop) {
    Write-Host "完成，现在可以编译插件了：.\build.ps1 -GameRoot `"$GameRoot`"" -ForegroundColor Green
} else {
    Write-Error "互操作程序集未生成，请查看 $GameRoot\BepInEx\LogOutput.log"
}
