# PvZ 融合版 3.9 修改器（BepInEx 6 + Harmony + 游戏内菜单）

基于 IL2CPP 逆向（`dump.cs` + Il2CppInterop 互操作程序集）编写的运行时插件。
**不改动任何游戏本体文件**：所有修改在运行时通过 Harmony 打补丁完成。

---

## 1. 游戏内菜单（经典外挂菜单风格）

启动游戏后**左上角会出现一个半透明深色面板**，可拖动标题栏移动，滚轮滚动内容。

| 热键 | 作用 |
|---|---|
| **INSERT** | 显示 / 隐藏菜单 |
| **F3** | 显示 / 隐藏 ESP 方框 |
| **F4** | 切换"全局植物默认"是否生效 |
| 标题栏 `-` | 折叠 / 展开 |
| 标题栏 `X` | 关闭菜单（等同 INSERT） |

菜单共 4 个页签：

### FEATURES
所有全局功能的可视化开关与滑条：解锁/资源、战斗（植物无敌、一击必杀、伤害倍率）、
旅行词条（含**词条白名单输入框**）、天赋、总开关、施加间隔。

### GLOBAL PLANT
对所有植物统一生效的修改，和单株设置叠加（单株优先）。

### PLANTS
- **LIVE PLANTS 列表**：场景中每株植物一行（`#序号 类型名 血量`），点一行进入编辑
- **编辑页**：机制（免伤/invincible/undead/keepShooting/alwaysLightUp/uncrashable）、
  数值（最大生命/攻击力/等级/攻击间隔/攻速倍率/伤害倍率/防御）、
  模型（skinType/缩放 + 「Refresh sprite now」）、
  子弹（BulletType id / 子弹伤害倍率 / 子弹速度倍率 / 穿透）
- 「Copy this to GLOBAL defaults」把当前这株的设置变成全场默认
- 「Reset this plant」清掉这株的覆盖

### ESP / KEYS
ESP 显示项（方框/名字/血量/序号）、标签高度与宽度、热键说明、清空全部单株覆盖。

### ESP 方框
每株植物头顶画一个方框（深色＝普通，红色＝当前选中），内容为 `类型名 当前血量/最大血量`。
**左键点方框 = 选中该植物并自动跳到 PLANTS 页签的编辑界面。**

> 界面**不透明深色面板**；开关项用**字体里的 `✓` 字形**表示已开启（绿底白勾），未开启显示空框。
> 文案**中文优先**：运行时用 `Font.HasCharacter('中')` 实测，字体缺中文字形时自动回退英文。
> 所有改动在**下一个周期（默认 0.25 秒）**写入植物字段。

### 默认全部关闭

**所有作弊项默认都是关闭的**，插件加载后不会改动任何东西，需要你在菜单里自己打开。

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `Enabled` | `true` | 总开关（只是让插件生效，不代表开启任何作弊） |
| `UnlockAllLevels` / `DeveloperMode` | `false` | |
| `Money` | `0` | **0 = 不改动金币** |
| `InfiniteSun` / `GodModePlants` / `OneHitZombies` | `false` | |
| `PlantDamageMultiplier` / `ZombieDamageTakenMultiplier` | `1` | 1 = 原样 |
| `TravelBuffs` / `TalentUnlockAll` / `DisableHardMode` | `false` | |
| `TalentStars` | `0` | **0 = 不改动** |
| `AbyssMaxTickets` / `AbyssInfiniteTickets` | `false` | |
| `DamageReduction` / `DamageAmplification` | `0` / `1` | 中性值 |

> 若你之前用过旧版本（默认全开），请删除 `BepInEx\config\com.dsh.pvzrh.cheat.cfg`
> 让新默认值生效（旧文件已备份为 `_mod\config_old_backup.cfg`）。

---

## 2. 功能总览

