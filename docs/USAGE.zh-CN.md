# PvZ 融合版 3.9 修改器（BepInEx 6 + Harmony + **游戏内**菜单）

基于 IL2CPP 逆向（`dump.cs` + Il2CppInterop 互操作程序集）编写的运行时插件。
**不改动任何游戏本体文件**：所有修改在运行时通过 Harmony 打补丁完成。

> **界面就在游戏里**（IMGUI 叠加层），不需要外置窗口。
> 所有数据都在同一个进程内直接读写游戏对象，**不经过 IPC / 序列化**，
> 所以不存在"传输丢数据"的问题。

---

## 1. 游戏内菜单

进游戏后屏幕左上角出现一个**不透明深色面板**，拖标题栏可移动。
**标题栏上写着版本号**（当前 `v1.2.0（内置界面 · 6 页签）`）——
如果你看到的不是这个，说明游戏里跑的还是旧 DLL，重启游戏即可。

> **怎么确认自己看的是"内置菜单"而不是旧的外置窗口**：
> 内置菜单的标题栏有 `ESP: 开` / `隐藏 (F1)` / `×` 三个按钮，
> 下面是 **6 个页签**（功能开关 · 植物 · 融合 · 沙盒 · 作弊动作 · 设置）。
> 外置的 `PvzRhCheatUi.exe` 是 WinForms 窗口，**已经不自动启动了**，也不需要再用了。

「功能开关」页默认**把 10 个分组全部展开**，所以一进去就能看到全部开关；
右上角有 `展开全部 / 折叠全部`，右侧有滚动条和 `▲▼`，底部会写
`共 N 行，当前显示第 a ~ b 行`。滚轮也可以滚。

| 热键 / 操作 | 作用 |
|---|---|
| **F1** | 显示 / 隐藏菜单（**可自定义**，见「设置」页） |
| **F3** | 显示 / 隐藏植物 ESP 方框（**可自定义**） |
| **鼠标左键点 ESP 方框** | 选中那株植物 |
| 编辑数值时 **回车** / **ESC** | 确认 / 取消（退格删除；筛选框可以直接打字）|
| 标题栏 `隐藏(F1)` / `×` | 关掉菜单 |

> **热键可以在游戏内改**：「设置」页有两个按钮（菜单键 / ESP 键），点一下再按你想要的键即可，
> 按 Esc 取消。改完写进配置文件，下次启动依然有效；**默认 F1 / F3**。
> 建议用 F1~F12 或 Insert 这类游戏本身不占用的键。
>
> **鼠标压在菜单上时，游戏不响应点击** —— 插件给 `Mouse.Update` 打了前缀补丁，
> 所以点菜单不会顺手在游戏里种植物、点卡片。滚轮同理（压在面板上时会被菜单吃掉）。

菜单 6 个页签：

### 功能开关
全部 40+ 个开关，按 10 组折叠（**默认全部展开**）：通用 / 解锁与资源 / 关卡内 / 战斗 / 旅行词条 /
天赋 / 深渊抽奖券 / 经典作弊 / **速度·僵尸·工具** / 界面与 ESP。
布尔项是**绿底勾选框**；数值项点一下就能**直接在面板上打字改**，回车生效并写回 `BepInEx\config`。

> **勾是自绘的，不用字体字形。** 一开始我用字体里的 `✓`（U+2713），
> 但 Bold + 大字号下它渲染出来是一坨亮绿色的大方块（就是那个"超大绿色像素"），
> 现在改成用整数坐标的小方块拼一个勾（`UiSkin.DrawCheck`），跟字体完全无关，
> 任何字号都清晰。启动日志里会写 `含✓字形=True/False`，只作记录。

### 植物
- 左栏：**场上植物列表**（`中文名 #编号  当前血量/上限`），点一行即选中；含「取消选择」
- 右栏：**该植物的全部实时数值**，两列排布：
  血量上限 / 当前血量 / 攻击力 / 攻击间隔 / 攻速倍率 / 伤害倍率 / 防御 / 等级 / 阶段 /
  植物类型 / 皮肤编号 / 缩放 / 子弹类型 / 子弹伤害倍率 / 子弹速度倍率 / 子弹穿透数 / 附加效果
- **编辑框里显示的就是这株植物当前的数值**（不是 "-1 代表不修改" 那套）。
  点数值框改成任意值，回车确认；**改过的项会变黄**，右边出现 `R` 可以单项还原。
- 下方 6 个机制勾选：免伤（打不死）/ 无敌字段 / 不死字段 / 持续射击 / 常亮 / 不碎
- 底部按钮：还原该植物的全部修改 / 刷新融合配方 / 去融合页

> **植物从哪来**：优先用游戏自己的 `Lawnf.GetAllPlants()`。
> 它在主菜单会抛 `NullReferenceException`（已单独 try 住），**进关卡后正常可用**，
> 实测读数与游戏侧 `Lawnf.GetPlantCount(board)` 完全一致（1/1、2/2、3/3 都对上）。
> 另有 4 条兜底路径（`GetPlantsByRow` / `boardEntity` / 非泛型 `FindObjectsOfType` /
> 逐格 `GetPlant(col,row)`），哪条通了会写进日志和「设置」页的诊断行。
> **不用泛型 `FindObjectsOfType<Plant>()`**——它在 IL2CPP 下会静默返回空数组。

