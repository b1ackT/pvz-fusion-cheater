# 《植物大战僵尸 融合版 3.9》反编译分析报告

> 静态分析（未运行游戏本体，仅做只读解析）。分析时间：本次会话。
> 所有结论均来自对二进制/元数据/存档的实际解析，证据文件见文末清单。

---

## 1. 结论速览

| 项目 | 结论 |
|---|---|
| 引擎 | Unity **2022.3.62f1c1**（b0109b07edb8，`c1` = **Unity 中国版**），URP 渲染管线 |
| 脚本后端 | **IL2CPP**（非 Mono），元数据版本 **31** |
| 混淆/保护 | **无任何混淆**，241 个 `il2cpp_*` 导出符号齐全，可直接反编译出接近源码的 C# |
| 主数据文件 | 单个 `data.unity3d`（550,206,064 B，UnityFS v8 + LZ4HC），非传统散装 `level0/sharedassets` 目录 |
| 代码规模 | 71 个程序集，**14,775 个类型**、**108,140 个方法**、**60,649 个字段**、19,077 条字符串字面量 |
| 游戏逻辑 | `Assembly-CSharp.dll`：3,692 个类型，其中 3,165 个在全局命名空间 |
| 规模 | **698 种植物**（`PlantType`）、**229 种僵尸**（`ZombieType`）、732 条植物资源路径、71 个基础植物 |
| 场景结构 | **全游戏只有 1 个场景（`level0`，14 KB）**：单场景 + 212 MB `resources.assets` 预制体驱动 |
| 架构特点 | **高度数据驱动**：配置与存档全为 JSON；内置可视化节点图关卡编辑器 |
| 网络 | 自建关卡服务器 `<自建服务器>:3000`（明文 HTTP）；接入 B 站直播开放平台（弹幕互动） |
| 版本号 | 存档内自报 **"3.9"** |

**一句话**：这是一个没有做任何保护的 IL2CPP Unity 游戏，反编译门槛很低；其真正的复杂度不在保护而在**内容量**（698 种植物、多套玩法子系统、数据驱动 + 内置编辑器）。

---

## 2. 目标识别

`app.info` 暴露了开发者与产品名：

```
LanPiaoPiao
PlantsVsZombiesRH
```

`boot.config` 给出构建 GUID `2b656e1bb0ce48aba36a03a8c4c4262b`，且 `wait-for-native-debugger=0`（关闭调试器等待）。

运行时日志确认了数据加载路径（`Player.log` 第 3 行）：

```
Loading player data from E:/games/.../PlantsVsZombiesRH_Data/data.unity3d
Initialize engine version: 2022.3.62f1c1 (b0109b07edb8)
```

---

## 3. 文件清单与作用

| 文件 | 大小 | 作用 |
|---|---|---|
| `PlantsVsZombiesRH.exe` | 0.64 MB | 标准 Unity 启动器，仅导入 `UnityPlayer.dll!UnityMain` |
| `UnityPlayer.dll` | 29.7 MB | Unity 运行时（官方原版，未改动） |
| `GameAssembly.dll` | 55.0 MB | **IL2CPP 原生代码 + 编译后的所有游戏逻辑**（核心逆向目标） |
| `baselib.dll` | 0.4 MB | Unity 平台抽象层 |
| `..._Data/data.unity3d` | 550,206,064 B (524.7 MB) | **主数据包**：整个游戏数据单文件打包（见 3.1） |
| `..._Data/resources.resource` | 40.2 MB | 资源流数据（Resources 的音频/贴图流） |
| `..._Data/sharedassets0.resource` | 1.0 MB | 共享资源流 |
| `..._Data/il2cpp_data/Metadata/global-metadata.dat` | 12.6 MB | **IL2CPP 元数据**：类型/方法/字段/字符串全表 |
| `..._Data/il2cpp_data/Resources/0Harmony.dll-resources.dat` | 0.5 MB | **0Harmony（运行时补丁库）被链接进游戏** |
| `..._Data/il2cpp_data/Resources/{mscorlib,System.Data,System.Drawing,Newtonsoft.Json}.dll-resources.dat` | — | 序列化/数据相关依赖 |
| `..._Data/Plugins/x86_64/lib_burst_generated.dll` | 0.3 MB | Unity Burst 编译器产物 |
| `..._Data/boot.config`、`app.info`、`ScriptingAssemblies.json` | — | 引擎配置 |

