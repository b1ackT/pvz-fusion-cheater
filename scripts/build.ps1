# 编译并安装 PvZ 融合版修改器
#
#   .\build.ps1 -GameRoot "D:\Games\PlantsVsZombiesRH"
#
param(
    [Parameter(Mandatory = $true)][string]$GameRoot
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $here '..\src\PvzRhCheat.csproj'

if (-not (Test-Path (Join-Path $GameRoot 'BepInEx\core\BepInEx.Core.dll'))) {
    Write-Error "在 '$GameRoot' 下找不到 BepInEx。请先安装 BepInEx 6 (IL2CPP) 并运行游戏一次。"
}
if (-not (Test-Path (Join-Path $GameRoot 'BepInEx\interop\Assembly-CSharp.dll'))) {
    Write-Error "找不到 BepInEx\interop\Assembly-CSharp.dll。请先启动一次游戏，让 BepInEx 生成互操作程序集。"
}

Write-Host "编译中..." -ForegroundColor Cyan
dotnet build $proj -c Release -p:GameRoot="$GameRoot" --nologo
if ($LASTEXITCODE -ne 0) { Write-Error "编译失败" }

$out = Join-Path $here '..\src\bin\Release\PvzRhCheat.dll'
$dst = Join-Path $GameRoot 'BepInEx\plugins\PvzRhCheat.dll'
New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
Copy-Item -Force $out $dst

Write-Host ""
Write-Host "已安装: $dst" -ForegroundColor Green
Write-Host "启动游戏后按 INSERT 打开菜单（F3 开关 ESP，F4 切换全局植物默认）" -ForegroundColor Green