### 融合
- 顶部显示选中植物的**分类**（基础植物 / 超级植物 / 终极植物 / 二次融合）与**合成来源**
  （"魅惑菇 + 西瓜投手"这种中文写法）
- **配方表**：`伙伴植物 (#id) → 当前融合结果 (#id)`，全中文名，可滚动
  - **「直接融合」**：让选中的这株和该伙伴**当场融合**（真的换成融合体，模型/血量/子弹都是游戏重建的）
  - **「改结果…」**：弹出中文植物选择器，改掉融合表里这一条配方（伙伴 → 你要的结果）
- 底部「自己加一条 / 换一条配方」：选伙伴、选结果 →「写入配方」
- **「直接变成「结果」那种」**：不查融合表，直接把选中的植物换成指定植物

#### 「直接融合」是怎么做出来的（重点）

踩过的两个坑：

1. **游戏运行时认哪张表**：实测是 `PlantMixTreeManager.TryGetMixResult(a, b, out result)`
   （日志里 `树查表=西瓜花盆`），而老的 `MixData.TryGetMix` 是 `未命中`。
   所以自定义配方写进 `PlantMixTreeNode.Recipes` 是**有效**的。

2. **`CreatePlant.SetPlant(...)` 的 `targetPlant` 参数决定了会不会真的重建植物**：
   - `targetPlant != null` → 只是把**同一个对象**的 `thePlantType` 改掉：
     对象指针不变、贴图不变、属性不变。表现就是**"融合只是改了名字"**。
   - `targetPlant == null` 且在**空格子**上 → 游戏真的新建一株植物
     （走 `AddToList` / `SetPlantAttributes` / `SetTransform` / `CreatePlantParticle`），
     模型·血量·子弹·技能全部按类型初始化。

   所以「直接融合」实现成**两阶段**（都在主循环里跑，不在 IMGUI 事件中动场景）：

   ```
   阶段 0：查到融合结果 → 让原植物 Die() 退场
   阶段 1：等格子腾空（Die 是异步的，最多等 8 个周期）
           → 在空格子上 SetPlant(col,row,结果,null,...) 让游戏新建
           → ReplaceSprite() + SetPlantAttributes() + 把用户设过的数值转移过来 + 选中它
   ```

   实测记录（`BepInEx\LogOutput.log`）：

   ```
   [融合] 豌豆射手 #0 + 南瓜 → 豌豆南瓜 #1318   方式:腾空格子后由游戏新建（模型/属性完整）
   ```

   融合前 `豌豆射手 ptr=…85568 hp=300/300`，融合后
   `豌豆南瓜 ptr=…84992 hp=4000/4000` —— **换了新对象、血量按融合体重算**，
   说明游戏完整重新初始化了这株植物（不是只改了个类型）。

   万一带上 Die 之后格子一直没腾空（游戏逻辑把 Die 排到很后面），会退回
   "原地改类型 + 强制 `ReplaceSprite()` / `SetPlantAttributes()`"，
   并在状态栏明确标注**"降级"**，不会假装成功。

### 沙盒
对齐老牌修改器 Modified-Plus 的"放置"那一套，全部走游戏自己的生成接口，模型/属性都正常初始化。

| 功能 | 说明 | 走的游戏接口 |
|---|---|---|
| **植物放置** | 选植物 + 列 + 行 → 放一株 | `CreatePlant.SetPlant(col,row,type,null,...)` |
| **僵尸放置** | 选僵尸 + 行 + X（0~12）+ 是否魅惑 | `CreateZombie.SetZombie` / `SetZombieWithMindControl` |
| **小推车放置** | 选行 + 类型（0草地 1泳池 2清洁车 3草地射手 4阳光车） | `CreateMower.SetMower(type,x,row)` |
| **清空全场僵尸** | 一键杀光 | `Zombie.Die` |
| **全员变身（植物）** | 所有植物变成指定植物 | 两阶段：Die 退场 → 空格子上 `SetPlant` 重建 |
| **全员变身（僵尸）** | 所有僵尸变成指定僵尸 | 同位置 `SetZombie` 新建 → 老的 `Die` |
| **阵容码** | 把场上植物+僵尸导出成一行文本，也能粘回来复原 | `PVZRH1;P列,行,ID;Z行,ID,魅惑,X` |

> 进游戏后如果不在关卡里，可以去「作弊动作」页点**进入冒险模式第 1 关**
> （走 `UIMgr.EnterGame(LevelType, level)`），沙盒功能只有在关卡内才有效。

### 作弊动作
一次性动作按钮（两列）：
秒杀全场僵尸 / 立刻下一波 / 触发全部小推车 / 阳光设为 9999 /
一键解锁全部内容 + 补满资源 / 补满当前阳光 / 恢复所有植物 / 清空全部植物修改记录 /
**全体植物升到 10 级** / **所有植物满血** / **魅惑全场僵尸（变友军）** /
**僵尸血量 ×10 并回满** / **下一回合（旅行模式）** / **杀死全部植物** /
**进入冒险模式第 1 关** / **回到主菜单**。

### 设置
菜单字号（10~26）、**菜单窗口复位到左上角**、ESP 显示项（方框/名称/血量/序号/加粗/字号）、
**植物读数诊断**（采用来源 + 5 条路径各自命中数 + 游戏侧计数）、热键说明。