**注意点**：`0Harmony` 出现在链接清单里，说明作者在游戏内做了**运行时方法补丁**，而不是纯静态代码。这对魔改是好消息（也意味着有现成的 patch 基础设施）。

### 3.1 `data.unity3d` 内部结构（已实测解包）

`data.unity3d` 是 UnityFS v8 归档：**实际大小 550,206,064 字节**（= 头部 size 字段自报值，0x20CB7A70），头部 52 字节，紧接 **12 字节补零**（0x34–0x3F，16 字节对齐）后即为 blocksInfo（位于文件偏移 **64**）。

头部字段（全部大端）：

| 字段 | 值 |
|---|---|
| version | 8 |
| unityVersion / revision | `5.x.x` / `2022.3.62f1c1`（**`c1` 后缀 = Unity 中国版**） |
| size | 550,206,064 |
| compressedBlocksInfoSize | 63,368 |
| uncompressedBlocksInfoSize | 178,363 |
| **flags** | **579 = 0x243** |

flags 解码（已实测校正，注意与部分资料的说明相反）：

- `& 0x3F` = **3** → 压缩方式 **LZ4HC**
- `0x40` = SET → BlocksAndDirectoryInfoCombined
- **`0x80` = clear → BlocksInfoAtTheEnd *未*置位**（blocksInfo 在文件开头）
- **`0x200` = SET → BlockInfoNeedPaddingAtStart**（即那 12 字节补零）

> 实证：blocksInfo 在偏移 52 与文件末尾两种猜测均 **LZ4 解码失败**，只有偏移 64 能解出恰好 178,363 字节（= `uncompressedBlocksInfoSize`，断言通过）。

**17,796 个数据块全部为 LZ4HC**，128 KiB 分块（末块 52,476 B），压缩比 **4.24×**（550,142,624 → 2,332,478,716 字节）。三项交叉校验全部精确闭合：blocksInfo 恰好耗尽无尾余、`Σnode.size == Σblock.uncompressedSize`、`63440 + ΣcompressedSize == 550206064`。

解压后得到 **9 个内部文件**（`11_bundle_paths.txt`）：

| # | 路径 | 大小 | 说明 |
|---|---|---|---|
| 0 | `globalgamemanagers` | 10.5 MB | 全局管理器（含 Resources 清单） |
| 1 | `Resources/unity_builtin_extra` | 383 KB | Unity 内置资源 |
| 2 | `globalgamemanagers.assets` | 962 KB | 全局资源 |
| 3 | `sharedassets0.assets` | 20.8 KB | 共享资源 |
| 4 | `globalgamemanagers.assets.resS` | 7.0 MB | 流数据 |
| 5 | `sharedassets0.assets.resS` | 72.8 KB | 流数据 |
| 6 | **`level0`** | **14.3 KB** | **唯一的场景文件** |
| 7 | **`resources.assets`** | **212.7 MB** | **全部 Resources 资产（含配置 JSON、预制体）** |
| 8 | **`resources.assets.resS`** | **2.10 GB**（解压后） | 全部音频/贴图流数据 |

**关键结论**：

1. 这不是开发者自制的 AssetBundle，而是**把常规 `*_Data` 目录单文件打包**的播放器数据文件——文件名全是 `globalgamemanagers`/`level0`/`resources.assets` 这类引擎固定名，没有任何 `CAB-xxxx` 或 `archive:/` 前缀。
2. **整个游戏只有一个场景（`level0`，仅 14,256 字节）**。游戏不是靠多场景切换，而是**单场景 + 海量 `Resources.Load` 预制体**驱动——这与 `GameAPP`/`Board`/`ResourcesManager` 的架构、以及 `resources.assets` 高达 212 MB 完全吻合。
   - `level0` 已完整解出，场景内对象名为：`GameAPP`、`Prelude`、`Main Camera`、`Canvas`、`CanvasUp`、`ScreenShine`、`BlackMask`、`EventSystem`，并含 `Horizontal`/`Vertical`/`Submit`/`Cancel` 输入轴配置。**入口对象 `GameAPP` 确实就在这个唯一场景里**，与前文运行期日志 `GameAPP:Awake()` 相互印证。
