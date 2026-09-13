using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppSystem.Collections.Generic;
using UnityEngine;

namespace PvzRhCheat
{
    /// <summary>公共逻辑：所有补丁最终都调用这里</summary>
    internal static class Actions
    {
        private static float _nextApply;
        private static float _nextFast;
        private static bool _warnedEmptyPool;
        private static bool _loggedOnce;

        // 主线程维护的植物列表/选中项
        private static readonly System.Collections.Generic.List<Plant> _plants =
            new System.Collections.Generic.List<Plant>();
        private static readonly System.Collections.Generic.HashSet<IntPtr> _seen = new System.Collections.Generic.HashSet<IntPtr>();
        private static Plant _selected;
        private static string _levelInfo = "?";
        private static int _gardenCount;
        private static string _plantSource = "none";

        /// <summary>每帧调用：刷新植物列表 + 周期施加</summary>
        internal static void FastTick()
        {
            if (!ModConfig.Enabled.Value) return;
            float now = Time.unscaledTime;
            if (now < _nextFast) return;
            _nextFast = now + 0.25f;

            RefreshPlants();
            try { RefreshZombies(); } catch (Exception e) { LogSlow("FastTick/僵尸", e); }
            RefreshLevelInfo();

            // 界面上点的"直接融合"在这里执行：不在 IMGUI 事件里动场景对象
            try { RunPendingFuse(); } catch (Exception e) { LogSlow("FastTick/融合", e); }
            // 批量变身
            try { RunPendingChangeAll(); } catch (Exception e) { LogSlow("FastTick/批量变身", e); }
            // 游戏速度：**只消费一次请求**，不再每帧把 timeScale 按回去。
            // 之前每 tick 都写 = 用户点一次就被永久锁死，连游戏自己的倍速滑条都改不动。
            try { RunPendingSpeed(); } catch (Exception e) { LogSlow("FastTick/速度", e); }
            // 卡片/工具无冷却：0.25 秒刷一次，别等 2 秒的施加周期（会看到冷却条回涨）
            try { FastCardTweak(); } catch (Exception e) { LogSlow("FastTick/卡片", e); }
            // 融合会换掉植物对象，重新读一次列表
            if (RefreshPlantsAgain()) RefreshPlants();

            try { IpcBridge.Tick(); } catch (Exception e) { LogSlow("FastTick/ipc", e); }
            try { Tick(); } catch (Exception e) { LogSlow("FastTick/tick", e); }

            // 心跳诊断：每 5 秒记录一次，进关卡后日志里就能看到到底读到了什么
            _ticks++;
            if (now >= _nextHeartbeat)
            {
                _nextHeartbeat = now + 5f;
                if (Actions.IsInLevel() && _heartbeatLogs < 60)
                {
                    _heartbeatLogs++;
                    Plugin.Log.LogInfo(string.Format(
                        "[心跳] 关卡={0} 来源={1} 植物={2} 花园={3} 子弹={4} 僵尸排数={5}",
                        _levelInfo, _plantSource, _plants.Count, _gardenCount,
                        BulletCount(), ZombieRows()));
                }
            }
        }

        private static int _ticks;

        // ---- 界面请求的"直接融合"（延迟到主循环里执行，避免在 IMGUI 事件中改场景）----
        //
        // 两阶段：
        //   阶段 0  找到这株植物、算出融合结果、让它 Die 退场；
        //   阶段 1  等格子腾空（Die 是异步的），再让游戏在空格子上**新建**结果植物。
        // 只有新建出来的植物才带正确的模型/血量/子弹/技能。
        private const int FuseIdle = 0, FuseEjecting = 1;
        private static int _fuseStage = FuseIdle;
        private static long _fusePtr;
        private static int _fusePartner = -1;
        private static int _fuseCol, _fuseRow, _fuseResult, _fuseTries;
        private static float _fusePosX, _fusePosY;
        private static string _fuseSelfName = "";
        private static bool _fuseDirty;

        internal static void RequestFuse(long plantPtr, int partnerType)
        {
            _fusePtr = plantPtr;
            _fusePartner = partnerType;
            _fuseStage = FuseIdle;
            _fuseTries = 0;
            _fuseDirty = false;
        }

        private static bool RefreshPlantsAgain() { bool d = _fuseDirty; _fuseDirty = false; return d; }

        internal static void RunPendingFuse()
        {
            if (_fusePartner < 0) return;

            if (_fuseStage == FuseIdle)
            {
                long ptr = _fusePtr;
                int partner = _fusePartner;
                Plant p = PlantByPtr(ptr);
                if (p == null) { Finish("融合失败：那株植物已经不在场上了"); return; }

                PlantDb.FusePlan plan;
                string err = PlantDb.PlanFuse(p, partner, out plan);
                if (err != null) { Finish("融合失败：" + err); return; }

                // 记下位置（供阶段 1 在空格子上重建）
                try { Vector3 wp = p.transform.position; _fusePosX = wp.x; _fusePosY = wp.y; } catch { }
                _fuseCol = plan.Col; _fuseRow = plan.Row; _fuseResult = plan.ResultType;
                _fusePtr = plan.OldPtr;
                _fuseSelfName = plan.SelfName;
                _fuseTries = 0;

                MenuUI.SetStatus("正在融合：" + plan.SelfName + " + " + PlantDb.CnName(partner) + " → "
                               + PlantDb.CnName(plan.ResultType) + " …（先让原植物退场）");

                err = PlantDb.EjectOld(p);
                if (err != null) { Finish("融合失败：" + err); return; }
                _fuseStage = FuseEjecting;
                return;
            }

            // 阶段 1：等格子空出来
            _fuseTries++;
            Plant still = PlantDb.CellPlant(_fuseCol, _fuseRow);
            if (still != null && _fuseTries < 8) return;    // 再等一个周期（0.25 秒）

            string how;
            if (still == null)
            {
                Plant created;
                string e2 = PlantDb.SpawnAt(_fuseCol, _fuseRow, _fuseResult,
                    new Vector2(_fusePosX, _fusePosY), out created);
                if (e2 == null && created != null)
                {
                    PlantDb.FinishNewPlant(_fusePtr, created);
                    how = "腾空格子后由游戏新建（模型/属性完整）";
                    Finish("融合成功：" + _fuseSelfName + " + " + PlantDb.CnName(_fusePartner) + " → "
                         + PlantDb.Label(created) + "   方式:" + how);
                    return;
                }
                how = "重建失败 " + e2;
            }
            else
            {
                how = "格子一直没腾空（Die 是异步的），改用原地改名";
            }

            // 兜底：原地改类型（模型可能不跟着换）
            Plant back = PlantDb.CellPlant(_fuseCol, _fuseRow);
            string e3 = PlantDb.InPlaceChange(back, _fuseResult);
            _fuseDirty = true;
            Finish(e3 == null
                ? ("融合成功（降级）：" + _fuseSelfName + " → " + PlantDb.CnName(_fuseResult) + "，但可能只是改了类型（" + how + "）")
                : ("融合失败：" + how + "；" + e3));
        }

        private static void Finish(string msg)
        {
            _fusePartner = -1;
            _fuseStage = FuseIdle;
            _fuseDirty = true;
            MenuUI.SetStatus(msg);
            Plugin.Log.LogInfo("[融合] " + msg);
        }
        private static float _nextHeartbeat;
        private static int _heartbeatLogs;

        private static int BulletCount()
        {
            try
            {
                Board b = Board.Instance;
                if (b == null || b.boardEntity == null) return -1;
                var l = b.boardEntity.bulletArray;
                return l == null ? -1 : l.Count;
            }
            catch { return -1; }
        }

        private static int ZombieRows()
        {
            try
            {
                Board b = Board.Instance;
                if (b == null || b.boardEntity == null) return -1;
                var d = b.boardEntity.waveZombies;
                return d == null ? -1 : d.Count;
            }
            catch { return -1; }
        }