### ESP 方框
每株植物头顶一个方框（深色＝普通，红色＝当前选中），内容为 `中文名  当前血量/上限`。
**左键点方框 = 选中该植物**，然后就能在菜单「植物」页改它。

> 面板**不透明**（`GUI.skin.box.normal.background = Texture2D.whiteTexture` + `GUI.color`）。
> **全中文**：植物名一律走 `Lawnf.GetName(PlantType)`（实测 696/696 都有中文名）；
> 界面字体用 `Font.HasCharacter('中')` 实测过。
> 数值改动在**下一个周期（默认 0.25 秒）**写入植物字段。
>
> **中文名做了缓存**：`Lawnf.GetName` 每次调用都会在 Il2Cpp 侧新建字符串，
> 界面每帧要给几十行取名字，不缓存会把游戏内存撑爆（已踩过一次）。
> 同理 `GUI.skin.label.clipping` 被设成 `Overflow`，否则某些字符串会被悄悄截断。

---

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
| `TalentStars` / `SunFloor` | `0` / `9999` | 中性/保底 |
| `AbyssMaxTickets` / `AbyssInfiniteTickets` | `false` | |
| `DamageReduction` / `DamageAmplification` | `0` / `1` | 中性值 |
| `AutoLaunchUi` | `false` | 外置窗口已不再需要 |
| `GameSpeed` / `ZombieHpMultiplier` | `1` / `1` | 中性值 |
| `StopZombieSpawn` / `ZombieInvincible` / `NoToolCooldown` / `UnlockAllPlants` | `false` | |
| `MenuKey` / `EspKey` | `F1` / `F3` | 界面热键，可在「设置」页改 |

> 若你之前用过旧版本（默认全开），删掉 `BepInEx\config\com.dsh.pvzrh.cheat.cfg`
> 让新默认值生效即可。

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
| 战斗 | **单株植物修改（界面）** | 周期写入 `Plant` 字段；子弹在 `Bullet.InitData` 后置改写 |
| **融合** | **直接融合 / 自定义融合配方** | `PlantMixTreeManager.TryGetMixResult` + `CreatePlant.SetPlant`（两阶段重建） |
| 旅行/词条 | 伤害减免、幸运一击、伤害增幅、`plantZeroHealth` | 周期写入 `TravelMgr.Instance` 字段 |
| **旅行/词条** | **改抽词条结果**（白名单） | 后置过滤 `TravelMgr.GetAdvancedBuffPool` / `GetUltiBuffPool` |
| 天赋 | 天赋树全解锁 + 星星拉满 + 关困难模式 | 后置 `AdvantureData.OnInit` |
| 深渊 | 抽奖券拉满 / 不消耗 | 后置 `AbyssData.GetTicket`；前缀改写 `UseTicket` |
| 输入 | 鼠标压在菜单上时不响应游戏点击 | 前缀 `Mouse.Update` |
| **速度** | **游戏倍速（0.05x~20x）** | 写 `Time.timeScale` |
| **僵尸** | **停止出怪** | 前缀跳过 `BoardSpawner.SummonZombies` |
| **僵尸** | **僵尸无敌** | 前缀跳过 `Zombie.TakeDamage` |
| **僵尸** | **僵尸血量倍率** | 周期写 `Zombie.theMaxHealth/theHealth` |
| **僵尸** | **魅惑全场 / 批量变身** | `Zombie.SetMindControl` / `CreateZombie.SetZombie` |
| **工具** | **手套/锤子/铁锹 无冷却** | 周期把 `InGameUI.GloveBank/HammerBank/ShovelBank` 上 `InGameTool.CD` 清零 + 后置 `Lawnf.GetGloveCD` 返回 0 |
| **植物** | **图鉴/植物池全解锁** | 填 `GodManager.godData.unlockedPlants` |
| **植物** | **全体升级 / 满血 / 清空** | `Plant.Upgrade` / 写 `thePlantHealth` / `Plant.Die` |
| **关卡** | **进关卡 / 旅行下一回合** | `UIMgr.EnterGame` / `Board.TravelNextRound` |

---

## 2.5 和 Modified-Plus（老修改器）的对照

用户提供了一个老版本修改器 `Modified-Plus V2.2.1`（作者 @高数带我飞，`pvz.ehre.top`，
需要卡密联网验证）。我把它的 `Modified-Plus.dll` 反编译出元数据，
把**它的功能清单**和本插件逐条对了一遍。已经补进来的：

| Modified-Plus 的功能 | 本插件现在的状态 |
|---|---|
| 开发者模式、无CD模式、植物无属性CD | 等价（`DeveloperMode` / `NoCardCooldown` / `FreePlanting`） |
| 植物图鉴解锁 / 植物解锁 | ✅ `UnlockAllPlants` |
| 停止出怪 | ✅ `StopZombieSpawn` |
| 设置阳光 / 设置金币 | ✅ `SunFloor` / `Money`（动作里也能一键设 9999） |
| 游戏速度 / 恢复速度 | ✅ `GameSpeed` |
| 植物放置（类型+列+行） | ✅ 沙盒页 |
| 僵尸放置（类型+行+X） | ✅ 沙盒页（还多一个"魅惑"选项） |
| 小推车放置（类型） | ✅ 沙盒页 |
| 僵尸无敌 / 血量修改 | ✅ `ZombieInvincible` / `ZombieHpMultiplier` / 动作里 ×10 |
| 魅惑全场僵尸 | ✅ 作弊动作页 |
| 全体植物升级 | ✅ 作弊动作页（默认升到 10 级） |
| 杀死全部植物 / 僵尸 | ✅ 作弊动作页 |
| 阵容码导出/导入 | ✅ 有了，但用**我自己的纯文本格式**（`PVZRH1;...`），不联网、不需要卡密 |
| 下一回合（普通/旅行） | ✅ 两个都有 |
| 手套/锤子无冷却 | ✅ `NoToolCooldown` |
| 词条修改（普通/僵尸/投资/强究） | ✅ 用**白名单**方式（`BuffWhitelist` / `UltiBuffWhitelist`），比它更直接：池子里只剩你要的 |
| 深渊相关（金币/刷新次数/券） | ✅ `AbyssMaxTickets` / `AbyssInfiniteTickets` |