3. **内嵌 SerializedFile 全部为版本 22、大端、48 字节头**（含 64 位 metadata/fileSize/dataOffset 字段）。6/6 个文件校验通过：`fileSize == node.size` 且 `dataOffset == align16(48 + metadataSize)`。
   - 平台字段 = 19（`BuildTarget.StandaloneWindows64`）
   - 首个类型的 classID 语义自洽：`globalgamemanagers`=129 PlayerSettings、`unity_builtin_extra`=48 Shader、`globalgamemanagers.assets`=115 MonoScript、`sharedassets0.assets`=150 PreloadData、`level0`=1 GameObject、`resources.assets`=Material
   - 注：**对象级清单（各贴图/预制体/TextAsset 的名字）未枚举**，见第 10 节。
4. 配置 JSON（`PlantEvolutionData.json` 等）作为 TextAsset 打包在 `resources.assets` 内，**要改就必须重打包**（或配合 `0Harmony` 做运行时注入）。
5. 压缩比 **4.24 倍**（2,332,478,716 → 550,206,064）。

### 3.2 两个 `.resource` 旁挂文件实为 FMOD 音频库

| 文件 | 大小 | magic | 判定 |
|---|---|---|---|
| `resources.resource` | 42,198,592 B | `FSB5` | FMOD Sample Bank v1 |
| `sharedassets0.resource` | 1,095,232 B | `FSB5` | FMOD Sample Bank v1 |

`FSB5` 是 Unity 打包音频（`.resource` 音频流）的常规封装格式，**属正常现象**；不过这两处旁挂文件与归档内部自带的 `*.assets.resS` 并存，暗示该构建在 Unity 出包后被动过（此点为推断，非实证）。

---

## 4. IL2CPP 元数据分析

### 4.1 头部与规模

`global-metadata.dat` 头部校验通过（magic `0xFAB11BAF`，version 31）：

| 实体 | 大小 | 条目数 |
|---|---|---|
| 类型定义 `Il2CppTypeDefinition` | 1,300,200 B | **14,775** |
| 方法 `Il2CppMethodDefinition` | 3,893,040 B | **108,140** |
| 字段 `Il2CppFieldDefinition` | 727,788 B | **60,649** |
| 参数 `Il2CppParameterDefinition` | 1,367,508 B | **113,959** |
| 属性 `Il2CppPropertyDefinition` | 383,700 B | 19,185 |
| 字符串字面量 | 152,616 B / 603,152 B 数据 | **19,077** 条（含中日韩文字 **3,881** 条） |

### 4.2 程序集 Top 与第三方依赖

游戏逻辑集中在 `Assembly-CSharp.dll`（3,692 类型）。第三方库暴露出技术选型：

- **UniTask**（Cysharp）—— 异步，586 类型
- **Unity.VisualScripting** —— 可视化脚本
- **spine-csharp / spine-unity** —— 骨骼动画（植物/僵尸动画大概率用 Spine）
- **Newtonsoft.Json** —— JSON 序列化（配合全 JSON 配置架构）
- **Unity 2D Animation / SpriteShape / Tilemap.Extras**
- **SimpleFileBrowser** —— **内置文件选择器**（游戏内读写文件）
- **NativeWebSocket / OpenBLive.Runtime** —— WebSocket 与 B 站直播 SDK
- **Unity.RenderPipelines.Universal** —— URP
- **Unity.MemoryProfiler** —— 内存分析器被保留在包里
- **com.cyborgAssets.inspectorButtonPro** —— 一个编辑器增强插件（连编辑器插件都编进来了，见下方"开发痕迹"）

### 4.3 游戏逻辑命名空间（剔除引擎/系统库）

