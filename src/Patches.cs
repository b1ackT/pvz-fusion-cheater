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
        /// 植物来源优先级：
        ///  1) Board.Instance.boardEntity.plantArray  ← 游戏自己的权威列表，覆盖所有关卡类型
        ///  2) boardEntity.hiddenPlants
        ///  3) 非泛型 FindObjectsOfType(Il2CppType) 兜底（泛型版在 IL2CPP 下可能静默返回空）
        /// </summary>
        internal static void RefreshPlants()
        {
            _plants.Clear();
            _seen.Clear();
            _gardenCount = 0;

            bool fromBoard = false;
            try
            {
                Board b = Board.Instance;
                if (b != null)
                {
                    BoardEntity be = b.boardEntity;
                    if (be != null)
                    {
                        AddPlants(be.plantArray);
                        AddPlants(be.hiddenPlants);
                        try
                        {
                            var gp = be.gardenPlants;
                            if (gp != null) _gardenCount = gp.Count;
                        }
                        catch { }
                        fromBoard = _plants.Count > 0;
                    }
                }
            }
            catch (Exception e) { LogSlow("plants/boardEntity", e); }

            if (_plants.Count == 0)
            {
                // 兜底：非泛型调用（不依赖泛型实例化是否被裁剪）
                try
                {
                    var arr = UnityEngine.Object.FindObjectsOfType(
                        Il2CppInterop.Runtime.Il2CppType.Of<Plant>());
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
                    if (_plants.Count > 0) fromBoard = false;
                }
                catch (Exception e) { LogSlow("plants/fallbackNonGeneric", e); }
            }

            if (_plants.Count == 0)
            {
                try
                {
                    var arr = UnityEngine.Object.FindObjectsOfType<Plant>();
                    if (arr != null)
                        for (int i = 0; i < arr.Length; i++) AddOne(arr[i]);
                }
                catch (Exception e) { LogSlow("plants/fallbackGeneric", e); }
            }

            _plantSource = fromBoard ? "board" : (_plants.Count > 0 ? "find" : "none");

            if (!_plantsLogged && _plants.Count > 0)
            {
                _plantsLogged = true;
                Plugin.Log.LogInfo("[植物] 来源=" + _plantSource + " 数量=" + _plants.Count +
                                   " 花园植物=" + _gardenCount + " 关卡=" + _levelInfo);
            }
        }

        private static bool _plantsLogged;
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

        // ------------------------------------------------------------------ 植物覆盖（UI 编辑器写入）
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
