# PvZ 融合版 3.9 修改器

用 **BepInEx 6 + Harmony** 给《植物大战僵尸 融合版 3.9》（Unity 2022.3.62f1c1 / IL2CPP）写的运行时修改器，
带一个**游戏内经典外挂菜单**和**植物 ESP**。

> 本仓库**只包含修改器本身的代码**（C# 源码 / 逆向脚本 / 文档），
> **不包含任何游戏文件、BepInEx 二进制、游戏资源或存档**。

---

## 功能

**游戏内菜单**（INSERT 开关，半透明深色面板，可拖标题栏、滚轮滚动，中文界面，开关用 `✓` 标记）

| 分组 | 功能 |
|---|---|
| 解锁 | 全部关卡解锁、开发者模式、金币 |
| 关卡资源 | 无限阳光（不消耗 + 维持下限）、无限关卡内金币 |
| 战斗 | 植物无敌、僵尸一击必杀、植物输出倍率、僵尸受伤倍率 |
| 旅行/词条 | 伤害减免、幸运一击、伤害增幅、`plantZeroHealth` |
| 旅行/词条 | **改抽词条结果**：白名单直接接管随机池（不是改概率，是每抽必中） |
| 天赋 | 冒险模式天赋树全解锁、星星、关闭困难模式 |
| 深渊 | 抽奖券拉满 / 不消耗 |

**植物 ESP + 单株编辑器**

- 每株植物头顶画信息框（类型名 / 血量），点击即选中并进入编辑
- **机制**：免伤、`invincible`、`undead`、`keepShooting`、`alwaysLightUp`、`uncrashable`
- **数值**：最大生命、攻击力、等级、攻击间隔、攻速倍率、伤害倍率、防御
- **模型**：`skinType`、缩放、立即刷新外观
- **子弹**：`BulletType`、子弹伤害倍率、速度倍率、穿透次数
- **全局批量**：把这些设置一键复制成全场植物默认值

> 所有作弊项**默认关闭**，加载后不改动任何东西，需要自己打开。

---

## 环境要求

- 游戏：PvZ 融合版 3.9（Unity 2022.3.62f1c1，IL2CPP，64 位）
- [BepInEx 6 **IL2CPP** win-x64](https://builds.bepinex.dev/projects/bepinex_be)
  - ⚠️ GitHub Releases 上的 `v6.0.0-pre.2` **太旧**，对不上本游戏的 IL2CPP 元数据 v31
    （会报 `Unsupported metadata version found! We support 23-29, got 31`）。
    请用 `builds.bepinex.dev` 上较新的构建（本插件在 **be.788** 上验证通过）。
- 编译：.NET SDK（本插件目标框架 `net6.0`）

---

## 安装

1. 把 BepInEx 6 (IL2CPP) 解压内容（`BepInEx\`、`dotnet\`、`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`）
   复制到游戏根目录
2. **启动一次游戏**，让 BepInEx 用 Cpp2IL 生成互操作程序集（会弹出游戏窗口，首次需数分钟）
   ```powershell
   .\scripts\gen-interop.ps1 -GameRoot "D:\Games\PlantsVsZombiesRH"
   ```
3. 编译并安装插件
   ```powershell
   .\scripts\build.ps1 -GameRoot "D:\Games\PlantsVsZombiesRH"
   ```
4. 启动游戏，按 **INSERT** 打开菜单

也可以用现成的 `release\PvzRhCheat.dll`，直接丢进 `BepInEx\plugins\`。

### 卸载

```powershell
.\scripts\uninstall.ps1 -GameRoot "D:\Games\PlantsVsZombiesRH"
```

---

## 使用

| 热键 | 作用 |
|---|---|
| **INSERT** | 显示 / 隐藏菜单 |
| **F3** | 显示 / 隐藏 ESP |
| **F4** | 切换"全局植物默认"是否生效 |

菜单四个页签：**功能** / **全局植物** / **植物** / **ESP·热键**。
点 ESP 方框会跳到该植物的编辑页。

### 改抽词条结果

`功能` 页签里的两个白名单输入框（或直接改配置文件）：

```ini
[4-Travel]
BuffWhitelist = 撒豆成兵,百步穿杨,妙手回春,势如破竹
UltiBuffWhitelist = 嗜血如命,力大砖飞,流星雨
```

**这是直接接管随机池**，池子里只剩你写的词条，所以每抽必中。留空 = 不干预。
写错的名字不会崩，日志会提示。

配置位置：`BepInEx\config\com.dsh.pvzrh.cheat.cfg`

---

## 技术要点

这套游戏的构建有几个坑，插件在运行时做了自动适配（详细记录见
[docs/USAGE.zh-CN.md](docs/USAGE.zh-CN.md) 第 5 节）：

| 问题 | 现象 | 处理 |
|---|---|---|
| `GUI.skin.label.font == NULL`（皮肤字体被 IL2CPP 剥离） | 任何 IMGUI 文字都渲染不出来，面板一片空白 | `Resources.FindObjectsOfTypeAll<Font>()` 抓字体补上 |
| `GUI.DrawTexture` 被剥离（调用即 `NotSupportedException`） | 每帧抛异常，整个菜单画不出来 | 把 `GUI.skin.box.normal.background` 换成 `Texture2D.whiteTexture`，用 `GUI.Box` 画不透明纯色 |
| `GUI.TextField` 被剥离 | 输入框不可用 | 自绘输入框，捕获 `Event.character` / Backspace |

界面还做了：中文优先（`Font.HasCharacter('中')` 实测，缺字形自动回退英文）、
勾选用字体 `✓` 字形、所有矩形取整到整像素避免发糊。

---

## 仓库结构

```
src/          插件源码（C#，net6.0）
scripts/      编译 / 安装 / 卸载 / 生成互操作程序集
research/     逆向分析用的工具脚本（IL2CPP 元数据解析、PE 分析、interop API 转储）
docs/         使用说明与逆向分析报告
release/      预编译好的插件 DLL
```

`research/` 里的脚本需要自备 `GameAssembly.dll` 与 `global-metadata.dat` 才能跑，
仓库里不提供这两者。

### 编译

```powershell
dotnet build src\PvzRhCheat.csproj -c Release -p:GameRoot="D:\Games\PlantsVsZombiesRH"
```

`GameRoot` 指向游戏目录，用于引用 `BepInEx\core\*.dll` 与 `BepInEx\interop\*.dll`。

---

## 免责声明

- 仅供**单人游戏**的本地学习与娱乐使用，请勿用于任何联机作弊或商业用途。
- 本仓库不含游戏本体、游戏资源或任何版权素材；游戏版权归其作者所有。
- 修改存档前请自行备份（存档在 `%USERPROFILE%\AppData\LocalLow\LanPiaoPiao\PlantsVsZombiesRH\`）。
- 使用本插件造成的一切后果由使用者自负。

## 许可

本仓库中**我自己编写的代码**采用 [MIT](LICENSE) 许可。
`research/` 中涉及第三方格式解析的部分仅用于互操作性研究。