        /// <summary>
        /// 多来源逐个尝试，并记录**每个来源各找到几只**，方便定位哪条路通：
        ///  A) Lawnf.GetAllPlants()                ← 游戏自己的"取全部植物"接口
        ///  B) Board.boardEntity.plantArray / hiddenPlants
        ///  C) 非泛型 FindObjectsOfType(Il2CppType) ← 泛型版在 IL2CPP 下可能静默返回空
        ///  另记录 Lawnf.GetPlantCount(Board) 作为游戏侧的校验值
        /// </summary>
        internal static void RefreshPlants()
        {
            _plants.Clear();
            _seen.Clear();
            _gardenCount = 0;

            int nA = 0, nB = 0, nC = 0, nD = 0;
            int gameCount = -1;
            bool boardNull = true, entityNull = true;

            // A) Lawnf.GetAllPlants() —— 注意它有时会抛 NullReferenceException，必须单独包住
            try
            {
                var list = Lawnf.GetAllPlants();
                if (list != null)
                    for (int i = 0; i < list.Count; i++) AddOne(list[i]);
            }
            catch (Exception e) { LogSlow("plants/Lawnf.GetAllPlants", e); }
            nA = _plants.Count;

            // B) Board.boardEntity
            try
            {
                Board b = Board.Instance;
                if (b != null)
                {
                    boardNull = false;
                    BoardEntity be = b.boardEntity;
                    if (be != null)
                    {
                        entityNull = false;
                        AddPlants(be.plantArray);
                        AddPlants(be.hiddenPlants);
                        AddPlants(be.plantHead);
                        // 按类型分组的全部植物
                        try
                        {
                            var heads = be.plantHeads;
                            if (heads != null)
                                foreach (var kv in heads) AddPlants(kv.Value);
                        }
                        catch (Exception e) { LogSlow("plants/plantHeads", e); }
                        try { var gp = be.gardenPlants; if (gp != null) _gardenCount = gp.Count; } catch { }
                    }
                    try { gameCount = Lawnf.GetPlantCount(b); } catch { }
                }
            }
            catch (Exception e) { LogSlow("plants/boardEntity", e); }
            nB = _plants.Count - nA;

            // C) 非泛型兜底
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType(
                    Il2CppInterop.Runtime.Il2CppType.Of<Plant>());
                int before = _plants.Count;
                if (arr != null)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var o = arr[i];
                        if (o == null) continue;
                        Plant p = null;
                        try { p = o.TryCast<Plant>(); } catch { }
                        if (p == null) { try { p = new Plant(o.Pointer); } catch { } }
                        AddOne(p);
                    }
                }
                nC = _plants.Count - before;
            }
            catch (Exception e) { LogSlow("plants/findNonGeneric", e); }

            // D) Lawnf.GetPlantsByRow(board, row) —— 只要行号，不依赖类型/坐标，最稳的一条
            try
            {
                Board b = Board.Instance;
                if (b != null)
                {
                    int before = _plants.Count;
                    for (int row = 0; row < 8; row++)
                    {
                        try { AddPlants(Lawnf.GetPlantsByRow(b, row)); }
                        catch { }
                    }
                    nD = _plants.Count - before;
                }
            }
            catch (Exception e) { LogSlow("plants/GetPlantsByRow", e); }

            // E) 网格兜底：Lawnf.GetPlant(列, 行, Board) 逐格问一次。
            //    只在前面全部落空时才跑，避免每 0.25 秒做几十次 native 调用。
            int nE = 0;
            if (_plants.Count == 0)
            {
                try
                {
                    Board b = Board.Instance;
                    if (b != null)
                    {
                        for (int row = 0; row < 7; row++)
                            for (int col = 0; col < 9; col++)
                            {
                                try { AddOne(Lawnf.GetPlant(col, row, b)); }
                                catch { }
                            }
                    }
                }
                catch (Exception e) { LogSlow("plants/grid", e); }
                nE = _plants.Count;
            }

            // 实测优先级：Lawnf.GetAllPlants 最准（在关卡内可用，主菜单时会抛异常，已单独包住）
            _plantSource = nA > 0 ? "Lawnf" : (nD > 0 ? "byRow" : (nB > 0 ? "board" : (nC > 0 ? "find" : (nE > 0 ? "grid" : "none"))));
            _srcLawnf = nA; _srcBoard = nB; _srcFind = nC; _srcByRow = nD; _srcGrid = nE; _gamePlantCount = gameCount;

            // 数量或来源变化时打一行，便于定位
            string sig = nA + "/" + nB + "/" + nC + "/" + nD + "/" + nE + "/" + gameCount + "/" + boardNull + entityNull;
            if (sig != _plantSig)
            {
                _plantSig = sig;
                if (!boardNull || nA > 0 || nD > 0 || nE > 0 || gameCount > 0)
                    Plugin.Log.LogInfo(string.Format(
                        "[植物] 来源={0} 合计={1}  | Lawnf={2} byRow={3} board={4} find={5} grid={6}  游戏侧计数={7}  Board为空={8} boardEntity为空={9} 花园={10} 关卡={11}",
                        _plantSource, _plants.Count, nA, nD, nB, nC, nE, gameCount, boardNull, entityNull, _gardenCount, _levelInfo));
            }
        }

        private static string _plantSig = "";
        private static int _srcLawnf, _srcBoard, _srcFind, _srcByRow, _srcGrid, _gamePlantCount;

        internal static int SrcLawnf() { return _srcLawnf; }
        internal static int SrcBoard() { return _srcBoard; }
        internal static int SrcFind() { return _srcFind; }
        internal static int SrcByRow() { return _srcByRow; }
        internal static int SrcGrid() { return _srcGrid; }
        internal static int GamePlantCount() { return _gamePlantCount; }

        private static bool _noPlantLogged;

        private static void AddPlants(Il2CppSystem.Collections.Generic.List<Plant> list)
        {
            if (list == null) return;
            int n = list.Count;
            for (int i = 0; i < n; i++) AddOne(list[i]);
        }

        private static void AddOne(Plant p)
        {
            if (p == null) return;
            IntPtr key;
            try { key = p.Pointer; } catch { return; }
            if (key == IntPtr.Zero) return;
            if (!_seen.Add(key)) return;
            _plants.Add(p);
        }

        /// <summary>读取当前关卡类型（LevelType + SceneType）</summary>
        internal static void RefreshLevelInfo()
        {
            var sb = new System.Text.StringBuilder(48);
            try
            {
                sb.Append(GameAPP.theBoardType.ToString());
                sb.Append(" Lv").Append(GameAPP.theBoardLevel);
            }
            catch { sb.Append("?"); }
            try
            {
                Board b = Board.Instance;
                if (b != null) sb.Append(" / ").Append(b.sceneType.ToString());
            }
            catch { }
            _levelInfo = sb.ToString();

            if (!_noPlantLogged && _plants.Count == 0 && IsInLevel())
            {
                _noPlantLogged = true;
                Plugin.Log.LogInfo("[植物] 当前在关卡中但植物列表为空（关卡=" + _levelInfo + "）");
            }
        }

        internal static bool IsInLevel()
        {
            try { return Board.Instance != null; } catch { return false; }
        }

        internal static string LevelInfo() { return _levelInfo; }
        internal static int GardenCount() { return _gardenCount; }
        internal static string PlantSource() { return _plantSource; }

        private static readonly System.Collections.Generic.HashSet<string> _slowLogged =
            new System.Collections.Generic.HashSet<string>();
        private static void LogSlow(string tag, Exception e)
        {
            if (_slowLogged.Add(tag)) Plugin.Log.LogWarning("[" + tag + "] " + e.GetType().Name + ": " + e.Message);
        }

        internal static Plant PlantAt(int index)
        {
            return (index >= 0 && index < _plants.Count) ? _plants[index] : null;
        }

        internal static System.Collections.Generic.List<Plant> PlantsSnapshot() { return _plants; }

        // ---- 融合配方（游戏自己的融合表：PlantMixTreeManager.GetTree(type).Recipes = 伙伴->结果）----
        private static int _recipeFor = -2;
        private static readonly System.Collections.Generic.List<int> _recipeFlat =
            new System.Collections.Generic.List<int>();

        internal static System.Collections.Generic.List<int> RecipeFlat() { return _recipeFlat; }

        internal static void InvalidateRecipes() { _recipeFor = -2; }

        internal static void EnsureRecipes(int curType)
        {
            if (_recipeFor == curType) return;
            _recipeFor = curType;
            _recipeFlat.Clear();
            if (curType < 0) return;
            try
            {
                PlantMixTreeNode node = PlantMixTreeManager.GetTree((PlantType)curType);
                if (node == null) { Plugin.Log.LogInfo("[融合] GetTree 返回 null: " + curType); return; }
                var rec = node.Recipes;
                if (rec == null) { Plugin.Log.LogInfo("[融合] Recipes 为 null: " + curType); return; }
                foreach (var kv in rec)
                {
                    _recipeFlat.Add((int)kv.Key);
                    _recipeFlat.Add((int)kv.Value);
                }
                Plugin.Log.LogInfo("[融合] 类型 " + curType + " 配方数 = " + (_recipeFlat.Count / 2));
            }
            catch (Exception e) { LogSlow("recipes/" + curType, e); }
        }

        internal static string RecipesJson()
        {
            int t = -1;
            try { if (_selected != null) t = (int)_selected.thePlantType; } catch { }
            if (t < 0) return "[]";
            EnsureRecipes(t);
            var sb = new System.Text.StringBuilder(512);
            sb.Append('[');
            for (int i = 0; i + 1 < _recipeFlat.Count; i += 2)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(_recipeFlat[i]).Append(',').Append(_recipeFlat[i + 1]).Append(']');
            }
            sb.Append(']');
            return sb.ToString();
        }

        internal static int SelectedIndex()
        {
            if (_selected == null) return -1;
            try
            {
                for (int i = 0; i < _plants.Count; i++)
                    if (_plants[i] != null && _plants[i].Pointer == _selected.Pointer) return i;
            }
            catch { }
            return -1;
        }

        // ---- 用指针寻址：植物列表每 0.25 秒重建，索引会变，指针稳定 ----
        internal static long SelectedPtr()
        {
            try { return _selected == null ? 0L : _selected.Pointer.ToInt64(); } catch { return 0L; }
        }

        internal static Plant PlantByPtr(long ptr)
        {
            if (ptr == 0L) return null;
            try
            {
                for (int i = 0; i < _plants.Count; i++)
                {
                    Plant p = _plants[i];
                    if (p == null) continue;
                    if (p.Pointer.ToInt64() == ptr) return p;
                }
            }
            catch { }
            return null;
        }

        internal static void SelectByPtr(long ptr)
        {
            Plant p = PlantByPtr(ptr);
            if (p != null) _selected = p;
        }

        internal static void Select(Plant p) { _selected = p; }

        internal static Plant SelectedPlant() { return _selected; }

        internal static void SelectByIndex(int index) { _selected = PlantAt(index); }

        internal static bool IsSelected(Plant p)
        {
            if (p == null || _selected == null) return false;
            try { return _selected.Pointer == p.Pointer; } catch { return false; }
        }

        internal static void Tick()
        {
            if (!ModConfig.Enabled.Value) return;

            float interval = ModConfig.ApplyInterval.Value;
            if (interval > 0f)
            {
                float now = Time.unscaledTime;
                if (now < _nextApply) return;
                _nextApply = now + Math.Max(interval, 0.5f);
            }
            else
            {
                if (_nextApply > 0f) return;   // 只施加一次
                _nextApply = 1f;
            }

            Step(UnlockAndResources, nameof(UnlockAndResources));
            Step(UnlockPlantsTweak,   nameof(UnlockPlantsTweak));
            Step(TopUpSun,            nameof(TopUpSun));
            Step(RestorePlants,       nameof(RestorePlants));
            Step(ApplyPlantOverrides, nameof(ApplyPlantOverrides));
            Step(TravelTweaks,        nameof(TravelTweaks));
            Step(ClassicCheats,       nameof(ClassicCheats));
            Step(ZombieHpTweak,       nameof(ZombieHpTweak));

            if (!_loggedOnce)
            {
                _loggedOnce = true;
                try
                {
                    var adv = GameAPP.advLevelCompleted;
                    Plugin.Log.LogInfo(string.Format(
                        "[首次施加] 金币={0} 开发者模式={1} 冒险关卡数组={2} 挑战={3} 小游戏={4} 生存={5} 旅行Mgr={6} Board={7}",
                        GameAPP.theMoneyCount, GameAPP.developerMode,
                        adv == null ? -1 : adv.Length,
                        GameAPP.clgLevelCompleted == null ? -1 : GameAPP.clgLevelCompleted.Length,
                        GameAPP.gameLevelCompleted == null ? -1 : GameAPP.gameLevelCompleted.Length,
                        GameAPP.survivalLevelCompleted == null ? -1 : GameAPP.survivalLevelCompleted.Length,
                        TravelMgr.Instance != null, Board.Instance != null));
                }
                catch (Exception e) { Plugin.Log.LogWarning("[首次施加] 统计日志失败: " + e.Message); }
            }
        }

        private static void Step(Action a, string name)
        {
            try { a(); }
            catch (Exception e) { Plugin.Log.LogWarning($"[{name}] {e.GetType().Name}: {e.Message}"); }
        }

        // ------------------------------------------------------------------ 解锁 / 资源
        internal static void UnlockAndResources()
        {
            long money = ModConfig.Money.Value;
            if (money > 0 && GameAPP.theMoneyCount != money) GameAPP.theMoneyCount = money;

            if (ModConfig.DeveloperMode.Value && !GameAPP.developerMode) GameAPP.developerMode = true;

            if (!ModConfig.UnlockAllLevels.Value) return;

            FillBoolArray(GameAPP.advLevelCompleted);
            FillBoolArray(GameAPP.clgLevelCompleted);
            FillBoolArray(GameAPP.gameLevelCompleted);
            FillBoolArray(GameAPP.survivalLevelCompleted);
            FillIntSet(GameAPP.exploreLevelCompleted, 600);
            FillIntSet(GameAPP.skinLevelCompleted, 600);
        }

        private static void FillBoolArray(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool> arr)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++) if (!arr[i]) arr[i] = true;
        }

        private static void FillIntSet(Il2CppSystem.Collections.Generic.HashSet<int> set, int count)
        {
            if (set == null) return;
            for (int i = 0; i < count; i++) if (!set.Contains(i)) set.Add(i);
        }

        // ------------------------------------------------------------------ 关卡内阳光
        internal static void TopUpSun()
        {
            if (!ModConfig.InfiniteSun.Value) return;
            Board board = Board.Instance;
            if (board == null) return;
            int floor = ModConfig.SunFloor.Value;
            if (board.theSun < floor) board.SetSun(floor);
        }

        // ------------------------------------------------------------------ 植物无敌（兜底回血）
        internal static void RestorePlants()
        {
            if (!ModConfig.GodModePlants.Value) return;
            for (int i = 0; i < _plants.Count; i++)
            {
                Plant p = _plants[i];
                if (p == null) continue;
                try
                {
                    if (p.thePlantHealth < p.thePlantMaxHealth) p.thePlantHealth = p.thePlantMaxHealth;
                    if (p.theShieldHealth < 0) p.theShieldHealth = 0;
                }
                catch { /* 某些子类字段可能不可用 */ }
            }
        }

        // ================================================================== 经典作弊
        /// <summary>周期性的经典作弊（自动收集 / 冻结 / 停速 / 秒杀 等）
        /// 卡片和工具的无冷却已经挪到 FastCardTweak（每 0.25 秒刷一次）。</summary>
        internal static void ClassicCheats()
        {
            try { if (ModConfig.AutoCollectSun.Value) TreasureData.autoCollect = true; }
            catch (Exception e) { LogSlow("cheat/autoCollect", e); }

            bool freeze = ModConfig.FreezeAllZombies.Value;
            if (freeze) ForEachZombie(FreezeOne);
            else if (_freezeWasOn) ForEachZombie(z => { try { ZombieDb.Thaw(z); } catch { } });   // 关掉时解冻
            _freezeWasOn = freeze;

            if (ModConfig.ZombiesStopMoving.Value) ForEachZombie(StopOne);
            if (ModConfig.AutoKillZombies.Value) ForEachZombie(KillOne);
        }

        private static bool _freezeWasOn;

        /// <summary>用非泛型 FindObjectsOfType，避免泛型实例化被裁剪时静默返回空</summary>
        internal static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Object>
            FindAll(Type t)
        {
            try { return UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.From(t)); }
            catch (Exception e) { LogSlow("FindAll/" + t.Name, e); return null; }
        }

        private static void ForEachZombie(Action<Zombie> fn)
        {
            // 直接复用多来源的僵尸列表（Lawnf.GetAllZombies + Board.zombieArray + FindObjectsOfType）。
            // 实测 Lawnf.GetAllZombies 对"手动放的僵尸"不认，只靠它会一个都遍历不到。
            var list = _zombies;
            for (int i = 0; i < list.Count; i++)
            {
                Zombie z = list[i];
                if (z == null) continue;
                try { fn(z); } catch { }
            }
        }

        /// <summary>数一下现在有多少僵尸（给界面显示用）</summary>
        internal static int ZombieCount()
        {
            int n = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                try
                {
                    var list = Lawnf.GetAllZombies(pass == 1);
                    if (list != null) n += list.Count;
                }
                catch { }
            }
            return n;
        }

        private static void FreezeOne(Zombie z) { try { ZombieDb.Freeze(z); } catch { } }
        private static void StopOne(Zombie z) { try { z.theSpeed = 0f; } catch { } }
        private static void KillOne(Zombie z) { try { z.Die(0); } catch { } }

        // ---------------------------------------------------------------- 僵尸列表 / 选中（和植物一个套路）
        private static readonly System.Collections.Generic.List<Zombie> _zombies =
            new System.Collections.Generic.List<Zombie>();
        private static readonly System.Collections.Generic.HashSet<IntPtr> _zseen =
            new System.Collections.Generic.HashSet<IntPtr>();
        private static Zombie _zsel;

        internal static void RefreshZombies()
        {
            _zombies.Clear();
            _zseen.Clear();

            // 多来源逐个尝试，并记录每个来源各找到几只（和植物列表一个套路）：
            //  A) Lawnf.GetAllZombies(false/true)   —— 游戏自己的接口，但实测对"手动放的僵尸"可能不认
            //  B) Board.zombieArray / zombieHead / zombieHeads —— 板子上的实际容器
            //  C) 非泛型 FindObjectsOfType(Zombie)  —— 兜底
            int nA = 0, nB = 0, nC = 0;

            for (int pass = 0; pass < 2; pass++)
            {
                try
                {
                    var list = Lawnf.GetAllZombies(pass == 1);
                    if (list != null)
                        for (int i = 0; i < list.Count; i++) AddZombie(list[i]);
                }
                catch (Exception e) { LogSlow("zombies/GetAllZombies", e); }
            }
            nA = _zombies.Count;

            try
            {
                Board b = Board.Instance;
                if (b != null)
                {
                    AddZombies(b.zombieArray);
                    AddZombies(b.zombieHead);
                    try
                    {
                        var heads = b.zombieHeads;
                        if (heads != null)
                            foreach (var kv in heads) AddZombies(kv.Value);
                    }
                    catch (Exception e) { LogSlow("zombies/zombieHeads", e); }
                }
            }
            catch (Exception e) { LogSlow("zombies/board", e); }
            nB = _zombies.Count - nA;

            try
            {
                var arr = FindAll(typeof(Zombie));
                int before = _zombies.Count;
                if (arr != null)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var o = arr[i];
                        if (o == null) continue;
                        Zombie z = null;
                        try { z = o.TryCast<Zombie>(); } catch { }
                        if (z == null) continue;
                        AddZombie(z);
                    }
                }
                nC = _zombies.Count - before;
            }
            catch (Exception e) { LogSlow("zombies/find", e); }

            _zombieSource = nB > 0 ? "board" : (nA > 0 ? "Lawnf" : (nC > 0 ? "find" : "none"));
            _zsrcLawnf = nA; _zsrcBoard = nB; _zsrcFind = nC;

            string sig = nA + "/" + nB + "/" + nC;
            if (sig != _zombieSig)
            {
                _zombieSig = sig;
                if (_zombies.Count > 0 || nA > 0 || nB > 0 || nC > 0)
                    Plugin.Log.LogInfo("[僵尸] 来源=" + _zombieSource + " 合计=" + _zombies.Count
                                     + "  | board=" + nB + " Lawnf=" + nA + " find=" + nC);
            }

            // 施加每只的行为覆盖（无敌/停速/持续冻结/魅惑）
            for (int i = 0; i < _zombies.Count; i++) ZombieOverrides.Apply(_zombies[i]);
        }

        private static void AddZombies(Il2CppSystem.Collections.Generic.List<Zombie> list)
        {
            if (list == null) return;
            int n = list.Count;
            for (int i = 0; i < n; i++) AddZombie(list[i]);
        }

        private static void AddZombie(Zombie z)
        {
            if (z == null) return;
            IntPtr k;
            try { k = z.Pointer; } catch { return; }
            if (k == IntPtr.Zero) return;
            if (!_zseen.Add(k)) return;
            _zombies.Add(z);
        }

        private static string _zombieSig = "";
        private static string _zombieSource = "none";
        private static int _zsrcLawnf, _zsrcBoard, _zsrcFind;

        internal static string ZombieSource() { return _zombieSource; }
        internal static int ZSrcBoard() { return _zsrcBoard; }
        internal static int ZSrcLawnf() { return _zsrcLawnf; }
        internal static int ZSrcFind() { return _zsrcFind; }

        internal static System.Collections.Generic.List<Zombie> ZombiesSnapshot() { return _zombies; }

        internal static Zombie ZombieAt(int index)
        {
            return (index >= 0 && index < _zombies.Count) ? _zombies[index] : null;
        }

        internal static Zombie SelectedZombie() { return _zsel; }
        internal static void SelectZombie(Zombie z) { _zsel = z; }

        internal static int ZombieSelIndex()
        {
            if (_zsel == null) return -1;
            try
            {
                for (int i = 0; i < _zombies.Count; i++)
                    if (_zombies[i] != null && _zombies[i].Pointer == _zsel.Pointer) return i;
            }
            catch { }
            return -1;
        }

        internal static long ZombieSelPtr()
        {
            try { return _zsel == null ? 0L : _zsel.Pointer.ToInt64(); } catch { return 0L; }
        }

        internal static Zombie ZombieByPtr(long ptr)
        {
            if (ptr == 0L) return null;
            try
            {
                for (int i = 0; i < _zombies.Count; i++)
                {
                    Zombie z = _zombies[i];
                    if (z == null) continue;
                    if (z.Pointer.ToInt64() == ptr) return z;
                }
            }
            catch { }
            return null;
        }

        internal static bool IsZombieSelected(Zombie z)
        {
            if (z == null || _zsel == null) return false;
            try { return _zsel.Pointer == z.Pointer; } catch { return false; }
        }

        /// <summary>卡片的原始数值（按对象指针缓存），关闭作弊时用来还原</summary>
        private struct CardBase
        {
            public float FullCd;
            public int Cost;
            public int MaxUse;
            public bool CdTouched, CostTouched, UseTouched;
        }

        private static readonly System.Collections.Generic.Dictionary<IntPtr, CardBase> _cardBase =
            new System.Collections.Generic.Dictionary<IntPtr, CardBase>();

        /// <summary>
        /// 卡片无冷却 / 免费种植 / 无限使用。
        ///
        /// 实测结论（2026-09-13，靠 `ACTION|Cards` 打印出来的数据）：
        ///   · 游戏每帧用**自己的计时器重算 CD**（`CD = fullCD - 已过时间`），
        ///     所以只写 `CD = 0` 完全没用：下一帧就被覆盖；而我们每 2 秒再拍一次 0，
        ///     表现就是"冷却条一直在重置"—— 正是用户报的那个现象。
        ///   · 真正有效的是把 `fullCD` 清零，这样 CD 会算成负数，卡片恒为"已就绪"。
        ///   · 关掉功能时要**还原**（只清标记不还原的话，得等下一关卡片重建才恢复），
        ///     所以这里按对象指针缓存原始 `fullCD` / `theSeedCost` / `maxUsedTimes`。
        /// </summary>
        private static void TweakCards()
        {
            bool noCd = ModConfig.NoCardCooldown.Value;
            bool free = ModConfig.FreePlanting.Value;
            bool unlim = ModConfig.UnlimitedCardUse.Value;

            var arr = FindAll(typeof(CardUI));
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
            {
                var o = arr[i];
                if (o == null) continue;
                CardUI c = null;
                try { c = o.TryCast<CardUI>(); } catch { }
                if (c == null) continue;
                try
                {
                    IntPtr key = c.Pointer;
                    CardBase b;
                    if (!_cardBase.TryGetValue(key, out b)) b = new CardBase();

                    if (noCd)
                    {
                        if (!b.CdTouched && c.fullCD > 0f) { b.FullCd = c.fullCD; b.CdTouched = true; }
                        c.CD = 0f;
                        c.fullCD = 0f;
                    }
                    else if (b.CdTouched)
                    {
                        if (c.fullCD <= 0.001f) c.fullCD = b.FullCd;
                        b.CdTouched = false;
                    }

                    if (free)
                    {
                        if (!b.CostTouched && c.theSeedCost > 0) { b.Cost = c.theSeedCost; b.CostTouched = true; }
                        c.theSeedCost = 0;
                    }
                    else if (b.CostTouched)
                    {
                        if (c.theSeedCost == 0) c.theSeedCost = b.Cost;
                        b.CostTouched = false;
                    }

                    if (unlim)
                    {
                        if (!b.UseTouched) { b.MaxUse = c.maxUsedTimes; b.UseTouched = true; }
                        c.maxUsedTimes = 9999;
                        if (c.usedTimes > 0) c.usedTimes = 0;
                    }
                    else if (b.UseTouched)
                    {
                        if (c.maxUsedTimes == 9999) c.maxUsedTimes = b.MaxUse;
                        b.UseTouched = false;
                    }

                    if (b.CdTouched || b.CostTouched || b.UseTouched) _cardBase[key] = b;
                    else _cardBase.Remove(key);
                }
                catch { }
            }
            // 关卡切换会重建卡片对象，缓存别无限涨
            if (_cardBase.Count > 600) _cardBase.Clear();
        }

        /// <summary>
        /// 卡片/工具的无冷却**每 0.25 秒就要刷一次**（不能等 2 秒的施加周期），
        /// 否则冷却条会先涨回去再被拍平，看起来就是"在闪/在重置"。
        /// 注意：**功能关掉时也要继续跑**，否则没机会把 fullCD 还原回去。
        /// </summary>
        internal static void FastCardTweak()
        {
            if (!ModConfig.Enabled.Value) return;
            if (ModConfig.NoCardCooldown.Value || ModConfig.FreePlanting.Value ||
                ModConfig.UnlimitedCardUse.Value || _cardBase.Count > 0)
                TweakCards();
            Tools.ZeroCooldowns();
        }

        // ---- 一次性动作（由 UI 按钮 / IPC 触发）----

        /// <summary>
        /// 模拟"玩家手动种一株"：直接调游戏的 SetPlant 且**不设** `PlantDb.Internal`，
        /// 所以会正常触发「一种种一排」。自检用。
        /// </summary>
        internal static string ActionPlayerPlant(int typeId, int col, int row)
        {
            try
            {
                CreatePlant cp = CreatePlant.Instance;
                if (cp == null) return "CreatePlant.Instance 为空（不在关卡内）";
                Plant p = cp.SetPlant(col, row, (PlantType)typeId, null, new Vector2(0f, 0f), true, true, null);
                if (p == null) return "SetPlant 返回空";
                return "已按玩家方式种下 " + PlantDb.Label(p) + " 于 (" + col + "," + row + ")";
            }
            catch (Exception e) { return "种植失败: " + e.GetType().Name + " " + e.Message; }
        }

        /// <summary>诊断：列出场上所有卡片的冷却状态（用来验证"卡片无冷却"有没有真的生效）</summary>
        internal static string ActionCardInfo()
        {
            var arr = FindAll(typeof(CardUI));
            if (arr == null) return "找不到 CardUI（不在关卡内？）";
            var sb = new System.Text.StringBuilder(512);
            int n = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                CardUI c = null;
                try { c = arr[i] == null ? null : arr[i].TryCast<CardUI>(); } catch { }
                if (c == null) continue;
                n++;
                if (n > 12) continue;
                string nm = "?";
                try { nm = PlantDb.CnName((int)c.thePlantType); } catch { }
                try
                {
                    sb.Append(nm).Append(": CD=").Append(c.CD.ToString("0.##"))
                      .Append(" fullCD=").Append(c.fullCD.ToString("0.##"))
                      .Append(" 花费=").Append(c.theSeedCost)
                      .Append(" 次数=").Append(c.usedTimes).Append('/').Append(c.maxUsedTimes)
                      .Append(" | ");
                }
                catch { }
            }
            string r = "卡片 " + n + " 张 -> " + sb.ToString();
            Plugin.Log.LogInfo("[卡片] " + r);
            return r;
        }
        internal static string ActionKillAllZombies()
        {
            int n = 0;
            ForEachZombie(z => { try { z.Die(0); n++; } catch { } });
            Plugin.Log.LogInfo("[作弊] 秒杀僵尸 " + n + " 只");
            return "秒杀僵尸 " + n + " 只";
        }

        internal static string ActionNextWave()
        {
            try
            {
                Board b = Board.Instance;
                if (b == null) return "不在关卡内";
                b.EnterNextRound();
                Plugin.Log.LogInfo("[作弊] 已跳到下一波");
                return "已跳到下一波";
            }
            catch (Exception e) { LogSlow("cheat/nextWave", e); return "下一波失败: " + e.Message; }
        }

        internal static string ActionSetSun(int value)
        {
            try
            {
                Board b = Board.Instance;
                if (b == null) return "不在关卡内";
                b.SetSun(value);
                return "阳光设为 " + value;
            }
            catch (Exception e) { LogSlow("cheat/setSun", e); return "设置阳光失败"; }
        }

        internal static string ActionTriggerMowers()
        {
            int n = 0;
            try
            {
                Board b = Board.Instance;
                if (b == null || b.mowerArray == null) return "不在关卡内";
                for (int i = 0; i < b.mowerArray.Count; i++)
                {
                    try
                    {
                        var m = b.mowerArray[i];
                        if (m != null && !m.started) { m.StartMove(); n++; }
                    }
                    catch { }
                }
            }
            catch (Exception e) { LogSlow("cheat/mower", e); }
            return "触发割草机 " + n + " 台";
        }

        internal static void ApplyPlantOverrides()
        {
            for (int i = 0; i < _plants.Count; i++) Overrides.Apply(_plants[i]);
        }

        // ================================================================== 对齐 Modified-Plus 的额外功能
        /// <summary>
        /// 游戏速度 —— **按一次应用一次，不常驻**。
        ///
        /// 游戏自己有一个 `GameSpeedMgr`（暂停菜单里的倍速滑条）+ `GameConfig.gameSpeed`
        /// + `GameSpeedMgr.Gears`（它自己的档位表）。所以严格来说游戏侧"有"这个功能，
        /// 只是没有公开的"设置倍速"方法 —— 它就是把 `GameConfig.gameSpeed` 应用到 `Time.timeScale`。
        ///
        /// 两个都写：
        ///   · `Time.timeScale`  —— 真正生效的地方
        ///   · `GameAPP.config.gameSpeed` —— 让游戏自己的滑条/文字也同步
        ///
        /// 踩过的坑：以前是在 FastTick 里**每个 tick 都写一遍**，结果
        /// "点一次就再也改不回来"（用户原话：循环锁死）—— 游戏自己的倍速滑条、
        /// 甚至暂停菜单都被这段代码按回去了。现在改成：
        ///   RequestGameSpeed(v)  只登记一个"待应用"的值（可以从任意线程调）
        ///   RunPendingSpeed()    在主循环里消费一次，应用后立刻清空
        /// 也就是说，之后游戏想怎么改速度都随它，我们不再插手。
        /// </summary>
        internal static void ApplyGameSpeed(float want)
        {
            if (want < 0.05f) want = 0.05f;
            if (want > 20f) want = 20f;

            try
            {
                GameConfig cfg = GameAPP.config;
                if (cfg != null) cfg.gameSpeed = want;
            }
            catch (Exception e) { LogSlow("speed/gameConfig", e); }

            try { Time.timeScale = want; } catch (Exception e) { LogSlow("speed/timeScale", e); }
        }

        private static float _pendSpeed = -1f;   // < 0 = 没有待应用的请求

        /// <summary>登记一次"把游戏速度设成 want"（IPC 线程 / IMGUI 都能调）</summary>
        internal static void RequestGameSpeed(float want)
        {
            if (want < 0.05f) want = 0.05f;
            if (want > 20f) want = 20f;
            _pendSpeed = want;
        }

        /// <summary>主循环里消费速度请求：应用一次就拉倒</summary>
        internal static void RunPendingSpeed()
        {
            float v = _pendSpeed;
            if (v < 0f) return;
            _pendSpeed = -1f;
            ApplyGameSpeed(v);
            Plugin.Log.LogInfo("[速度] 已应用一次：" + v.ToString("0.###") + "x（不再常驻锁定，之后游戏自己也能改）");
        }

        /// <summary>当前实际速度（给界面显示用）</summary>
        internal static float CurrentSpeed()
        {
            try { return Time.timeScale; } catch { return 1f; }
        }

        /// <summary>诊断：游戏侧和 Unity 侧的速度各是多少、游戏自己的档位有哪些</summary>
        internal static string SpeedInfo()
        {
            var sb = new System.Text.StringBuilder(300);
            try { sb.Append("Time.timeScale=").Append(Time.timeScale.ToString("0.###")); }
            catch { sb.Append("Time.timeScale=?"); }
            try { sb.Append("  GameConfig.gameSpeed=").Append(GameAPP.config == null ? "无config" : GameAPP.config.gameSpeed.ToString("0.###")); }
            catch (Exception e) { sb.Append("  gameSpeed读取失败 ").Append(e.GetType().Name); }
            try { sb.Append("  配置里的GameSpeed=").Append(ModConfig.GameSpeed.Value.ToString("0.###")); } catch { }
            try
            {
                var gears = GameSpeedMgr.Gears;
                sb.Append("  游戏档位=[");
                if (gears != null)
                    for (int i = 0; i < gears.Count; i++) { if (i > 0) sb.Append(','); sb.Append(gears[i].ToString("0.##")); }
                sb.Append(']');
            }
            catch (Exception e) { sb.Append("  档位读取失败 ").Append(e.GetType().Name); }
            try
            {
                var arr = Actions.FindAll(typeof(GameSpeedMgr));
                sb.Append("  GameSpeedMgr实例=").Append(arr == null ? -1 : arr.Length);
            }
            catch { }
            string r = sb.ToString();
            Plugin.Log.LogInfo("[速度] " + r);
            return r;
        }

        /// <summary>僵尸血量倍率 + 僵尸无敌兜底回血</summary>
        private static readonly System.Collections.Generic.Dictionary<IntPtr, long> _zBaseHp =
            new System.Collections.Generic.Dictionary<IntPtr, long>();

        internal static void ZombieHpTweak()
        {
            float mult = 1f;
            try { mult = ModConfig.ZombieHpMultiplier.Value; } catch { }
            bool inv = false;
            try { inv = ModConfig.ZombieInvincible.Value; } catch { }
            if (Math.Abs(mult - 1f) < 0.001f && !inv) return;

            ForEachZombie(z =>
            {
                try
                {
                    IntPtr key = z.Pointer;
                    if (Math.Abs(mult - 1f) > 0.001f)
                    {
                        long baseHp;
                        if (!_zBaseHp.TryGetValue(key, out baseHp) || baseHp <= 0)
                        {
                            baseHp = z.theMaxHealth;
                            if (baseHp <= 0) return;
                            _zBaseHp[key] = baseHp;
                        }
                        long want = (long)(baseHp * (double)mult);
                        if (want < 1L) want = 1L;
                        if (z.theMaxHealth != want)
                        {
                            double ratio = z.theMaxHealth > 0 ? (double)z.theHealth / z.theMaxHealth : 1.0;
                            z.theMaxHealth = want;
                            z.theHealth = (long)(want * ratio);
                        }
                    }
                    if (inv && z.theHealth < z.theMaxHealth) z.theHealth = z.theMaxHealth;
                }
                catch { }
            });
        }

        /// <summary>植物图鉴 / 植物池全解锁：填 GodManager.godData.unlockedPlants</summary>
        internal static void UnlockPlantsTweak()
        {
            bool on = false;
            try { on = ModConfig.UnlockAllPlants.Value; } catch { }
            if (!on) return;
            try
            {
                GodData gd = GodManager.godData;
                if (gd == null) return;
                var list = gd.unlockedPlants;
                if (list == null) return;
                foreach (object o in Enum.GetValues(typeof(PlantType)))
                {
                    int v = Convert.ToInt32(o);
                    if (v < 0) continue;
                    PlantType t = (PlantType)v;
                    try { if (!list.Contains(t)) list.Add(t); } catch { }
                }
            }
            catch (Exception e) { LogSlow("unlockPlants", e); }
        }

        // ---- 一次性动作：植物 / 僵尸 批量 ----
        internal static string ActionKillAllPlants()
        {
            int n = 0;
            for (int i = 0; i < _plants.Count; i++)
            {
                Plant p = _plants[i];
                if (p == null) continue;
                try { p.Die(); n++; } catch { }
            }
            Plugin.Log.LogInfo("[作弊] 清除植物 " + n + " 株");
            return "已清除植物 " + n + " 株";
        }

        internal static string ActionHealAllPlants()
        {
            int n = 0;
            for (int i = 0; i < _plants.Count; i++)
            {
                Plant p = _plants[i];
                if (p == null) continue;
                try
                {
                    if (p.thePlantHealth < p.thePlantMaxHealth) { p.thePlantHealth = p.thePlantMaxHealth; n++; }
                }
                catch { }
            }
            string m = "已补满 " + n + " 株植物的血量";
            Plugin.Log.LogInfo("[作弊] " + m);
            return m;
        }

        internal static string ActionUpgradeAllPlants(int level)
        {
            if (level < 1) level = 1;
            if (level > 99) level = 99;
            int n = 0, fail = 0;
            string firstErr = null;
            for (int i = 0; i < _plants.Count; i++)
            {
                Plant p = _plants[i];
                if (p == null) continue;
                try
                {
                    if (p.Upgrade(level, true, false)) { n++; continue; }
                    if (firstErr == null) firstErr = "Upgrade 返回 false";
                }
                catch (Exception e) { if (firstErr == null) firstErr = "Upgrade: " + e.GetType().Name + " " + e.Message; }

                try { p.theLevel = level; n++; }
                catch (Exception e2)
                {
                    fail++;
                    if (firstErr == null) firstErr = "写 theLevel: " + e2.GetType().Name + " " + e2.Message;
                }
            }
            string m = "已把 " + n + " 株植物升到 " + level + " 级"
                     + (fail > 0 ? ("（" + fail + " 株失败；" + (firstErr ?? "?") + "）") : "");
            Plugin.Log.LogInfo("[作弊] " + m);
            return m;
        }

        internal static string ActionMindControlAll()
        {
            int n = 0;
            ForEachZombie(z =>
            {
                try { if (!z.isMindControlled) { z.SetMindControl(0); n++; } }
                catch { }
            });
            string m = "已魅惑 " + n + " 只僵尸";
            Plugin.Log.LogInfo("[作弊] " + m);
            return m;
        }

        internal static string ActionZombieInvincibleToggle() { return ""; }

        internal static string ActionSetAllZombieHp(double mult)
        {
            if (mult < 0.1) mult = 0.1;
            if (mult > 100) mult = 100;
            int n = 0;
            ForEachZombie(z =>
            {
                try
                {
                    long baseHp = z.theMaxHealth;
                    IntPtr key = z.Pointer;
                    long stored;
                    if (_zBaseHp.TryGetValue(key, out stored) && stored > 0) baseHp = stored;
                    else _zBaseHp[key] = baseHp;
                    long want = (long)(baseHp * mult);
                    if (want < 1L) want = 1L;
                    z.theMaxHealth = want;
                    z.theHealth = want;
                    n++;
                }
                catch { }
            });
            return "已把 " + n + " 只僵尸的血量设为 " + mult.ToString("0.##") + " 倍";
        }

        /// <summary>
        /// 所有僵尸变成指定类型。
        /// Zombie 没有 ReplaceSprite（改了类型贴图不会变），所以走"先在同位置生成新的，再让老的死"，
        /// 这样新僵尸是游戏自己完整初始化的。
        /// </summary>
        internal static string ActionChangeAllZombies(int typeId)
        {
            if (typeId < 0) return "类型无效";
            var old = new System.Collections.Generic.List<float[]>();   // row, x, mind
            ForEachZombie(z =>
            {
                try
                {
                    float x = 9.9f;
                    try { x = z.transform.position.x; } catch { }
                    old.Add(new[] { z.theZombieRow, x, z.isMindControlled ? 1f : 0f });
                }
                catch { }
            });

            int n = 0;
            foreach (float[] d in old)
            {
                int row = (int)d[0];
                float x = d[1];
                if (x <= 0f || x > 12f) x = 9.9f;
                string r = ActionSpawnZombie(row, typeId, x, d[2] > 0.5f);
                if (r.StartsWith("已在")) n++;
            }
            int killed = 0;
            ForEachZombie(z =>
            {
                try { z.Die(0); killed++; } catch { }
            });
            return "所有僵尸已变成 #" + typeId + "：" + n + " 只新建，旧僵尸清掉 " + killed + " 只";
        }

        internal static string ActionTravelNextRound()
        {
            try
            {
                Board b = Board.Instance;
                if (b == null) return "不在关卡内";
                b.TravelNextRound();
                return "已跳到旅行模式的下一回合";
            }
            catch (Exception e) { return "旅行下一回合失败: " + e.GetType().Name; }
        }

        internal static string ActionSetLevelName(string name)
        {
            // 游戏侧没有可安全写入的关卡名字段（LevelName1 是 UI 上的 TextMeshProUGUI），
            // 这个功能价值也不高，暂不实现，避免为了改一个标题去碰 UI 对象。
            return "该功能暂未实现（游戏侧没有可安全写入的关卡名字段）";
        }

        // ---- 沙盒：放置僵尸 / 小推车 ----
        /// <summary>
        /// 直接进关卡：UIMgr.EnterGame(LevelType, levelNumber, id, name) 是游戏自己的入口。
        /// 顺带解决"必须在关卡内才能用沙盒功能"的问题。
        /// </summary>
        internal static string ActionEnterGame(int levelType, int levelNumber)
        {
            if (levelNumber < 1) levelNumber = 1;
            try
            {
                UIMgr.EnterGame((LevelType)levelType, levelNumber, -1, null);
                Plugin.Log.LogInfo("[进关卡] LevelType=" + levelType + " 第 " + levelNumber + " 关");
                return "正在进入 关卡类型" + levelType + " 第 " + levelNumber + " 关…";
            }
            catch (Exception e) { return "进关卡失败: " + e.GetType().Name + " " + e.Message; }
        }
        internal static string ActionSpawnZombie(int row, int typeId, float x, bool mind)
        {
            if (typeId < 0) return "僵尸类型无效";
            if (row < 0) row = 0;
            if (row > 6) row = 6;
            if (x <= 0f || x > 12f) x = 9.9f;
            try
            {
                CreateZombie cz = CreateZombie.Instance;
                if (cz == null) return "CreateZombie.Instance 为空（不在关卡内）";
                Zombie z = mind
                    ? cz.SetZombieWithMindControl(row, (ZombieType)typeId, x, true)
                    : cz.SetZombie(row, (ZombieType)typeId, x, false);
                if (z == null) return "SetZombie 返回空";
                _zBaseHp.Remove(z.Pointer);
                return "已在第 " + (row + 1) + " 行 x=" + x.ToString("0.##") + " 放置僵尸 #" + typeId + (mind ? "（魅惑）" : "");
            }
            catch (Exception e) { return "放置僵尸失败: " + e.GetType().Name + " " + e.Message; }
        }

        internal static string ActionSpawnMower(int row, int mowerTypeId)
        {
            if (row < 0) row = 0;
            if (row > 6) row = 6;
            if (mowerTypeId < 0) mowerTypeId = 0;
            try
            {
                CreateMower cm = CreateMower.Instance;
                if (cm == null) return "CreateMower.Instance 为空（不在关卡内）";
                Mower m = cm.SetMower((MowerType)mowerTypeId, 0f, row);
                if (m == null) return "SetMower 返回空";
                return "已在第 " + (row + 1) + " 行放置小推车 #" + mowerTypeId;
            }
            catch (Exception e) { return "放置小推车失败: " + e.GetType().Name + " " + e.Message; }
        }

        /// <summary>所有植物变成指定类型（走已验证的两阶段重建）</summary>
        internal static string ActionChangeAllPlants(int typeId)
        {
            if (typeId < 0) return "类型无效";

            // 关键：坐标必须在"让它们退场之前"记下来。
            // 之前是每周期从 _plants 重新推坐标，植物一死列表就空了，结果只杀了不重建。
            _changeAllCells.Clear();
            int n = 0;
            for (int i = 0; i < _plants.Count; i++)
            {
                Plant p = _plants[i];
                if (p == null) continue;
                try
                {
                    int col = p.thePlantColumn, row = p.thePlantRow;
                    if (col < 0 || row < 0) continue;
                    _changeAllCells.Add(new[] { col, row });
                    n++;
                }
                catch { }
            }
            foreach (int[] c in _changeAllCells)
            {
                Plant cur = PlantDb.CellPlant(c[0], c[1]);
                if (cur == null) continue;
                try { PlantDb.EjectOld(cur); } catch { }
            }
            _changeAllTo = typeId;
            _changeAllTries = 0;
            string rm = "正在把 " + n + " 株植物变成 " + PlantDb.CnName(typeId) + " …（记下 " + _changeAllCells.Count + " 个格子，分周期重建）";
            Plugin.Log.LogInfo("[作弊] " + rm);
            return rm;
        }

        private static int _changeAllTo = -1;
        private static int _changeAllTries;
        private static readonly System.Collections.Generic.List<int[]> _changeAllCells =
            new System.Collections.Generic.List<int[]>();

        /// <summary>批量变身的第二阶段：等格子腾空后逐个重建（用第一阶段记下的格子）</summary>
        internal static void RunPendingChangeAll()
        {
            if (_changeAllTo < 0) return;
            _changeAllTries++;

            int done = 0;
            var still = new System.Collections.Generic.List<int[]>();
            foreach (int[] c in _changeAllCells)
            {
                Plant cur = null;
                try { cur = PlantDb.CellPlant(c[0], c[1]); } catch { }

                if (cur != null)
                {
                    int t = -1;
                    try { t = (int)cur.thePlantType; } catch { }
                    if (t == _changeAllTo) { done++; continue; }     // 已经是目标类型
                }

                Plant created;
                string e2 = PlantDb.SpawnAt(c[0], c[1], _changeAllTo,
                                            new Vector2(0f, 0f), out created);
                if (e2 == null) { done++; continue; }
                still.Add(c);                                        // 格子还被占着，下个周期再试
            }
            _changeAllCells.Clear();
            _changeAllCells.AddRange(still);

            if (done > 0)
            {
                string dm = "批量变身：已重建 " + done + " 株（剩余 " + _changeAllCells.Count + "）";
                MenuUI.SetStatus(dm);
                Plugin.Log.LogInfo("[作弊] " + dm);
            }
            if (_changeAllCells.Count == 0 || _changeAllTries >= 12)
            {
                int total = _changeAllTo;
                _changeAllTo = -1;
                _changeAllCells.Clear();
                string em = "批量变身结束（目标 " + PlantDb.CnName(total) + "）";
                MenuUI.SetStatus(em);
                Plugin.Log.LogInfo("[作弊] " + em);
            }
        }

        // ---- 阵容码（自己的文本格式，纯本地，不联网）----
        /// <summary>只返回阵容码本身（给界面输入框用）</summary>
        internal static string ActionExportLineupCode() { return BuildLineupCode(); }

        internal static string ActionExportLineup()
        {
            string code = BuildLineupCode();
            Plugin.Log.LogInfo("[阵容码] " + code);
            return "阵容码已写入日志（植物 " + _plants.Count + " 株）：\n" + code;
        }

        private static string BuildLineupCode()
        {
            var sb = new System.Text.StringBuilder(512);
            sb.Append("PVZRH1;");
            for (int i = 0; i < _plants.Count; i++)
            {
                Plant p = _plants[i];
                if (p == null) continue;
                try { sb.Append("P").Append(p.thePlantColumn).Append(',').Append(p.thePlantRow).Append(',').Append((int)p.thePlantType).Append(';'); }
                catch { }
            }
            ForEachZombie(z =>
            {
                try
                {
                    float x = 9.9f;
                    try { x = z.transform.position.x; } catch { }
                    sb.Append("Z").Append(z.theZombieRow).Append(',').Append((int)z.theZombieType)
                      .Append(',').Append(z.isMindControlled ? 1 : 0).Append(',')
                      .Append(x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                }
                catch { }
            });
            return sb.ToString();
        }

        internal static string ActionImportLineup(string code)
        {
            if (string.IsNullOrEmpty(code) || !code.StartsWith("PVZRH1")) return "阵容码格式不对（应以 PVZRH1 开头）";
            int np = 0, nz = 0, bad = 0;
            foreach (string part in code.Split(';'))
            {
                if (part.Length < 2) continue;
                string[] f = part.Substring(1).Split(',');
                try
                {
                    if (part[0] == 'P' && f.Length >= 3)
                    {
                        int col = int.Parse(f[0]), row = int.Parse(f[1]), t = int.Parse(f[2]);
                        Plant created;
                        if (PlantDb.SpawnAt(col, row, t, new Vector2(0f, 0f), out created) == null) np++;
                        else bad++;
                    }
                    else if (part[0] == 'Z' && f.Length >= 3)
                    {
                        int row = int.Parse(f[0]), t = int.Parse(f[1]);
                        bool mind = f.Length > 2 && f[2] == "1";
                        float x = 9.9f;
                        if (f.Length > 3) float.TryParse(f[3], System.Globalization.NumberStyles.Float,
                                                         System.Globalization.CultureInfo.InvariantCulture, out x);
                        if (ActionSpawnZombie(row, t, x, mind).StartsWith("已在")) nz++;
                        else bad++;
                    }
                }
                catch { bad++; }
            }
            return "阵容码已应用：植物 " + np + " 株 / 僵尸 " + nz + " 只" + (bad > 0 ? ("（" + bad + " 条失败）") : "");
        }

        // ------------------------------------------------------------------ 旅行模式强化
        internal static void TravelTweaks()
        {
            if (!ModConfig.TravelBuffs.Value) return;
            TravelMgr tm = TravelMgr.Instance;
            if (tm == null) return;
            tm.damageReduction       = ModConfig.TravelDamageReduction.Value;
            tm.luckyStrike           = ModConfig.TravelLuckyStrike.Value;
            tm.damageAmplification   = ModConfig.TravelDamageAmplification.Value;
            tm.plantZeroHealth       = ModConfig.TravelPlantZeroHealth.Value;
        }

        // ------------------------------------------------------------------ 天赋树
        internal static void UnlockTalents(AdvantureData data)
        {
            if (data == null) return;

            int stars = ModConfig.TalentStars.Value;
            if (stars > 0)
            {
                if (data.enpowerStarCount < stars) data.enpowerStarCount = stars;
                if (data.enpowerStarCount_hard < stars) data.enpowerStarCount_hard = stars;
            }

            if (ModConfig.TalentFreeHardModeOff.Value)
            {
                if (data.hardMode) data.hardMode = false;
                if (data.gameDifficulty != 0) data.gameDifficulty = 0;
            }

            if (!ModConfig.TalentUnlockAll.Value) return;

            Array all = Enum.GetValues(typeof(TalentType));
            foreach (object o in all)
            {
                try
                {
                    TalentType t = (TalentType)o;
                    if (!data.CheckTalent(t)) data.GetTalent(t);
                }
                catch { /* 个别天赋可能有前置要求 */ }
            }
        }

        // ------------------------------------------------------------------ 词条白名单
        private static System.Collections.Generic.HashSet<int> _buffAllow, _ultiAllow;

        private static System.Collections.Generic.HashSet<int> BuildAllow(string csv, Type enumType)
        {
            var set = new System.Collections.Generic.HashSet<int>();
            if (string.IsNullOrWhiteSpace(csv)) return set;
            foreach (string raw in csv.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string n = raw.Trim();
                if (n.Length == 0) continue;
                try
                {
                    object v = Enum.Parse(enumType, n, true);
                    set.Add(Convert.ToInt32(v));
                }
                catch
                {
                    Plugin.Log.LogWarning($"[词条白名单] 无法识别的名称: {n}");
                }
            }
            return set;
        }

        internal static void FilterBuffPool(ref Il2CppSystem.Collections.Generic.List<AdvBuff> pool)
        {
            if (pool == null) return;
            if (_buffAllow == null) _buffAllow = BuildAllow(ModConfig.BuffWhitelist.Value, typeof(AdvBuff));
            if (_buffAllow.Count == 0) return;

            int keep = 0;
            for (int i = 0; i < pool.Count; i++) if (_buffAllow.Contains((int)pool[i])) keep++;
            if (keep == 0)
            {
                if (!_warnedEmptyPool)
                {
                    _warnedEmptyPool = true;
                    Plugin.Log.LogWarning("[词条白名单] 过滤后为空，本次不过滤（请检查名称是否写对）");
                }
                return;
            }
            for (int i = pool.Count - 1; i >= 0; i--)
                if (!_buffAllow.Contains((int)pool[i])) pool.RemoveAt(i);
        }

        internal static void FilterUltiPool(ref Il2CppSystem.Collections.Generic.List<UltiBuff> pool)
        {
            if (pool == null) return;
            if (_ultiAllow == null) _ultiAllow = BuildAllow(ModConfig.UltiBuffWhitelist.Value, typeof(UltiBuff));
            if (_ultiAllow.Count == 0) return;

            int keep = 0;
            for (int i = 0; i < pool.Count; i++) if (_ultiAllow.Contains((int)pool[i])) keep++;
            if (keep == 0) return;
            for (int i = pool.Count - 1; i >= 0; i--)
                if (!_ultiAllow.Contains((int)pool[i])) pool.RemoveAt(i);
        }

        /// <summary>配置热重载时清掉缓存</summary>
        internal static void InvalidateBuffCache() { _buffAllow = null; _ultiAllow = null; }

        // ------------------------------------------------------------------ 深渊抽奖券
        internal static void MaxTickets(GameLevel.Abyss.AbyssData d)
        {
            if (d == null) return;
            const int N = 99999;
            if (d.woodenTicket  < N) d.woodenTicket  = N;
            if (d.silverTicket  < N) d.silverTicket  = N;
            if (d.goldTicket    < N) d.goldTicket    = N;
            if (d.diamondTicket < N) d.diamondTicket = N;
        }
    }

    // ====================================================================== 补丁
    // 驱动：GameAPP.Update 每帧被调用
    [HarmonyPatch(typeof(GameAPP), "Update")]
    internal static class Patch_GameApp_Update
    {
        [HarmonyPostfix]
        private static void Postfix() { Actions.FastTick(); }
    }

    // 初始施加一次（存档已加载完之后）
    [HarmonyPatch(typeof(GameAPP), "Awake")]
    internal static class Patch_GameApp_Awake
    {
        [HarmonyPostfix]
        private static void Postfix() { Actions.Tick(); }
    }

    // 无限阳光：花阳光时不扣
    [HarmonyPatch(typeof(Board), "UseSun")]
    internal static class Patch_Board_UseSun
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !(ModConfig.Enabled.Value && ModConfig.InfiniteSun.Value);
        }
    }

    // 无限金币（关卡内）
    [HarmonyPatch(typeof(Board), "UseMoney")]
    internal static class Patch_Board_UseMoney
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return !(ModConfig.Enabled.Value && ModConfig.InfiniteSun.Value);
        }
    }

    // 植物无敌：直接跳过受击
    [HarmonyPatch(typeof(Plant), "TakeDamage",
        new Type[] { typeof(int), typeof(IDamageMaker), typeof(DamageType), typeof(PlantType), typeof(bool) })]
    internal static class Patch_Plant_TakeDamage
    {
        [HarmonyPrefix]
        private static bool Prefix() { return !(ModConfig.Enabled.Value && ModConfig.GodModePlants.Value); }
    }

    [HarmonyPatch(typeof(Plant), "RealTakeDamage")]
    internal static class Patch_Plant_RealTakeDamage
    {
        [HarmonyPrefix]
        private static bool Prefix() { return !(ModConfig.Enabled.Value && ModConfig.GodModePlants.Value); }
    }

    [HarmonyPatch(typeof(Plant), "DecreaseHealth")]
    internal static class Patch_Plant_DecreaseHealth
    {
        [HarmonyPrefix]
        private static bool Prefix() { return !(ModConfig.Enabled.Value && ModConfig.GodModePlants.Value); }
    }

    // 僵尸受伤倍率（植物输出增强）
    [HarmonyPatch(typeof(Zombie), "TakeDamage",
        new Type[] { typeof(int), typeof(IDamageMaker), typeof(DamageType), typeof(PlantType), typeof(bool) })]
    internal static class Patch_Zombie_TakeDamage
    {
        [HarmonyPrefix]
        private static bool Prefix(ref int theDamage)
        {
            if (!ModConfig.Enabled.Value) return true;

            // 僵尸无敌：直接不扣血
            if (ModConfig.ZombieInvincible.Value) return false;

            if (ModConfig.OneHitZombies.Value)
            {
                theDamage = 1_000_000_000;
                return true;
            }
            double m = (double)ModConfig.PlantDamageMultiplier.Value * ModConfig.ZombieDamageTakenMultiplier.Value;
            if (m > 1.0001d && theDamage > 0)
            {
                double v = theDamage * m;
                theDamage = v > 1_000_000_000d ? 1_000_000_000 : (int)v;
            }
            return true;
        }
    }

    /// <summary>停止出怪：BoardSpawner.SummonZombies 是关卡放僵尸的入口，直接跳过它。</summary>
    [HarmonyPatch(typeof(BoardSpawner), "SummonZombies")]
    internal static class Patch_BoardSpawner_SummonZombies
    {
        [HarmonyPrefix]
        private static bool Prefix() { return !(ModConfig.Enabled.Value && ModConfig.StopZombieSpawn.Value); }
    }

    /// <summary>
    /// 手套/锤子/铁锹 无冷却。
    ///
    /// 注意：这里**故意不用 Harmony 打 InGameTool 的补丁**。
    /// 实测给 `InGameTool.UpdateCDTimer` 加 `InGameTool __instance` 参数会让
    /// Il2CppInterop 的 native->managed 蹦床抛 "Handle is not initialized"，
    /// 然后整个游戏 0xc0000005 崩掉（因为工具对象是游戏早期用 native 方式建出来的）。
    /// 改成在主循环里直接拿 InGameUI 上的三个工具对象把 CD 清零。
    /// </summary>
    internal static class Tools
    {
        // ---------------------------------------------------------------- 冷却总清单
        // 把游戏里**所有**和冷却有关的地方列全（2026-09-13 全量核对）：
        //
        //   类                          场地                      我原来的做法 / 问题
        //   CardUI                      CD, fullCD                ✅ 写 fullCD=0（写 CD 无效，游戏每帧重算）
        //     └ SpecialCard             重写 CDUpdate()
        //   InGameTool（基类）           fullCD, CD, coolSpeed      ⚠️ 原来漏了 coolSpeed
        //     ├ Glove  : InGameTool     重写 UpdateCDTimer()      ❌ 有 static Glove.Instance，原来用 GetComponent 找
        //     ├ Hammer : InGameTool     重写 UpdateCDTimer()      ❌ 同上（有 static Hammer.Instance）
        //     ├ Shovel : InGameTool     不重写（用基类）           ❌ 同上（有 static Shovel.Instance）
        //     └ Wheel  : InGameTool     不重写                     ❌ 完全没处理（没有 static Instance）
        //   Board                       freeCD (bool)             ❌ **原来完全没碰 —— 这才是游戏自己的"无CD模式"总开关**
        //   Lawnf.GetGloveCD()          static 返回 float         ✅ 已加后缀补丁返回 0
        //   IZManager.AICard            cd, fullcd                 — IZE 模式的 AI 出怪卡，与玩家无关
        //   LevelData.gloveCD           关卡配置里的手套 CD        — 只读配置，不用改
        //
        // 所以"手套无冷却没用"的原因是两条：
        //   1) 没设 Board.freeCD（游戏自己的总开关）
        //   2) 工具的 CD 字段是拿 InGameUI 上的 GameObject 去 GetComponent 找的，不一定找得到；
        //      应该直接用 Glove.Instance / Hammer.Instance / Shovel.Instance。

        private static bool _boardFreeCdSaved;
        private static bool _boardFreeCdOld;

        internal static void ZeroCooldowns()
        {
            try
            {
                bool on = ModConfig.Enabled.Value && ModConfig.NoToolCooldown.Value;

                // (1) 游戏自己的总开关：Board.freeCD
                Board b = Board.Instance;
                if (b != null)
                {
                    if (on)
                    {
                        if (!_boardFreeCdSaved) { _boardFreeCdOld = b.freeCD; _boardFreeCdSaved = true; }
                        if (!b.freeCD) b.freeCD = true;
                    }
                    else if (_boardFreeCdSaved)
                    {
                        if (b.freeCD != _boardFreeCdOld) b.freeCD = _boardFreeCdOld;
                        _boardFreeCdSaved = false;
                    }
                }

                if (!on) return;

                // (2) 三个工具有 static Instance，直接用，最稳
                Zero(Glove.Instance);
                Zero(Hammer.Instance);
                Zero(Shovel.Instance);

                // (3) 兜底：把场上所有 InGameTool 都扫一遍（覆盖 Wheel 这种没有 Instance 的）
                var arr = Actions.FindAll(typeof(InGameTool));
                if (arr != null)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var o = arr[i];
                        if (o == null) continue;
                        InGameTool t = null;
                        try { t = o.TryCast<InGameTool>(); } catch { }
                        Zero(t);
                    }
                }
            }
            catch { }
        }

        private static void Zero(InGameTool t)
        {
            if (t == null) return;
            try
            {
                t.CD = 0f;
                t.fullCD = 0f;
                t.avaliable = true;
                try { t.coolSpeed = 0f; } catch { }   // 让 CD 永远涨不上去
            }
            catch { }
        }

        /// <summary>诊断：打印冷却相关的真实数值（验证"无冷却"到底有没有写进去）</summary>
        internal static string Info()
        {
            var sb = new System.Text.StringBuilder(400);
            try
            {
                Board b = Board.Instance;
                sb.Append("Board.freeCD=").Append(b == null ? "无Board" : b.freeCD.ToString()).Append("  ");
            }
            catch { sb.Append("Board.freeCD=?  "); }
            try { sb.Append("GetGloveCD()=").Append(Lawnf.GetGloveCD().ToString("0.##")).Append("  "); }
            catch { sb.Append("GetGloveCD=?  "); }
            sb.Append('\n');
            Append(sb, "手套 Glove", Glove.Instance);
            Append(sb, "锤子 Hammer", Hammer.Instance);
            Append(sb, "铁锹 Shovel", Shovel.Instance);

            var arr = Actions.FindAll(typeof(InGameTool));
            int n = arr == null ? 0 : arr.Length;
            sb.Append("InGameTool 实例共 ").Append(n).Append(" 个");
            if (arr != null)
            {
                for (int i = 0; i < arr.Length && i < 6; i++)
                {
                    InGameTool t = null;
                    try { t = arr[i] == null ? null : arr[i].TryCast<InGameTool>(); } catch { }
                    if (t == null) continue;
                    sb.Append("\n  ").Append(t.GetType().Name).Append(": ");
                    Detail(sb, t);
                }
            }
            string r = sb.ToString();
            Plugin.Log.LogInfo("[工具] " + r.Replace("\n", " | "));
            return r;
        }

        private static void Append(System.Text.StringBuilder sb, string label, InGameTool t)
        {
            sb.Append(label).Append(": ");
            if (t == null) { sb.Append("(没有实例)\n"); return; }
            Detail(sb, t);
            sb.Append('\n');
        }

        private static void Detail(System.Text.StringBuilder sb, InGameTool t)
        {
            try
            {
                sb.Append("CD=").Append(t.CD.ToString("0.##"))
                  .Append(" fullCD=").Append(t.fullCD.ToString("0.##"))
                  .Append(" 可用=").Append(t.avaliable);
                try { sb.Append(" coolSpeed=").Append(t.coolSpeed.ToString("0.##")); } catch { }
            }
            catch (Exception e) { sb.Append("读取失败 ").Append(e.GetType().Name); }
        }
    }

    [HarmonyPatch(typeof(Lawnf), "GetGloveCD")]
    internal static class Patch_Lawnf_GetGloveCD
    {
        [HarmonyPostfix]
        private static void Postfix(ref float __result)
        {
            if (ModConfig.Enabled.Value && ModConfig.NoToolCooldown.Value) __result = 0f;
        }
    }

    /// <summary>
    /// 一种种一排（一列/一行铺满）。
    ///
    /// 游戏里**没有**现成的"种一排"函数（只找到 `Plant.InRow(int)`，那是个查询），
    /// 所以这里挂在最终的种植入口 `CreatePlant.SetPlant` 上：
    /// 玩家种下一株之后，把**同一行其它空格**也放上同一种植物。
    ///
    /// 三个注意点：
    ///  1. 只用参数 + `__result`，**不注入 `__instance`**（那个在这套构建里崩过）。
    ///  2. 用 `PlantDb.Internal` 区分"玩家种的"和"插件自己放的"，
    ///     否则融合/沙盒/批量变身每放一株都会把整行铺满。
    ///  3. 只在空格子上放，不覆盖已有植物；放不下去（水里/花盆限制）就跳过。
    /// </summary>
    [HarmonyPatch(typeof(CreatePlant), "SetPlant")]
    internal static class Patch_CreatePlant_SetPlant
    {
        [HarmonyPostfix]
        private static void Postfix(int newColumn, int newRow, PlantType theSeedType, Plant __result)
        {
            try
            {
                if (!ModConfig.Enabled.Value || !ModConfig.PlantWholeLine.Value) return;
                if (PlantDb.Internal) return;          // 插件自己放的，不铺
                if (__result == null) return;
                Board b = Board.Instance;
                if (b == null) return;
                if (newRow < 0 || newRow > 6) return;

                // 用"实际种出来的类型"而不是传入的类型：融合过的就在整条也放融合体
                int type = (int)theSeedType;
                try { type = (int)__result.thePlantType; } catch { }

                // 方向：col = 一列（竖着；游戏里那个"一种种一列"词条就是这个，也是默认）
                //       row = 一排（横着，整条车道）
                bool vertical = true;
                try { vertical = ModConfig.PlantLineDir.Value != "row"; } catch { }

                int n = 0;
                if (vertical)
                {
                    for (int row = 0; row < 7; row++)
                    {
                        if (row == newRow) continue;
                        Plant there = PlantDb.CellPlant(newColumn, row);
                        if (there != null) continue;   // 已经有植物了，不动
                        Plant made;
                        if (PlantDb.SpawnAt(newColumn, row, type, new Vector2(0f, 0f), out made) == null) n++;
                    }
                    if (n > 0) Plugin.Log.LogInfo("[一种种一列] 第 " + (newColumn + 1) + " 列又补了 " + n + " 株 " + PlantDb.CnName(type));
                }
                else
                {
                    for (int col = 0; col < 9; col++)
                    {
                        if (col == newColumn) continue;
                        Plant there = PlantDb.CellPlant(col, newRow);
                        if (there != null) continue;
                        Plant made;
                        if (PlantDb.SpawnAt(col, newRow, type, new Vector2(0f, 0f), out made) == null) n++;
                    }
                    if (n > 0) Plugin.Log.LogInfo("[一种种一排] 第 " + (newRow + 1) + " 行又补了 " + n + " 株 " + PlantDb.CnName(type));
                }
            }
            catch (Exception e) { Plugin.LogOnce("一种种一列", e); }
        }
    }

    // 词条随机池过滤
    [HarmonyPatch(typeof(TravelMgr), "GetAdvancedBuffPool")]
    internal static class Patch_Travel_AdvBuffPool
    {
        [HarmonyPostfix]
        private static void Postfix(ref Il2CppSystem.Collections.Generic.List<AdvBuff> __result)
        {
            if (!ModConfig.Enabled.Value) return;
            Actions.FilterBuffPool(ref __result);
        }
    }

    [HarmonyPatch(typeof(TravelMgr), "GetUltiBuffPool")]
    internal static class Patch_Travel_UltiBuffPool
    {
        [HarmonyPostfix]
        private static void Postfix(ref Il2CppSystem.Collections.Generic.List<UltiBuff> __result)
        {
            if (!ModConfig.Enabled.Value) return;
            Actions.FilterUltiPool(ref __result);
        }
    }

    // 天赋：初始化完成时全部解锁
    [HarmonyPatch(typeof(AdvantureData), "OnInit")]
    internal static class Patch_Advanture_OnInit
    {
        [HarmonyPostfix]
        private static void Postfix(AdvantureData __instance) { Actions.UnlockTalents(__instance); }
    }

    // 子弹生成后应用子弹覆盖（改子弹类型/伤害/速度/穿透）
    [HarmonyPatch(typeof(Bullet), "InitData")]
    internal static class Patch_Bullet_InitData
    {
        [HarmonyPostfix]
        private static void Postfix(Bullet __instance)
        {
            if (!ModConfig.Enabled.Value) return;
            Overrides.ApplyBullet(__instance);
        }
    }

    // 深渊：拿到券时直接拉满
    [HarmonyPatch(typeof(GameLevel.Abyss.AbyssData), "GetTicket")]
    internal static class Patch_Abyss_GetTicket
    {
        [HarmonyPostfix]
        private static void Postfix(GameLevel.Abyss.AbyssData __instance)
        {
            if (ModConfig.Enabled.Value && ModConfig.AbyssMaxTickets.Value) Actions.MaxTickets(__instance);
        }
    }

    // 深渊：券不消耗
    [HarmonyPatch(typeof(GameLevel.Abyss.AbyssData), "UseTicket")]
    internal static class Patch_Abyss_UseTicket
    {
        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (ModConfig.Enabled.Value && ModConfig.AbyssInfiniteTickets.Value)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