```
3165  (global)                  ← 绝大多数游戏类无命名空间
 162  GameLevel.RogueShooting   ← Roguelike 射击模式
 146  GameLevel.EventNodes      ← 可视化节点图（关卡事件节点编辑器）
  72  AutoChess                 ← 自走棋模式
  64  GameLevel                 ← 关卡框架
  29  OpenBLive.Runtime.Data    ← B 站直播
  26  AdvBuffData               ← 冒险增益
  25  GameLevel.Abyss           ← 深渊模式
  21  ZenGarden                 ← 禅境花园
  21  OpenBLive.Runtime
  19  SimpleFileBrowser
  15  NativeWebSocket
   9  RhythmGame                ← 音游模式
   8  GameLevel.OnLine          ← 联机/在线关卡
   7  AlmanacData               ← 图鉴
   5  GameLevel.RogueShooting.CurseBuffs
   3  PVPScaryPot               ← PVP 砸罐子
   3  VarietyFX
   3  GameLevel.Scene
   2  PlaceRule
   1  NewTravel / HuaLun / PlantEvolution
```

---

## 5. 游戏架构

### 5.1 启动链（运行期日志实证）

`Player.log` 里的调用栈直接给出了初始化路径：

```
GameAPP:Awake()
  → ResourcesManager:.ctor()
    → Core.Lawnf:GetDict(String, Boolean)      // 加载 LawnStrings 字典
```

即：`GameAPP.Awake()` 是入口，`ResourcesManager` 构造函数负责把 JSON 字符串表读进内存。

### 5.2 核心类（全部实测自元数据）

**主板与战斗**
- `GameAPP`（入口）、`Board`、`BoardData`、`BoardEntity`、`BoardSpawner`、`BoardGame`、`BoardGrid`、`InitBoard`
- `GridManager`、`WaveManager`、`BulletPoolManager`、`NumberPopManager`、`HealthSliderManager`
- `PlantDataManager`、`ZombieDataManager`、`CustomPlantManager`、`CustomIZManager`
- `SynergyManager`、`RoguelikeManager`、`TreasureManager`、`QuestManager`、`AdvantureManager`
- `SaveMgr` / `SaveInfo` / `SaveBoardData` / `SavePlantData` / `SavedCustomPlantData`

**融合系统（本版核心玩法）**
- `PlantMixTreeManager`、`PlantMixTreeNode`、`MixData`、`MixedPlant`、`MixBomb`
- `PlantEvolutionData`、`PlantEvolutionRoute`、`DynamicPlantEvolutionData`、`GodEvolution`
- `PlantEvolution.PlantEvolutionConfigLoader`

**羁绊（Synergy）系统** —— 类名直接用中文，共 43 个 `SynergyType`：
`Synergy_前院守卫`、`Synergy_后院守卫`、`Synergy_屋顶守卫`、`Synergy_蘑菇岛`、`Synergy_冰雪之地`、`Synergy_极致之冰`、`Synergy_烈焰战士`、`Synergy_磁力科技`、`Synergy_阳光财团`、`Synergy_泰坦之躯`、`Synergy_召唤师`、`Synergy_爆破王`、`Synergy_百步穿杨`、`Synergy_爽快射击`、`Synergy_战术小队`、`Synergy_前线壁垒`、`Synergy_寰宇`、`Synergy_持续伤害` …

**Buff / Debuff / 词条（Roguelike 抽取）** —— 同样是中文类名：
- Debuff：`Debuff_战争激励`、`Debuff_适应之力`、`Debuff_格挡反击`、`Debuff_急行军`、`Debuff_领袖强化`、`Debuff_随从强化`、`Debuff_空军强化`、`Debuff_霸凌弱者`、`Debuff_全民皆兵`、`Debuff_信息封锁I/II`
- Invest（投资/词条）：`Invest_完美开局`、`Invest_气氛组`、`Invest_无伤通关`、`Invest_植物重组`、`Invest_究极支援`、`Invest_恢复生机`、`Invest_简单模式`、`Invest_难度修改器`、`Invest_当头一棒`、`Invest_榜样的力量`