**没有做 / 做不了的（诚实说明）**：

| 它的功能 | 为什么没做 |
|---|---|
| 小宠物放置 / 小物件放置 | 需要确定 `PetType` / 小物件道具的生成接口，性价比不高 |
| 出怪修改（拉出怪列表逐条改） | 要动 `Board.Zombies`（`List<ZombieSpawnData>`）的波次结构，改坏了一个都刷不出来，风险高于收益 |
| 传送带修改 / 卡片修改（改卡池） | 同上，属于"改关卡数据表"级别，需要更多逆向验证 |
| 关卡名称修改 | 游戏侧没有可安全写入的关卡名字段（`LevelName1` 只是 UI 上的 TextMeshProUGUI） |
| 大花园皮肤修改 | 花园（`GardenPlant`）是另一套对象，本插件不碰 |
| 输入植物/僵尸 ID 直接放 | 沙盒页已经能按中文名搜索选择，等价（也支持输编号筛选） |
| 关于/赞赏支持/自动更新 | 与本插件定位无关 |

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

重新编译并安装：

```powershell
cd _mod\plugin
dotnet build -c Release
copy bin\Release\PvzRhCheat.dll ..\..\BepInEx\plugins\
```

---

## 5. 调试/自检用的 IPC 接口

外置窗口已经不自动启动了，但插件仍在 `127.0.0.1:27183` 监听一个**行协议** TCP 服务，
方便脚本化验证（`_mod\probe_ipc.py`、`probe_fuse.py` 就是干这个的）：

| 指令 | 作用 |
|---|---|
| `STATE` | 返回 JSON 快照（关卡、植物列表、选中项、配置、ESP 状态） |
| `TYPES` | 返回 `id/name/cn` 的植物类型表 |
| `PING` | 探活 |
| `SELECTPTR\|<ptr>` | 按对象指针选中植物 |
| `ACTION\|Plant\|<类型>\|<列>\|<行>` | 让游戏在指定格子放一株植物（沙盒） |
| `ACTION\|SpawnZombie\|<行>\|<类型>\|<X>\|<魅惑>` | 放一只僵尸 |
| `ACTION\|Mower\|<行>\|<类型>` | 放一台小推车 |
| `ACTION\|EnterGame\|<关卡类型>\|<第几关>` | 直接进关卡（0=冒险 1=挑战 2=IZ 3=生存 4=探索 5=旅行 7=深渊 8=新冒险 9=塔 10=星冒险） |
| `ACTION\|MindAll` / `KillAllPlants` / `KillAllZombies` / `HealPlants` / `UpgradePlants\|<等级>` | 批量动作 |
| `ACTION\|ZombieHp\|<倍率>` / `ChangeAllPlants\|<类型>` / `ChangeAllZombies\|<类型>` | 僵尸血量倍率 / 群体变身 |
| `ACTION\|ExportLineup` / `ImportLineup\|<阵容码>` | 阵容码 |
| `ACTION\|Speed\|<倍率>` / `Zombies` | 游戏速度 / 数僵尸 |
| `ACTION\|Cards` | 打印每张卡片的 `CD` / `fullCD` / 花费 / 次数（验证无冷却是否生效） |
| `ACTION\|Fuse\|<伙伴类型>` | 让当前选中的植物与该伙伴直接融合 |
| `ACTION\|FusionTest\|<类型>` | 跑一遍融合页的全部游戏调用并输出报告（不需要看屏幕就能验证） |

> `ACTION` 类指令**不回包**（避免与 `STATE` 回复错位），发完即可。

---

## 6. 关键技术发现（为什么会"没字"/"只是改名"）

### 6.1 默认 GUI 皮肤的字体是 NULL

游戏打包时把 IMGUI 默认皮肤的字体资源**剥离**了（游戏自己不用 IMGUI，所以被裁掉）。
`font=NULL` 意味着**任何 IMGUI 文字都渲染不出来**——面板画出来但一片空白。
插件启动时用 `Resources.FindObjectsOfTypeAll<Font>()` 从已加载资源里抓一个字体补上：

```
[界面] 皮肤缺少字体，已补: LegacyRuntime
```

`LegacyRuntime` 实测 `HasCharacter('中') == true`，中文正常显示。

### 6.2 `GUI.DrawTexture` 被剥离

`GUI.DrawTexture` / `GUI.TextField` / `GUI.Toggle` / `GUI.Button` 这些调用即抛
`NotSupportedException`。解决办法是把 `box` 样式的背景贴图换成纯白贴图：

```csharp
GUI.skin.box.normal.background = Texture2D.whiteTexture;
```

