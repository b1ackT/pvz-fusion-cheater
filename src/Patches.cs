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
            RefreshLevelInfo();

            // 界面上点的"直接融合"在这里执行：不在 IMGUI 事件里动场景对象
            try { RunPendingFuse(); } catch (Exception e) { LogSlow("FastTick/融合", e); }
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
            Step(TopUpSun,            nameof(TopUpSun));
            Step(RestorePlants,       nameof(RestorePlants));
            Step(ApplyPlantOverrides, nameof(ApplyPlantOverrides));
            Step(TravelTweaks,        nameof(TravelTweaks));
            Step(ClassicCheats,       nameof(ClassicCheats));

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
        /// <summary>周期性的经典作弊（自动收集 / 免费种植 / 无冷却 / 冻结等）</summary>
        internal static void ClassicCheats()
        {
            try { if (ModConfig.AutoCollectSun.Value) TreasureData.autoCollect = true; }
            catch (Exception e) { LogSlow("cheat/autoCollect", e); }

            if (ModConfig.NoCardCooldown.Value || ModConfig.FreePlanting.Value || ModConfig.UnlimitedCardUse.Value)
                TweakCards();

            if (ModConfig.FreezeAllZombies.Value) ForEachZombie(FreezeOne);
            if (ModConfig.ZombiesStopMoving.Value) ForEachZombie(StopOne);
            if (ModConfig.AutoKillZombies.Value) ForEachZombie(KillOne);
        }

        /// <summary>用非泛型 FindObjectsOfType，避免泛型实例化被裁剪时静默返回空</summary>
        private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Object>
            FindAll(Type t)
        {
            try { return UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.From(t)); }
            catch (Exception e) { LogSlow("FindAll/" + t.Name, e); return null; }
        }

        private static void ForEachZombie(Action<Zombie> fn)
        {
            var arr = FindAll(typeof(Zombie));
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
            {
                var o = arr[i];
                if (o == null) continue;
                Zombie z = null;
                try { z = o.TryCast<Zombie>(); } catch { }
                if (z == null) continue;
                try { fn(z); } catch { }
            }
        }

        private static void FreezeOne(Zombie z) { try { z.SetFreeze(30f, 3); } catch { } }
        private static void StopOne(Zombie z) { try { z.theSpeed = 0f; } catch { } }
        private static void KillOne(Zombie z) { try { z.Die(0); } catch { } }

        private static void TweakCards()
        {
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
                    if (ModConfig.NoCardCooldown.Value) c.CD = 0f;
                    if (ModConfig.FreePlanting.Value) c.theSeedCost = 0;
                    if (ModConfig.UnlimitedCardUse.Value) c.maxUsedTimes = 9999;
                }
                catch { }
            }
        }

        // ---- 一次性动作（由 UI 按钮 / IPC 触发）----
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
        private static void Prefix(ref int theDamage)
        {
            if (!ModConfig.Enabled.Value) return;

            if (ModConfig.OneHitZombies.Value)
            {
                theDamage = 1_000_000_000;
                return;
            }
            double m = (double)ModConfig.PlantDamageMultiplier.Value * ModConfig.ZombieDamageTakenMultiplier.Value;
            if (m > 1.0001d && theDamage > 0)
            {
                double v = theDamage * m;
                theDamage = v > 1_000_000_000d ? 1_000_000_000 : (int)v;
            }
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