### 5.3 内置的多种玩法子系统

从管理器类名可复原出完整玩法矩阵：

| 模式 | 关键类 | 说明 |
|---|---|---|
| **自走棋** | `AutoChess.{EconomyManager, ShopManager, RoundManager, SynergyManager, AutoChessSaveSystem}` | 6 步初始化：经济→商店→羁绊→回合→UI→存档 |
| **Roguelike 射击** | `GameLevel.RogueShooting.ShootingManager`、`PlayerShootingManager`、`PlayerController`、`RogueShooting_hellMode` | 有 `CurseBuffs` 诅咒增益，`Synergy_爽快射击` |
| **深渊** | `GameLevel.Abyss.AbyssManager` | |
| **音游** | `RhythmGame.{RhythmGameManager, ComboManager}`、`RhythmGameEditor.RhythmEditorDataManager` | 带**踩点编辑器**，BGM 谱面 JSON |
| **联机/在线关卡** | `GameLevel.OnLine.OnlineLevelLoader`、`OnlineLevelSaver` | 走自建 HTTP API |
| **禅境花园** | `ZenGarden`、`GardenBattleManager` | |
| **PVP 砸罐子** | `PVPScaryPot`、`ScaryPotManager` | |
| **我是僵尸(IZ)** | `IZManager`、`CustomIZManager` | 自定义僵尸关卡 |
| **迷你游戏(14 个)** | `BejeweledManager`、`BilliardManager`、`BrickManager`、`BubbleGameManager`、`ChessManager`、`DrawCardManager`、`FlagGameManager`、`FruitNinjaManager`、`Game2048Manager`、`MinesweeperManager`、`SnakeManager`、`TowerManager`、`JigsawManager`、`ZombieBattleManager` | 宝石迷阵/台球/打砖块/泡泡/国际象棋/抽卡/夺旗/水果忍者/2048/扫雷/贪吃蛇/塔/拼图 |
| **旅行/新旅程** | `NewTravel`、`TravelSynergyMenu`、`Plants/Travel/*` | |
| **花轮** | `HuaLun.HuaLunManager` | |
| **植物进化** | `PlantEvolution.PlantEvolutionConfigLoader` | |

### 5.4 内置可视化节点图关卡编辑器（重要发现）

`GameLevel.EventNodes` 有 **146 个类型**，配合运行时日志，可以确认游戏内建了一个**节点图（node graph）关卡编辑器**：

- 编辑器基础设施：`EventNodeCanvas`、`EventNodeCanvasInputHandler`（平移/框选）、`EventNodeContextMenu`、`AttributePanel`（变量面板）
- 节点类型（部分，均为实测日志/类名）：
  `DamagePlantNode`、`DamageZombieNode`、`DiePlantNode`、`CreateGraveNode`、`CreateLadderNode`、`CreateCraterNode`、`CreateInfoCardNode`、`AddPlantCardNode`、`DeleteCardByTypeNode`、`CounterNode`、`BoolValueNode`、`IntValueNode`、`FloatValueNode`、`StringValueNode`、`DivideNode`、`GameWinNode`、`GameOverNode`、`ModifyPlantAttackNode`、`ModifyPlantHealthNode`、`ModifyZombieHealthNode`、`MergePlantTypeListsNode`、`AddMultipleChoiceOptionNode`、`MergeMultipleChoiceOptionListsNode`、`IntModuloNode`

**含义**：作者已经做了"用节点图写关卡逻辑"的工具，且这些节点类是现成的扩展点。

---

## 6. 数据驱动架构与可魔改点

### 6.1 JSON 配置表（在 `data.unity3d` 内，`Assets/Resources/`）

```
PlantEvolutionData.json     ← 植物进化/融合配置
CustomPlantData.json        ← 自定义植物
DetailStrings.json          ← 植物详情文案
LawnStrings.json            ← 通用字符串表
ZombieStrings.json          ← 僵尸文案
GardenData.json / GardenUnifiedData.json / GardenData{0}.json
OpenBLive.json              ← B 站直播配置
LevelData/Explore/{0}.json  ← 探索关卡
```