这样 `GUI.Box(rect, "")` 就等于用 `GUI.color` 画一块**完全不透明的纯色矩形**。
按钮、勾选框、滚动条、输入框全部**手绘**，绝不依赖被裁掉的接口。

### 6.3 对勾：**不要用字体字形**（见 6.8）

试过用字体里的 `✓`（U+2713），`HasCharacter` 明明返回 true，但实际渲染是一坨绿色方块。
现在用 `UiSkin.DrawCheck()` 自绘。早期还试过"一堆小方块手绘"，那个会糊——
区别在于**现在所有坐标都 `Mathf.Round` 取整到整像素**，方块也不再是 1px 而是 `side/4.5`。

### 6.4 注入类型的签名限制

Il2CppInterop 会把注入类型（`EspOverlay` / `MenuOverlay`）的**所有方法**转成 il2cpp 可调用的形式。
遇到无法转换的签名，调用时抛 `NotSupportedException`。

**规则：注入类型的方法签名里不要出现数组返回值、委托、纯托管类参数。**
现在的做法更彻底——注入类型只有一个 `void OnGUI()`，全部界面逻辑放在普通静态类
`MenuUI` 里（普通类没有这个限制，想怎么写就怎么写）。

### 6.5 `out ValueTuple` 会把游戏直接干崩（重要）
`MixData.TryGetDisMix(PlantType, out ValueTuple<PlantType,PlantType>)` 这类
**`out` 值元组**在 Il2CppInterop 下是坏的：调用了 3 次没事，
第 4 次（查一个真的有父节点的融合体时）整个进程 **访问违例**崩溃：

```
Faulting module name: coreclr.dll
Exception code: 0xc0000005
```

所以现在**完全不碰** `ValueTuple` / `MixData._recipes`：

- 融合结果用 `PlantMixTreeManager.TryGetMixResult(a, b, out PlantType result)`（`out` 枚举，二进制兼容）
- "合成来源"改成扫 `PlantMixTreeManager.PlantMixTrees` 自己建反向表
  （`GetTree` / `Recipes` 都是字典，实测安全），建一次缓存

> 这三条对以后给这个游戏写任何 IMGUI 工具都适用。

### 6.6 给"游戏早期用 native 方式建出来的对象"打补丁会崩（重要）

本来"手套/锤子无冷却"是想用 Harmony 前缀给 `InGameTool.UpdateCDTimer` 清零 CD 的，
结果一进游戏就崩：

```
[Error:Il2CppInterop] During invoking native->managed trampoline
Exception: System.InvalidOperationException: Handle is not initialized.
   at Il2CppInterop.Runtime.Runtime.Il2CppObjectPool.Get[T](IntPtr ptr)
   at (il2cpp -> managed) UpdateCDTimer(IntPtr, Il2CppMethodInfo*)
```

`__instance` 参数让蹦床去 `Il2CppObjectPool` 里找托管包装，而这类对象的 GCHandle 还没初始化，
于是抛异常 → 整个进程 `0xc0000005`。
**规则：给这类方法打补丁时不要注入 `__instance`**，改成在主循环里
通过 `InGameUI.Instance` 上的字段（`GloveBank`/`HammerBank`/`ShovelBank`）
拿到对象直接改字段，零风险。

### 6.7 批量操作一定要在"动手之前"记下坐标

"所有植物变成 X"最早写成"每周期从植物列表重新推坐标"，
结果第一周期把植物 `Die` 掉之后列表就空了，**只杀了不重建**。
现在改成请求时先把 `(列,行)` 记进一个列表，之后按这个列表分周期重建。

同样地，`Plant.Upgrade(...)` 在这个构建里会**返回 false**（不是抛异常），
所以升级动作必须"返回 false 就退回直接写 `theLevel`"，只按返回值判断会一株都升不了。

### 6.8 画勾这件事踩了三个坑（最后才拿到一个清晰的 ✓）

1. **字体字形**：`HasCharacter('\u2713')` 返回 **true**，但用
   `SetFontSize(box.height + 3)` + `FontStyle.Bold` + `label.clipping = Overflow` 画出来，
   是一坨比框还大的亮绿色实心块 —— 就是用户说的"超大绿色像素"。
   字号必须**收进框内**（`box.height - 2`）才正常。
2. **用 `GUI.Box` 画 2px 小方块**：`GUI.Box` 是 9 宫格样式，画比 border 还小的矩形时，
   画出来的面积远大于矩形本身。实测自绘的勾期望约 30 个像素、实际量出来 **328 个**，
   形状是一块 23x19 的实心板 —— 完全没有勾形。
3. **`GUI.DrawTexture` 被剥离**：本来想用它画细线，运行日志明确说
   `[界面] GUI.DrawTexture 不可用（NotSupportedException）`，
   于是又退回了 `GUI.Box`，继续糊。

所以最后的做法是**运行时探测 + 自动选路**：

```
[界面] GUI.DrawTexture 不可用（NotSupportedException），勾选框改用字体 ✓
[界面] 勾选框画法 = 字体 ✓ 字形
```

勾选框固定 20x20，勾用**字体 ✓、字号 = 框高-2**。
实测像素图（G=绿边框 g=深绿底 #=勾）：

