using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;

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
        }
    }
}