配置缺失时的降级逻辑（字符串字面量实证）：

```
未找到 PlantEvolutionData.json，将使用硬编码数据
无法加载植物进化配置文件！请检查 Assets/Resources/PlantEvolutionData.json
```

音效同样是数据驱动 + 可诊断报错：

```
[GameAPP] 未找到音效 {0} (ID: {1})，请确保文件存在于 Resources/Audio/Sound/
```

### 6.2 资源路径即融合树

7,332 条 `Plants/...` 资源路径中，**目录层级本身就编码了融合链**，最深 5～6 层：

```
Plants/PeaShooter/LanternPea/UltimateLanternSplit/UltimateGarlicSplit/UltimateGarlicSplit
Plants/_Mixer/DoublePuff/SnowGatlingPuff/UltimateSnowGatlingPuff/UltimateSnowGatlingPuff
Plants/UniquePlants/PlantGirls/Cattail/HypnoCattailGril/HypnoCattailGirl
Plants/Blover/Bloverpult/MelonBlover          ← 三叶草 × 投手系
Plants/CobCannon/CabbageCannon/UltimateCabbageCannon/UltimateCabbageCannon
Plants/Travel/SniperPea/FireSniper/FireSniper
```

约定：`Plants/<基础植物>/<融合1>/<融合2>/…`，另有 `_Mixer`、`UniquePlants`、`Travel`、`Pot`、`_TowerPlant` 等特殊前缀。**71 个基础植物目录**。

资源顶层分布：`Plants/` 732 条、`Zombies/` 72 条、`Items/` 30 条、`UI/` 24 条、`Audio/` 3 条（总计 861 条，完整清单见 `09_asset_paths.txt`）。

融合衍生最多的基础植物（该系全部后代数量，反映"融合树"分支规模）：

```
 74  _Mixer（融合中间体，非真实植物）
 43  Pumpkin        32  WallNut       26  Blover
 26  FumeShroom     26  PeaShooter    26  Travel
 25  Pot            23  _TowerPlant   21  Melonpult
 20  Squash         19  Starfruit     19  Umbrellaleaf
 17  Caltrop        17  PotatoMine    15  SmallPuff
```

可见**南瓜、坚果、三叶草、大喷菇、豌豆射手**是融合体系的核心载体（"什么都能套南瓜/坚果"的设计）。

### 6.3 存档格式（明文 JSON，最好改的地方）

实测路径：`%USERPROFILE%\AppData\LocalLow\LanPiaoPiao\PlantsVsZombiesRH\`（即 Unity `persistentDataPath`）

| 文件 | 内容 |
|---|---|
| `playerData.json` | 全局进度：`advLevel`、`towerLevel`、`theMoneyCount`、`treasureSaveData.treasureMoney`(实测 999)、`advantureData`、各模式的 `*LevelCompleted[]` 位图 |
| `Player/Saves/level{N}_{slot}.json` | 关卡内存档：`plants[]`（`thePlantType`/`thePlantHealth`/`thePlantColumn`/`thePlantRow`/`theLilyType`/`theLevel`/`starUp`…）、`boardData`（`theBoardSun`、`sceneType`）、`boardStatistics` |
| `level{N}.json` | 同类关卡存档（旧格式路径） |
| `GardenUnifiedData.json` | 禅境花园：`allPlants[]`（`growStage`/`waterLevel`/`love`/`page`）、`totalPages` |
| `CustomIZ.json` | 我是僵尸自定义关卡 |
| `autosave.json`、`save{N}.json` | 自动/手动存档 |
| `Player.log` | 运行时日志（**逆向情报金矿**：含完整调用栈） |

存档自带版本校验字段 `version: "3.9"`，以及 `name`（如"最新备份"）、`savedTime`（Unix 时间戳）。

`boardStatistics` 字段示例（实测 level40）：`zombiesKilled: 402`、`plantsPlanted: 152`、`sunProduced: 23125`、`maxWave: 20`、`gameDuration: 910.49`。

---

## 7. 网络与外部集成

### 7.1 自建关卡服务器（HTTP API）

元数据里完整保留了 API 端点：

```
http://<自建服务器>:3000/api/QQ            ← QQ 相关
http://<自建服务器>:3000/api/Version       ← 版本检查
http://<自建服务器>:3000/api/validate-key  ← API 密钥校验
http://<自建服务器>:3000/api/levels
http://<自建服务器>:3000/api/level/{id}
http://<自建服务器>:3000/api/level/upload
http://<自建服务器>:3000/api/user-levels
http://<自建服务器>:3000/api/user-level/{id}
http://<自建服务器>:3000/api/user-level/upload
```

对应报错字符串：`API密钥无效，无法删除关卡`、`获取到最新版本：`、`在注册表中更新了关卡：{0}，总计{1}关`。**注意这是明文 HTTP，无 TLS。**

### 7.2 B 站直播开放平台（弹幕互动）

- `OpenBLive.Runtime` / `OpenBLive.Runtime.Data` / `Utilities`，配置 `OpenBLive.json`
- 端点：`https://live-open.biliapi.com`、`http://test-live-open.biliapi.net`
- 作者主页：`<作者B站空间>`
- 官方 Wiki：`https://wiki.biligame.com/pvzrh`