```
Gggggggggggg.#.ggggG
Ggggggggggg.#.gggggG
Gggggggggg.#.ggggggG
Gggggggggg.#gggggggG
Ggggggggg.#.gggggggG
Gggggggg.#.ggggggggG
Ggggg.#g.#gggggggggG
Ggggg.#.#.gggggggggG
Ggggg.##.ggggggggggG
Gggggg.#.ggggggggggG
```

细笔画、整数像素、形状正确。
**如果以后换到 `DrawTexture` 可用的构建**，代码会自动切到自绘路径（`UiSkin.DrawCheck`）。

### 6.9 开分组默认折叠 = 用户以为"功能没了"

「功能开关」页一开始 10 个分组全是折叠的，一打开只看到 10 行组名，
用户反馈"开关什么的少了"。现在**默认全部展开**，组名上还标 `(N 项)`，
并加了 `展开全部 / 折叠全部`、右侧滚动条 + `▲▼`、
底部 `共 N 行，当前显示第 a ~ b 行` 提示。

另外还有两个排版坑（都是像素扫描查出来的）：
- 滚动条原来画在 `cr.xMax + 2`，**探出面板右边界 9px**，截图里是一条伸出边框的灰带子 → 挪到 `cr` 内部。
- 配置键名原来只给 38px，长键名（`ZombieDamageTakenMultiplier`）会溢出并压在数值框上，
  看起来像 `9999SunFloor` → 改成 **中文标签 | 键名（右对齐截断）| 数值框** 三段固定宽度。
- `共 N 行` 那行原来画在列表最后一行上面 → 改成列表高度里先扣掉 22px，让它单独占一条。

---

### 6.10 自写滚动列表最容易犯的错：跳过窗口外的行时把 y 也推了

「功能开关」页往下滚时**上半部分会变空、下面的行跑到面板外看不见**，就是这个 bug：

```csharp
// 错误写法
if (k < _scroll[0] || k >= _scroll[0] + visible) {
    if (k < _scroll[0] + visible) y += rh;   // ← 窗口"上方"被跳过的行也把 y 往下推了
    continue;
}
```

`y` 是"当前行该画在哪个屏幕 y"。它必须从窗口顶部开始、**只被真正画出来的行推进**。
上面那句让窗口上方的每一行都推进了 y，于是 `_scroll[0]=10` 时第一行被画在
`cr.y + 10*23 = +230px` 的位置，随后 `y >= cr.yMax` 提前结束循环，
结果**只有 11 行被画出来、而且挤在下半部分**，上半部分全空 —— 看起来就是"UI 消失了一部分"。

正确写法（`continue` 就完了，什么都别做）：

```csharp
if (k < _scroll[0] || k >= _scroll[0] + visible) continue;
```

用一段模拟脚本验证过：修复后在 scroll = 0 / 5 / 10 / 29 四种情况下，
都是**正好 21 行**从 y=0 铺到 y=460（窗口高 483），不再有空洞。
另外三个页签（植物列表、融合配方表）用的是"只遍历窗口那一段"的写法，本来就没错。

顺便一起改掉的两个滚轮问题：
- **没有 `Event.Use()`**：滚轮事件没被吃掉，游戏自己也收到同一个滚轮（菜单滚一下、画面也动）。
  现在 `TakeWheel()` 里会 `Use()`，且只在鼠标压在面板上时才取。
- **一格只滚一行**：50 行要滚 29 下。现在一格固定滚 3 行
  （**不要**拿 `delta` 的绝对值去乘 —— 不同鼠标一格给的 delta 是 1 也可能是 3，乘出来忽快忽慢）。

---

### 6.11 「卡片无冷却」为什么之前是坏的：写 CD 没用，要写 fullCD

用户报"开了之后会一直重置到有冷却"。用新加的 `ACTION|Cards` 把每张卡的实际值打出来才看清：

```
默认       冰块礼盒: CD=0  fullCD=30   雪棘草: CD=30 fullCD=30   冬笋路障: CD=15 fullCD=15
只写 CD=0  冰块礼盒: CD=0  fullCD=30   雪棘草: CD=0  fullCD=30   ← 下一帧就被游戏覆盖回去了
写 fullCD=0 冰块礼盒: CD=0 fullCD=0    雪棘草: CD=0  fullCD=0    ← 真的恒为就绪
```

游戏每帧用**自己的计时器**算 `CD = fullCD - 已过时间`，所以：

- 只写 `CD = 0` → 下一帧就被覆盖；而我们每 2 秒再拍一次 0，
  看起来就是**冷却条一直在重置**（用户看到的现象）。
- 把 `fullCD` 清零 → `CD` 算出来是负数 → 卡片恒为"已就绪"。
- 刷新频率也得跟上：从 2 秒的施加周期挪到 **0.25 秒的快 tick**，否则冷却条会先涨回去再被拍平。

关闭功能时要**还原**：按对象指针缓存原始 `fullCD` / `theSeedCost` / `maxUsedTimes`，
否则要等下一关卡片重建才恢复。实测三态：

```
1) 默认       fullCD=30 / fullCD=50 / fullCD=15     ← 正常冷却
2) 开启无冷却 CD=0 fullCD=0                          ← 恒就绪
3) 关闭还原   fullCD=30 / fullCD=50                 ← 还原成功
```

### 6.12 冷却相关的东西到底有哪些（全量核对，别再漏）

用户报"手套 CD 也不行"，索性把游戏里**所有**跟冷却有关的成员列全：

