using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace PvzRhCheat
{
    /// <summary>全部可调项，生成在 BepInEx\config\com.dsh.pvzrh.cheat.cfg</summary>
    public static class ModConfig
    {
        // 供外置 UI 读写用的注册表
        private static readonly Dictionary<string, ConfigEntryBase> Registry =
            new Dictionary<string, ConfigEntryBase>(StringComparer.Ordinal);

        public static IEnumerable<KeyValuePair<string, ConfigEntryBase>> All() { return Registry; }

        public static ConfigEntryBase ByKey(string key)
        {
            return Registry.TryGetValue(key, out var e) ? e : null;
        }

        public static string GetString(ConfigEntryBase e)
        {
            object v = e.BoxedValue;
            if (v is float f) return f.ToString("0.###", CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static void SetFromString(ConfigEntryBase e, string s)
        {
            Type t = e.SettingType;
            try
            {
                if (t == typeof(bool)) e.BoxedValue = (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase));
                else if (t == typeof(int)) e.BoxedValue = int.Parse(s, CultureInfo.InvariantCulture);
                else if (t == typeof(long)) e.BoxedValue = long.Parse(s, CultureInfo.InvariantCulture);
                else if (t == typeof(float)) e.BoxedValue = float.Parse(s, CultureInfo.InvariantCulture);
                else if (t == typeof(string)) e.BoxedValue = s;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[IPC] 配置赋值失败 " + e.Definition.Key + " = " + s + ": " + ex.Message); }
        }

        private static ConfigEntry<T> Reg<T>(ConfigEntry<T> e, string key)
        {
            Registry[key] = e;
            return e;
        }

        // ---- 通用 ----
        public static ConfigEntry<bool>   Enabled;
        public static ConfigEntry<float>  ApplyInterval;

        // ---- 解锁 / 资源 ----
        public static ConfigEntry<bool>   UnlockAllLevels;
        public static ConfigEntry<long>   Money;
        public static ConfigEntry<bool>   DeveloperMode;

        // ---- 关卡内阳光 / 金币 ----
        public static ConfigEntry<bool>   InfiniteSun;
        public static ConfigEntry<int>    SunFloor;

        // ---- 战斗 ----
        public static ConfigEntry<bool>   GodModePlants;
        public static ConfigEntry<float>  PlantDamageMultiplier;
        public static ConfigEntry<float>  ZombieDamageTakenMultiplier;
        public static ConfigEntry<bool>   OneHitZombies;

        // ---- 旅行 / 词条（Roguelike） ----
        public static ConfigEntry<bool>   TravelBuffs;
        public static ConfigEntry<float>  TravelDamageReduction;
        public static ConfigEntry<float>  TravelLuckyStrike;
        public static ConfigEntry<float>  TravelDamageAmplification;
        public static ConfigEntry<bool>   TravelPlantZeroHealth;

        /// <summary>词条白名单：逗号分隔的 AdvBuff 名称；留空 = 不干预随机池</summary>
        public static ConfigEntry<string> BuffWhitelist;
        /// <summary>终极词条白名单：逗号分隔的 UltiBuff 名称；留空 = 不干预</summary>
        public static ConfigEntry<string> UltiBuffWhitelist;

        // ---- 天赋（冒险模式天赋树） ----
        public static ConfigEntry<bool>   TalentUnlockAll;
        public static ConfigEntry<int>    TalentStars;
        public static ConfigEntry<bool>   TalentFreeHardModeOff;

        // ---- 深渊抽奖券 ----
        public static ConfigEntry<bool>   AbyssMaxTickets;
        public static ConfigEntry<bool>   AbyssInfiniteTickets;

        // ---- ESP 外观 ----
        public static ConfigEntry<int>    EspFontSize;
        public static ConfigEntry<bool>   EspBold;
        public static ConfigEntry<bool>   AutoLaunchUi;
        /// <summary>游戏内菜单字号</summary>
        public static ConfigEntry<int>    MenuFontSize;
        /// <summary>僵尸 ESP</summary>
        public static ConfigEntry<bool>   ZombieEsp;
        public static ConfigEntry<bool>   ZombieEspName;
        public static ConfigEntry<bool>   ZombieEspHp;
        public static ConfigEntry<float>  ZombieEspHeight;

        // ---- 经典作弊 ----
        public static ConfigEntry<bool>   AutoCollectSun;     // TreasureData.autoCollect
        public static ConfigEntry<bool>   NoCardCooldown;     // CardUI.CD = 0
        public static ConfigEntry<bool>   FreePlanting;       // CardUI.theSeedCost = 0
        public static ConfigEntry<bool>   UnlimitedCardUse;   // CardUI.maxUsedTimes = 9999
        public static ConfigEntry<bool>   FreezeAllZombies;   // 持续冻结全场
        public static ConfigEntry<bool>   ZombiesStopMoving;  // 僵尸速度归零
        public static ConfigEntry<bool>   AutoKillZombies;    // 自动秒杀新出现的僵尸
        /// <summary>一种种一列/一排：手动种一株就把同一条线（一列或一排）其它空格也种满</summary>
        public static ConfigEntry<bool>   PlantWholeLine;
        /// <summary>铺满方向："col" = 一列（竖着，默认），"row" = 一排（横着，整条车道）</summary>
        public static ConfigEntry<string> PlantLineDir;

        // ---- 对齐 Modified-Plus 的额外功能 ----
        public static ConfigEntry<float>  GameSpeed;          // Time.timeScale
        public static ConfigEntry<bool>   StopZombieSpawn;    // 停止出怪（BoardSpawner.SummonZombies）
        public static ConfigEntry<bool>   ZombieInvincible;   // 僵尸无敌
        public static ConfigEntry<float>  ZombieHpMultiplier; // 僵尸血量倍率
        public static ConfigEntry<bool>   NoToolCooldown;     // 手套/锤子无冷却
        public static ConfigEntry<bool>   UnlockAllPlants;    // 植物图鉴全解锁

        // ---- 热键 ----
        public static ConfigEntry<string> MenuKey;            // 显示/隐藏菜单，默认 F1
        public static ConfigEntry<string> EspKey;             // 开关 ESP 方框，默认 F3

        /// <summary>配置结构版本：低于当前值时把"作弊项"强制拉回全关（见 Init 里的迁移）</summary>
        public static ConfigEntry<int>    ConfigVersion;
        public const int CurrentConfigVersion = 2;

        public static void Init(ConfigFile cfg)
        {
            Enabled  = Reg(cfg.Bind("0-General", "Enabled", true, "Master switch"), "Enabled");
            ApplyInterval = Reg(cfg.Bind("0-General", "ApplyIntervalSeconds", 2.0f,
                "How often to re-apply (seconds). 0 = only once at load"), "ApplyIntervalSeconds");

            UnlockAllLevels = Reg(cfg.Bind("1-Unlock", "UnlockAllLevels", false,
                "Unlock every level (adventure/challenge/survival/minigame/explore/skin)"), "UnlockAllLevels");
            Money           = Reg(cfg.Bind("1-Unlock", "Money", 0L, "Money amount"), "Money");
            DeveloperMode   = Reg(cfg.Bind("1-Unlock", "DeveloperMode", false, "Developer mode"), "DeveloperMode");

            InfiniteSun = Reg(cfg.Bind("2-Board", "InfiniteSun", false, "Sun is never consumed"), "InfiniteSun");
            SunFloor    = Reg(cfg.Bind("2-Board", "SunFloor", 9999, "Keep sun at least this high"), "SunFloor");

            GodModePlants   = Reg(cfg.Bind("3-Combat", "GodModePlants", false, "Plants take no damage"), "GodModePlants");
            PlantDamageMultiplier = Reg(cfg.Bind("3-Combat", "PlantDamageMultiplier", 1.0f,
                "Plant outgoing damage multiplier (x). 1 = unchanged"), "PlantDamageMultiplier");
            ZombieDamageTakenMultiplier = Reg(cfg.Bind("3-Combat", "ZombieDamageTakenMultiplier", 1.0f,
                "Zombie incoming damage multiplier (x), stacks with the above"), "ZombieDamageTakenMultiplier");
            OneHitZombies   = Reg(cfg.Bind("3-Combat", "OneHitZombies", false, "One-hit kill zombies"), "OneHitZombies");

            TravelBuffs = Reg(cfg.Bind("4-Travel", "TravelBuffs", false, "Enable travel mode buffs"), "TravelBuffs");
            TravelDamageReduction  = Reg(cfg.Bind("4-Travel", "DamageReduction", 0f, "Damage reduction 0..1"), "DamageReduction");
            TravelLuckyStrike      = Reg(cfg.Bind("4-Travel", "LuckyStrike", 1.0f, "Lucky strike rate"), "LuckyStrike");
            TravelDamageAmplification = Reg(cfg.Bind("4-Travel", "DamageAmplification", 1.0f, "Damage amplification (x)"), "DamageAmplification");
            TravelPlantZeroHealth  = Reg(cfg.Bind("4-Travel", "PlantZeroHealth", false, "Built-in plantZeroHealth flag"), "PlantZeroHealth");

            BuffWhitelist = Reg(cfg.Bind("4-Travel", "BuffWhitelist", "",
                "Buff whitelist: only these AdvBuff names can be rolled (csv). Empty = no change.\n" +
                "Example: 撒豆成兵,百步穿杨,妙手回春\n" +
                "Full list: _mod\\dump\\dump.cs -> enum AdvBuff"), "BuffWhitelist");
            UltiBuffWhitelist = Reg(cfg.Bind("4-Travel", "UltiBuffWhitelist", "",
                "Ultimate buff whitelist (UltiBuff names, csv). Empty = no change"), "UltiBuffWhitelist");

            TalentUnlockAll = Reg(cfg.Bind("5-Talent", "TalentUnlockAll", false, "Unlock all adventure talents"), "TalentUnlockAll");
            TalentStars     = Reg(cfg.Bind("5-Talent", "TalentStars", 0, "Talent star count"), "TalentStars");
            TalentFreeHardModeOff = Reg(cfg.Bind("5-Talent", "DisableHardMode", false, "Disable hard mode and zero the difficulty"), "DisableHardMode");

            AbyssMaxTickets       = Reg(cfg.Bind("6-Abyss", "AbyssMaxTickets", false, "Max out abyss lottery tickets"), "AbyssMaxTickets");
            AbyssInfiniteTickets  = Reg(cfg.Bind("6-Abyss", "AbyssInfiniteTickets", false, "Abyss tickets are never consumed"), "AbyssInfiniteTickets");

            EspFontSize = Reg(cfg.Bind("7-ESP", "FontSize", 18, "ESP label font size (bigger = easier to read)"), "EspFontSize");
            EspBold     = Reg(cfg.Bind("7-ESP", "Bold", true, "ESP label bold"), "EspBold");
            MenuFontSize = Reg(cfg.Bind("7-ESP", "MenuFontSize", 14, "In-game menu font size (10-26)"), "MenuFontSize");
            ZombieEsp       = Reg(cfg.Bind("7-ESP", "ZombieEsp", true, "Show ESP boxes above zombies"), "ZombieEsp");
            ZombieEspName   = Reg(cfg.Bind("7-ESP", "ZombieEspName", true, "Show zombie name in the box"), "ZombieEspName");
            ZombieEspHp     = Reg(cfg.Bind("7-ESP", "ZombieEspHp", true, "Show zombie HP in the box"), "ZombieEspHp");
            ZombieEspHeight = Reg(cfg.Bind("7-ESP", "ZombieEspHeight", 1.1f, "Zombie ESP box height above the sprite"), "ZombieEspHeight");
            // 内置菜单已经覆盖全部功能，外置窗口默认不再自动弹出
            AutoLaunchUi = Reg(cfg.Bind("7-ESP", "AutoLaunchUi", false, "Launch the standalone WinForms window at startup (not needed: the in-game menu has everything)"), "AutoLaunchUi");

            AutoCollectSun    = Reg(cfg.Bind("8-Classic", "AutoCollectSun", false, "Auto collect sun and coins"), "AutoCollectSun");
            NoCardCooldown    = Reg(cfg.Bind("8-Classic", "NoCardCooldown", false, "Seed cards never go on cooldown"), "NoCardCooldown");
            FreePlanting      = Reg(cfg.Bind("8-Classic", "FreePlanting", false, "Seed cards cost 0 sun"), "FreePlanting");
            UnlimitedCardUse  = Reg(cfg.Bind("8-Classic", "UnlimitedCardUse", false, "Seed cards have unlimited uses"), "UnlimitedCardUse");
            FreezeAllZombies  = Reg(cfg.Bind("8-Classic", "FreezeAllZombies", false, "Keep all zombies frozen"), "FreezeAllZombies");
            ZombiesStopMoving = Reg(cfg.Bind("8-Classic", "ZombiesStopMoving", false, "Zombie speed set to zero"), "ZombiesStopMoving");
            AutoKillZombies   = Reg(cfg.Bind("8-Classic", "AutoKillZombies", false, "Automatically kill zombies as they appear"), "AutoKillZombies");
            PlantWholeLine    = Reg(cfg.Bind("8-Classic", "PlantWholeLine", false,
                "Plant one seed and the whole line (column by default) gets filled with the same plant"), "PlantWholeLine");
            PlantLineDir      = Reg(cfg.Bind("8-Classic", "PlantLineDir", "col",
                "Fill direction: col = vertical column (default), row = horizontal lane"), "PlantLineDir");

            // ---- 对齐 Modified-Plus 的额外功能（默认同样全关）----
            GameSpeed        = Reg(cfg.Bind("9-Extra", "GameSpeed", 1.0f,
                "Game speed (Time.timeScale). 1 = normal, 0.05-20"), "GameSpeed");
            StopZombieSpawn  = Reg(cfg.Bind("9-Extra", "StopZombieSpawn", false,
                "Stop new zombies from spawning (skips BoardSpawner.SummonZombies)"), "StopZombieSpawn");
            ZombieInvincible = Reg(cfg.Bind("9-Extra", "ZombieInvincible", false,
                "Zombies take no damage"), "ZombieInvincible");
            ZombieHpMultiplier = Reg(cfg.Bind("9-Extra", "ZombieHpMultiplier", 1.0f,
                "Zombie max HP multiplier. 1 = unchanged"), "ZombieHpMultiplier");
            NoToolCooldown   = Reg(cfg.Bind("9-Extra", "NoToolCooldown", false,
                "Glove and hammer have no cooldown"), "NoToolCooldown");
            UnlockAllPlants  = Reg(cfg.Bind("9-Extra", "UnlockAllPlants", false,
                "Unlock every plant in the almanac / plant pool"), "UnlockAllPlants");

            // ---- 热键 ----
            MenuKey = Reg(cfg.Bind("A-Hotkey", "MenuKey", "F1",
                "Key that shows/hides the in-game menu (Unity KeyCode name, e.g. F1, Insert, F8, BackQuote)"), "MenuKey");
            EspKey  = Reg(cfg.Bind("A-Hotkey", "EspKey", "F3",
                "Key that toggles the plant ESP boxes"), "EspKey");

            ConfigVersion = Reg(cfg.Bind("A-Hotkey", "ConfigVersion", 0,
                "Internal: config layout version, used for one-time resets"), "ConfigVersion");

            // ---- 一次性迁移：以前被写脏的配置强制拉回"全关" ----
            // （之前调试时用 IPC 写过一堆 =1，用户看到"默认还开着一些作弊"就是这个原因）
            if (ConfigVersion.Value < CurrentConfigVersion)
            {
                int changed = ForceAllOff();
                ConfigVersion.Value = CurrentConfigVersion;
                try { cfg.Save(); } catch { }
                Log("配置迁移到 v" + CurrentConfigVersion + "：已把 " + changed + " 个作弊项强制关闭");
            }
        }

        private static void Log(string s) { try { Plugin.Log?.LogInfo("[配置] " + s); } catch { } }

        /// <summary>所有作弊项的"关闭/中性"值</summary>
        private static readonly string[][] OffValues =
        {
            new[]{"UnlockAllLevels","0"}, new[]{"Money","0"}, new[]{"DeveloperMode","0"},
            new[]{"InfiniteSun","0"}, new[]{"SunFloor","9999"},
            new[]{"GodModePlants","0"}, new[]{"PlantDamageMultiplier","1"},
            new[]{"ZombieDamageTakenMultiplier","1"}, new[]{"OneHitZombies","0"},
            new[]{"TravelBuffs","0"}, new[]{"DamageReduction","0"}, new[]{"LuckyStrike","1"},
            new[]{"DamageAmplification","1"}, new[]{"PlantZeroHealth","0"},
            new[]{"BuffWhitelist",""}, new[]{"UltiBuffWhitelist",""},
            new[]{"TalentUnlockAll","0"}, new[]{"TalentStars","0"}, new[]{"DisableHardMode","0"},
            new[]{"AbyssMaxTickets","0"}, new[]{"AbyssInfiniteTickets","0"},
            new[]{"AutoCollectSun","0"}, new[]{"NoCardCooldown","0"}, new[]{"FreePlanting","0"},
            new[]{"UnlimitedCardUse","0"}, new[]{"FreezeAllZombies","0"},
            new[]{"ZombiesStopMoving","0"}, new[]{"AutoKillZombies","0"}, new[]{"PlantWholeLine","0"}, new[]{"PlantLineDir","col"},
            new[]{"GameSpeed","1"}, new[]{"StopZombieSpawn","0"}, new[]{"ZombieInvincible","0"},
            new[]{"ZombieHpMultiplier","1"}, new[]{"NoToolCooldown","0"}, new[]{"UnlockAllPlants","0"},
        };

        /// <summary>
        /// 把所有作弊项恢复到"关闭/中性"。返回实际改动的项数。
        /// 注意：总开关 Enabled、字号、热键这些**不动**。
        /// </summary>
        public static int ForceAllOff()
        {
            int n = 0;
            foreach (string[] kv in OffValues)
            {
                ConfigEntryBase e = ByKey(kv[0]);
                if (e == null) continue;
                // 必须把 bool 的 "True"/"False" 与 "1"/"0" 视为等价，
                // 否则全新配置也会被算成"改了 22 项"（日志会骗人）。
                if (SameValue(GetString(e), kv[1])) continue;
                SetFromString(e, kv[1]);
                n++;
            }
            return n;
        }

        private static bool SameValue(string cur, string want)
        {
            if (cur == null) cur = "";
            if (cur.Equals(want, StringComparison.OrdinalIgnoreCase)) return true;
            bool curTrue = cur == "True" || cur == "1";
            bool curFalse = cur == "False" || cur == "0" || cur.Length == 0;
            if (want == "1") return curTrue;
            if (want == "0") return curFalse;
            double a, b;
            if (double.TryParse(cur, NumberStyles.Float, CultureInfo.InvariantCulture, out a) &&
                double.TryParse(want, NumberStyles.Float, CultureInfo.InvariantCulture, out b))
                return Math.Abs(a - b) < 0.0001d;
            return false;
        }

        // ---------------------------------------------------------------- 热键
        public static KeyCode MenuKeyCode() { return ParseKey(MenuKey == null ? null : MenuKey.Value, KeyCode.F1); }
        public static KeyCode EspKeyCode() { return ParseKey(EspKey == null ? null : EspKey.Value, KeyCode.F3); }

        private static KeyCode ParseKey(string s, KeyCode dflt)
        {
            if (string.IsNullOrEmpty(s)) return dflt;
            try
            {
                if (Enum.IsDefined(typeof(KeyCode), s)) return (KeyCode)Enum.Parse(typeof(KeyCode), s, false);
            }
            catch { }
            return dflt;
        }

        /// <summary>把 KeyCode 存成可读名字</summary>
        public static void SetKey(ConfigEntry<string> entry, KeyCode k)
        {
            if (entry == null || k == KeyCode.None) return;
            entry.Value = k.ToString();
        }
    }
}