说明作者做了**直播弹幕驱动玩法**（观众互动）。

### 7.3 其他网络栈

`NativeWebSocket`、UnityWebRequest，原生层导入 `WS2_32.dll`（`getaddrinfo`/`WSASend`）、`IPHLPAPI.DLL`（`GetAdaptersAddresses`）。

---

## 8. 反编译方法与下一步

### 8.1 现状评估：可直接深入

- **无混淆**：241 个 `il2cpp_*` 导出、方法名/类名/字段名完好（连中文类名都在）
- 原生层导入 `IsDebuggerPresent`、`SymInitialize/SymFromAddr`（dbghelp，崩溃上报）、`CryptGenRandom` —— 属 Unity 默认行为，**未见自研反调试/反篡改**
- `GameAssembly.dll` 节区为标准 IL2CPP 布局：`.text` / **`il2cpp`(38 MB，存类型与元数据指针)** / `.rdata` / `.data` / `.pdata` / `.reloc`

### 8.2 推荐工具链（本机已具备 python / dotnet，未安装以下工具）

```powershell
# 1) IL2CPP → 伪 DLL + 结构头文件（首选 Il2CppDumper v6.7+，支持 metadata v31）
Il2CppDumper.exe GameAssembly.dll `
  "PlantsVsZombiesRH_Data\il2cpp_data\Metadata\global-metadata.dat" out\

# 2) 伪 DLL 用 ILSpy / dnSpy 打开，得到接近源码的 C#（Assembly-CSharp.dll）
#    备选：Cpp2IL、Il2CppInspector