| 类 | 字段 / 方法 | 我原来做的 | 问题 |
|---|---|---|---|
| `CardUI` | `CD`, `fullCD`, `CDUpdate()`(virtual) | 写 `fullCD=0` | ✅ 已对（写 `CD` 无效，游戏每帧重算） |
| `SpecialCard : CardUI` | 重写 `CDUpdate()` | — | 靠 `fullCD=0` 一样生效 |
| `InGameTool`(基类) | `fullCD`, `CD`, `coolSpeed`, `avaliable`, `CDUpdate()`, `UpdateCDTimer()` | 写 `CD/fullCD/avaliable` | ⚠️ 原来**漏了 `coolSpeed`** |
| `Glove : InGameTool` | 重写 `UpdateCDTimer()`，有 **`static Glove Instance`** | 拿 `InGameUI.GloveBank` 去 `GetComponent` | ❌ 不一定找得到；应该直接用 `Glove.Instance` |
| `Hammer : InGameTool` | 重写 `UpdateCDTimer()`，有 **`static Hammer Instance`** | 同上 | ❌ 同上 |
| `Shovel : InGameTool` | 不重写 | 同上 | ❌ 同上 |
| `Wheel : InGameTool` | 不重写，**没有 static Instance** | 完全没处理 | ❌ 现在靠遍历所有 `InGameTool` 覆盖 |
| **`Board`** | **`freeCD` (bool)** | **完全没碰** | ❌ **这才是游戏自己的"无CD模式"总开关** |
| `Lawnf.GetGloveCD()` | static 返回 float | 后缀补丁返回 0 | ✅ 已对 |
| `IZManager.AICard` | `cd`, `fullcd` | — | IZE 模式的 AI 出怪卡，与玩家无关，不动 |
| `LevelData.gloveCD` | 关卡配置里的手套 CD | — | 只读配置，不用改 |

所以"手套无冷却没用"是两条原因叠加：**没设 `Board.freeCD`**，而且工具的 CD 是拿
`GameObject` 去 `GetComponent` 找的（不一定拿得到），应该直接用静态 `Instance`。

改完之后实测（`ACTION|Tools` 打印真实数值）：

```
关闭: Board.freeCD=False  GetGloveCD()=10  手套 Glove: CD=0 fullCD=10 可用=False
开启: Board.freeCD=True   GetGloveCD()=0   手套 Glove: CD=0 fullCD=0  可用=True
还原: Board.freeCD=False  GetGloveCD()=10  手套 Glove: CD=0 fullCD=10 可用=False
```

`Board.freeCD` 会按对象保存原值并在关闭时还原，不会把游戏原本的免费冷却状态改坏。

> 教训：**改这类字段前先把所有相关成员列全**（用 dump 搜 `CD`/`fullCD`/`CDUpdate`/`UpdateCDTimer`），
> 不然很容易只改到一半。另外新增了 `ACTION|Cards` 和 `ACTION|Tools` 两条诊断指令，
> 直接把游戏里的真实数值打出来 —— 这次两个冷却问题都是靠它们定位的。

---

1. **`Effects`（附加效果输入框）只记录不生效**。游戏效果走 `Dictionary<EffectType, BaseEffect>` 与
   `eveBuffs`，正确施加需要构造 Effect 实例并调用语义未知的方法，没反编译方法体不敢贸然调。
2. **「直接融合」靠"先 Die 再重建"**。原植物的死亡处理会跑一遍（可能有粒子/音效），
   而且如果游戏把 Die 排到很久之后，会降级成"原地改类型"（状态栏会写明"降级"）。
3. **「直接变成某种植物」不查融合表**，新植物是游戏按该类型正常建出来的；
   但如果你变的是一个"需要特定前置"的植物，它的技能可能表现不完整。
4. **单株覆盖按对象指针索引**。植物销毁后若新植物分配到同一地址，旧覆盖会套到新植物。
   融合时已经把数值转移过去了，跨关卡建议按一下「清空全部植物修改记录」。
5. **攻速倍率**会先缓存原始 `thePlantAttackInterval` 再换算，避免周期施加反复相除导致指数级加速（已修）。
6. **子弹覆盖在 `Bullet.InitData` 后置生效**，每颗子弹只处理一次（防重复翻倍，已加去重）。
7. **虚方法重写**：`Plant.TakeDamage`/`Zombie.TakeDamage` 是虚方法，补的是基类实现；
   子类若重写则那条路径拦不住。植物无敌额外加了"周期回满血"兜底。
8. **未做动态调试**：本插件是"按签名打补丁 + 运行时日志验证"的产物，没有下断点逐帧跟踪。
   功能无效时把 `BepInEx\LogOutput.log` 里 `[FAIL]`/`[融合]`/`[词条白名单]` 开头的行发我。

---

## 7. 实测验证

### 7.1 默认居然还开着作弊？—— 是调试时用 IPC 写脏了配置

调试过程中我用 `CFG|key|1` 打开过一堆开关，这些会**写进配置文件并持久化**，
所以用户看到"默认还开着一些作弊"（`AutoCollectSun` / `ZombieHpMultiplier=3` / `UnlockAllPlants`）。

修法：加了配置结构版本 `ConfigVersion`，
**默认绑 0**（不是当前版本号 —— 否则已存在的旧文件读出来就是新版本，迁移根本不会跑），
加载时发现 `< 2` 就调 `ForceAllOff()` 把 34 个作弊项全部拉回"关闭/中性"值，然后写回 2。
实测日志：`[配置] 配置迁移到 v2：已把 23 个作弊项强制关闭`。