| 分组 | 功能 | 实现方式（补丁点） |
|---|---|---|
| 解锁 | 全部关卡（冒险/挑战/小游戏/生存/探索/皮肤） | `GameAPP.Awake` 后置：填充 `advLevelCompleted` 等 |
| 解锁 | 金币拉满 / 开发者模式 | `GameAPP.theMoneyCount` / `developerMode` |
| 关卡资源 | 无限阳光 + 自动维持下限 | 前缀跳过 `Board.UseSun`；周期回填 `Board.SetSun` |
| 关卡资源 | 无限关卡内金币 | 前缀跳过 `Board.UseMoney` |
| 战斗 | 植物无敌（全局） | 前缀跳过 `Plant.TakeDamage`/`RealTakeDamage`/`DecreaseHealth` + 周期回满血 |
| 战斗 | 输出/受伤倍率、一击必杀 | 前缀改写 `Zombie.TakeDamage` 的伤害值 |
| 战斗 | **单株/全局植物修改（UI）** | 周期写入 `Plant` 字段；子弹在 `Bullet.InitData` 后置改写 |
| 旅行/词条 | 伤害减免、幸运一击、伤害增幅、`plantZeroHealth` | 周期写入 `TravelMgr.Instance` 字段 |
| **旅行/词条** | **改抽词条结果**（白名单） | 后置过滤 `TravelMgr.GetAdvancedBuffPool` / `GetUltiBuffPool` |
| 天赋 | 天赋树全解锁 + 星星拉满 + 关困难模式 | 后置 `AdvantureData.OnInit` |
| 深渊 | 抽奖券拉满 / 不消耗 | 后置 `AbyssData.GetTicket`；前缀改写 `UseTicket` |

---

## 3. 配置文件与"改抽词条"

菜单里改的开关会同步写回配置文件；也可以直接改文件（BepInEx 会热重载）。

```
BepInEx\config\com.dsh.pvzrh.cheat.cfg
```

### 改"抽词条"结果

**直接接管随机池**，不是改概率——池子里只剩你写的词条，所以**每抽必中**：

```ini
[4-Travel]
BuffWhitelist = 撒豆成兵,百步穿杨,妙手回春,势如破竹
UltiBuffWhitelist = 嗜血如命,力大砖飞,流星雨
```

留空 = 不干预。名称必须与枚举完全一致。
完整名单见 `_mod\dump\dump.cs` 里的 `AdvBuff` / `UltiBuff` 枚举。

- 写错的名字不会崩，日志会打 `[词条白名单] 无法识别的名称: xxx`
- 若过滤后池子为空，会自动放弃本次过滤并告警（避免卡在抽取界面）

> 冒险模式**天赋树没有随机抽取**（是靠星星购买的确定性树），
> 所以 `TalentUnlockAll=true` + `TalentStars=9999` 即为全天赋到手。

---

## 4. 安装 / 卸载