# 3) 资源包用 AssetStudio / AssetRipper 直接打开 data.unity3d
#    或 python: pip install UnityPy  → UnityPy.load("data.unity3d")
```

### 8.3 建议的入手顺序（性价比排序）

1. **`PlantEvolutionData.json` / `CustomPlantData.json`** —— 融合配方与数值，改这里就能改玩法（需重打包 `data.unity3d`）
2. **存档 JSON** —— 直接改 `playerData.json` / `level*.json` 即可解锁进度、改阳光/金币（无需重打包，最省事）
3. **`SaveMgr` / `Board` / `PlantMixTreeManager`** —— 看核心逻辑
4. **`GameLevel.EventNodes`** —— 若想做新关卡模式，节点图是现成框架
5. **`SynergyManager` + `Invest_*` / `Debuff_*`** —— 中文类名一目了然，适合做平衡性调整

### 8.4 开发痕迹（有趣的旁证）

- 类名 `PlatyerSettings`（拼写错误，被保留在发布版）
- `PlantMixTreeManagerExample`（示例类未删）
- `com.cyborgAssets.internalIBPExample`（编辑器插件示例编进了运行时）
- 调试开关字符串：`debug模式开启` / `debug模式关闭` / `cheatmode` / `作弊模式已开启` / `UNLOCK`
- 教学提示字符串泄露了操作：`Tips：右键植物可以查看全部融合`、`Tips：试试在游戏中把鼠标放在植物上然后按下C键`
- 关卡编辑器快捷键：`清除僵尸（Y）`、`清除植物（U）`、`随机卡槽（O）`、`右键放罐（I）`

---

## 9. 本次分析产出

| 文件 | 内容 |
|---|---|
| `meta_parse.py` | IL2CPP 元数据解析器（自研，v31） |
| `pe_info.py` | PE 节区/导出/导入分析器 |
| `enum_extract.py` | 枚举成员提取器 |
| `01_assemblies.txt` | 71 个程序集及类型数 |
| `02_types.txt` | 全量类型表（563 KB，`程序集\t命名空间\t类型名`） |
| `03_namespaces.txt` | 非引擎命名空间统计 |
| `04_game_types.txt` | 游戏逻辑类型清单（含中文类名） |
| `05_strings_interesting.txt` | 分类字符串：URL/路径/调试/玩法（3,719 行） |
| `06_chinese_text.txt` | 3,046 行中文文案/日志（含开发期调试日志） |
| `07_enum_summary.txt` | 538 个疑似枚举及成员数 |
| `08_{PlantType,ZombieType,SceneType,SynergyType,EffectType,ItemType}_members.txt` | 枚举成员名单（声明顺序） |
| `09_asset_paths.txt` | 861 条 Unity 资源路径（植物/僵尸/道具/UI/音频） |
| `bundle_parse.py` / `bundle_parse_result.json` | UnityFS 归档解析器与结构化结果 |
| `10_bundle_report.md` / `11_bundle_paths.txt` | `data.unity3d` 归档取证报告与 9 个内部文件清单 |
| `00_分析报告.md` | 本报告 |
| `dbg_*.py` | 枚举数值编码的排查脚本（下方"未确定项"） |

---

## 10. 明确未能确定的项（避免误导）

1. **枚举数值（ID）未能可靠还原**。`08_*_members.txt` 里给的是**声明顺序**，不是枚举数值。
   原因：`Il2CppFieldDefaultValue` 的 `dataIndex` 指向的数据宽度无法从 `global-metadata.dat` 单独判定——它需要解析 `GameAssembly.dll` 中 `il2cpp` 节区的 `Il2CppType` 表（元数据 v27+ 起类型表在二进制里，不在 metadata 文件里）。我实测了 1/2/4 字节三种解释均不自洽，且全文件暴力搜索不到 `0,1,2,…` 连续序列（说明存在**值去重共享**），故**不予给出可能错误的 ID 表**。
   → 正确做法：用 Il2CppDumper 导出（它会读二进制类型表），ID 表在 `dump.cs` 里；或直接读资源包内的 JSON 配置。
2. **`data.unity3d` 内部的对象级清单未枚举**：归档层已完全解析（9 个内部文件见 3.1），6 个 SerializedFile 的头部也已 6/6 校验通过；但**版本 22 的元数据体含变长的 per-type 依赖数组**，未能写出一个恰好消耗 `metadataSize` 字节的解析器，因此**对象数量与 classID 直方图没有猜测、直接标记为未完成**。
   → 用 AssetStudio / AssetRipper / UnityPy 打开 `data.unity3d`（该容器它们可直接加载）即可列出全部对象，比手写解析划算得多。
3. **未对 2.1 GB 的 `resources.assets.resS` 做中文资源名扫描**（该文件是音频/贴图流数据，本轮有意跳过）。已确认在归档路径与 `level0`/`sharedassets0.assets` 中**没有**中文资源名或 mod 目录。
4. **`thePlantType: 1033`（存档）与 `PlantType` 枚举的对应关系**未验证——因为第 1 条未解决。1033 也可能是另一套 ID 空间（如卡片/注册表 ID）。
5. 未做动态分析（未运行游戏、未下断点、未抓包），所有网络端点仅来自字符串常量，**未验证服务端是否仍在线**。
6. 关于"构建在 Unity 出包后被改动过"（3.2 节）只是**依据旁挂 `.resource` 与内部 `.resS` 并存的推断**，未做进一步取证。