另外「作弊动作」页加了 **★ 一键关闭全部作弊** 和 **只关总开关** 两个按钮，随时可以一键回到干净状态。

> `ForceAllOff()` 只动作弊项，**不动**总开关 `Enabled`、字号、热键这些偏好设置。

---

补丁加载 **17/17 全部成功**（`GameAPP_Awake/_Update`、`Board_UseSun/_UseMoney`、
`Plant_TakeDamage/_RealTakeDamage/_DecreaseHealth`、`Zombie_TakeDamage`、`Bullet_InitData`、
`Travel_AdvBuffPool/_UltiBuffPool`、`Advanture_OnInit`、`Abyss_GetTicket/_UseTicket`、`Mouse_Update`、
`BoardSpawner_SummonZombies`、`Lawnf_GetGloveCD`）。

游戏把结果持久化到 `playerData.json`，证明修改真实生效：

| 字段 | 原始 | 修改后 |
|---|---|---|
| `theMoneyCount` | 190 | **999999999** |
| `advLevelCompleted` | 部分 | **128/128** |
| `clgLevelCompleted` | 部分 | **256/256** |
| `gameLevelCompleted` | 部分 | **128/128** |
| `survivalLevelCompleted` | 部分 | **128/128** |

植物读取（进关卡后，多次实测与游戏侧计数一致）：

```
[植物] 来源=Lawnf 合计=1  | Lawnf=1 byRow=0 board=0 find=0 grid=0  游戏侧计数=1
[植物] 来源=Lawnf 合计=2  | Lawnf=2 byRow=0 board=0 find=0 grid=0  游戏侧计数=2
```

融合（`豌豆射手 #0 + 南瓜 → 豌豆南瓜 #1318`）：

```
融合前  豌豆射手 ptr=1747574485568 hp=300/300
融合后  豌豆南瓜 ptr=1747574484992 hp=4000/4000
[融合] 融合成功：豌豆射手 #0 + 南瓜 → 豌豆南瓜 #1318   方式:腾空格子后由游戏新建（模型/属性完整）
```

反向表：`[融合] 反向表建立完成：1274 条配方，覆盖 523 种结果`

新增功能的实测（全部通过 `ACTION` 接口脚本化验证，日志为准）：

```
停止出怪        开关打开 + 重进关卡 + 等 35 秒  ->  场上僵尸 0 只
植物放置        ACTION|Plant|0|3|2              ->  豌豆射手 (3,2)
僵尸放置        ACTION|SpawnZombie|2|0|9|0      ->  已在第 3 行 x=9 放置僵尸 #0
小推车放置      ACTION|Mower|4|1                ->  已在第 5 行放置小推车 #1
魅惑全场        ACTION|MindAll                  ->  已魅惑 1 只僵尸
僵尸血量 ×10    ACTION|ZombieHp|10              ->  已把 1 只僵尸的血量设为 10 倍
僵尸群体变身    ACTION|ChangeAllZombies|1       ->  1 只新建，旧僵尸清掉 2 只
全体升级        ACTION|UpgradePlants|5         ->  已把 2 株植物升到 5 级（lv=5 实测）
植物群体变身    ACTION|ChangeAllPlants|32       ->  记下 2 个格子 -> 已重建 2 株 -> 西瓜投手 (3,2)/(5,3)
阵容码          ExportLineup / ImportLineup     ->  PVZRH1;P3,2,0;Z2,0,1,10.03;... -> 植物 1 僵尸 2
旅行下一回合    ACTION|TravelNext               ->  已跳到旅行模式的下一回合
进关卡          ACTION|EnterGame|0|1            ->  Advanture Lv1 / Day（inLevel=1）
```

---

## 8. 已知限制（诚实说明）

## 9. 工程文件

```
_mod\
├─ plugin\                    C# 插件源码（net6.0，离线编译）
│   ├─ PvzRhCheat.csproj
│   ├─ Plugin.cs              入口 + 安全打补丁 + 注入叠加层
│   ├─ ModConfig.cs           全部配置项
│   ├─ Patches.cs             公共逻辑 + 15 个补丁类 + 植物列表/选中/融合状态机
│   ├─ Overrides.cs           单株植物覆盖 + 子弹覆盖 + 融合后数值转移
│   ├─ PlantDb.cs             中文名缓存/类型表/数值读写/融合配方/两阶段融合
│   ├─ UiSkin.cs              只用确认可用的 IMGUI 接口手绘控件
│   ├─ MenuUI.cs              游戏内菜单全部界面（含 MenuOverlay 外壳）
│   └─ EspOverlay.cs          ESP 方框
├─ ui\                        外置 WinForms 窗口（已不自动启动，保留备用）
├─ dump\                      Il2CppDumper 全量导出（dump.cs / il2cpp.h / DummyDll）
├─ tools\                     BepInEx be.788、Il2CppDumper、apidump（自研 API 转储器）
├─ nuget-local\               离线 net6.0 targeting pack
├─ backup\saves_*             存档备份
├─ probe_ipc.py               行协议 IPC 探针
├─ probe_fuse.py              融合链路自动化验证
├─ install_when_closed.ps1    等游戏退出后自动安装
└─ uninstall.ps1              一键回滚
```