**安装**：游戏根目录需有 `winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`、`BepInEx\`、`dotnet\`，
插件本体在 `BepInEx\plugins\PvzRhCheat.dll`。

**卸载**：运行 `_mod\uninstall.ps1`（删除新增文件并校验游戏本体完整）。
存档备份在 `_mod\backup\saves_*`。

日志：`BepInEx\LogOutput.log`

---

## 5. 关键技术发现（为什么会"没字"）

这次排障挖出了两个**这套 IL2CPP 构建特有的坑**，都不是代码写错：

### 5.1 默认 GUI 皮肤的字体是 NULL

```
[UI] skin.label.font=NULL  fontSize=0
[UI] 已为皮肤指派字体: LegacyRuntime
```

游戏打包时把 IMGUI 默认皮肤的字体资源**剥离**了（游戏自己不用 IMGUI，所以被裁掉）。
`font=NULL` 意味着**任何 IMGUI 文字都渲染不出来**——面板画出来但一片空白。
插件启动时用 `Resources.FindObjectsOfTypeAll<Font>()` 从游戏已加载资源里抓一个字体补上。

### 5.2 `GUI.DrawTexture` 被剥离 → 面板半透明且画不出来

一次性 API 探针的实测结果：

```
[探针] 该构建不支持 GUI.DrawTexture (NotSupportedException)
```

而 `GUI.Box` / `GUI.Label` / `GUI.BeginGroup` / `GUIStyle.*` 都正常。
原本用 `GUI.DrawTexture` 画所有纯色矩形，**每帧抛异常导致整个菜单画不出来**。

解决办法（同时解决"太透明"）——**把 `box` 样式的背景贴图换成 1×1 纯白贴图**：

```csharp
GUI.skin.box.normal.background = Texture2D.whiteTexture;
```

这样 `GUI.Box(rect, "")` 就等于用 `GUI.color` 画一块**完全不透明的纯色矩形**，
既绕开了被剥离的 `GUI.DrawTexture`，又拿到了精确的透明度与颜色控制：

```
[UI] 填充方式 / 已把 box 背景替换为纯白贴图（不透明填充）
```

### 5.3 对勾用字体字形

开关状态直接用字体里的 `✓`。运行时先探测：

```csharp
f.HasCharacter('\u2713')   // ✓
f.HasCharacter('\u221A')   // √
f.HasCharacter('\u2714')   // ✔
// 都没有才退回 ASCII 的 "v"
```

实测这台机器上 `LegacyRuntime` 就有 `✓`，所以直接用字形，**清晰不发糊**
（早期版本用一堆小方块手绘对勾，会糊，已废弃）。
另外所有矩形都 `Mathf.Round` 取整到整像素，避免亚像素采样发虚。

### 5.4 中文可用性实测

```
[UI] 字体=LegacyRuntime  含中文=True  文案=中文
```

补上的 `LegacyRuntime` 动态字体实测 `HasCharacter('中') == true`，所以中文正常显示。
插件仍保留自动回退：若换成不含中文字形的字体，界面会自动切英文，不会变成空白。

> 这三条对以后给这个游戏写任何 IMGUI 工具都适用。

---

## 6. 实测验证

补丁加载 **14/14 全部成功**（`Patch_GameApp_Awake/_Update`、`Board_UseSun/_UseMoney`、
`Plant_TakeDamage/_RealTakeDamage/_DecreaseHealth`、`Zombie_TakeDamage`、`Bullet_InitData`、
`Travel_AdvBuffPool/_UltiBuffPool`、`Advanture_OnInit`、`Abyss_GetTicket/_UseTicket`）。

游戏把结果持久化到 `playerData.json`，证明修改真实生效：

| 字段 | 原始 | 修改后 |
|---|---|---|
| `theMoneyCount` | 190 | **999999999** |
| `advLevelCompleted` | 部分 | **128/128** |
| `clgLevelCompleted` | 部分 | **256/256** |
| `gameLevelCompleted` | 部分 | **128/128** |
| `survivalLevelCompleted` | 部分 | **128/128** |

无崩溃：Unity `Player.log` 以正常退出的内存统计收尾，无 Exception、无 crash dump。

---

## 7. 已知限制（诚实说明）

1. **`Effects` 只记录不生效**。游戏效果走 `Dictionary<EffectType, BaseEffect>` 与 `eveBuffs`，
   正确施加需要调用未知语义的方法并构造 Effect 实例。没反编译方法体的前提下贸然调用风险过高，
   所以做成"记录+日志"。需要真生效请指定具体效果类型，我再去定位对应方法。
2. **`skinType` 不一定立刻换外观**。已接 `ReplaceSprite()`，但部分子类可能重写外观逻辑。
3. **单株覆盖按对象指针索引**。植物销毁后若新植物分配到同一地址，旧覆盖会套到新植物。
   切关卡后建议按一下「Clear ALL per-plant overrides」。
4. **攻速倍率**会先缓存该植物原始 `thePlantAttackInterval` 再换算，避免周期施加反复相除导致指数级加速（已修）。
5. **子弹覆盖在 `Bullet.InitData` 后置生效**，每颗子弹只处理一次（防重复翻倍，已加去重）。
6. **虚方法重写**：`Plant.TakeDamage`/`Zombie.TakeDamage` 是虚方法，补的是基类实现；
   子类若重写则那条路径拦不住。植物无敌额外加了"周期回满血"兜底。
7. `plantZeroHealth` 语义由字段名推断，未反编译方法体验证；异常就把该项关掉。
8. **未做动态调试**：本插件是"按签名打补丁 + 运行时日志验证"的产物，没有下断点逐帧跟踪。
   功能无效时把 `BepInEx\LogOutput.log` 里 `[FAIL]`/`[探针]`/`[词条白名单]` 开头的行发我。
9. 补丁按类型逐个 try/catch，单个失败不影响其它功能。

---

## 8. 工程文件

```
_mod\
├─ plugin\                    C# 插件源码（net6.0，离线编译）
│   ├─ PvzRhCheat.csproj
│   ├─ Plugin.cs              入口 + 安全打补丁 + 注入叠加层
│   ├─ ModConfig.cs           全部配置项
│   ├─ Patches.cs             公共逻辑 + 14 个补丁类 + 植物列表/选中
│   ├─ Overrides.cs           单株/全局植物覆盖 + 子弹覆盖
│   └─ EspOverlay.cs          经典外挂菜单（手绘）+ ESP 方框 + API 探针
├─ dump\                      Il2CppDumper 全量导出（dump.cs / il2cpp.h / DummyDll）
├─ tools\                     BepInEx be.788、Il2CppDumper、apidump（自研 API 转储器）
├─ nuget-local\               离线 net6.0 targeting pack
├─ backup\saves_*             存档备份
├─ run_interop_gen.ps1        启动游戏生成互操作程序集
├─ test_plugin.ps1            安装插件并抓日志验证
└─ uninstall.ps1              一键回滚
```

重新编译并安装：

```powershell
cd _mod\plugin
dotnet build -c Release
copy bin\Release\PvzRhCheat.dll ..\..\BepInEx\plugins\
```
