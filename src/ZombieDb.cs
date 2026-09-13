using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PvzRhCheat
{
    /// <summary>
    /// 僵尸的数据层：中文名、数值读写、行为开关、变身。
    /// 和 PlantDb 一个套路 —— 界面里显示的是僵尸**当前的真实数值**，
    /// 用户改多少就写多少（未改过的项内部是 -1，表示不干预）。
    /// </summary>
    internal static class ZombieDb
    {
        // ---------------------------------------------------------------- 中文名
        private static readonly Dictionary<int, string> _cn = new Dictionary<int, string>(900);

        internal static string CnName(int typeId)
        {
            if (typeId < 0) return "";
            string s;
            if (_cn.TryGetValue(typeId, out s)) return s;
            try { s = Lawnf.GetName((ZombieType)typeId) ?? ""; }
            catch { s = ""; }
            if (!string.IsNullOrEmpty(s)) _cn[typeId] = s;   // 字符串表没加载好时先不缓存
            return s;
        }

        internal static string Label(Zombie z)
        {
            if (z == null) return "?";
            int t = -1; try { t = (int)z.theZombieType; } catch { }
            string cn = CnName(t);
            if (string.IsNullOrEmpty(cn)) cn = "未知僵尸";
            return cn + " #" + t;
        }

        /// <summary>类型表（复用 PlantDb 里已经建好的僵尸枚举表）</summary>
        internal static int TypeCount { get { return PlantDb.ZombieTypeCount; } }
        internal static int TypeIdAt(int i) { return PlantDb.ZombieTypeIdAt(i); }
        internal static string TypeNameAt(int i) { return PlantDb.ZombieTypeNameAt(i); }
        internal static List<int> FilterTypes(string filter) { return PlantDb.FilterTypes(filter, true); }

        // ---------------------------------------------------------------- 数值字段
        internal const int KInt = 0, KFloat = 1, KLong = 2;

        internal struct FieldDef
        {
            public string Key, Name, Unit;
            public int Kind;
        }

        internal static readonly FieldDef[] Fields =
        {
            new FieldDef { Key="Health",       Name="当前血量",   Unit="",    Kind=KLong  },
            new FieldDef { Key="MaxHealth",    Name="血量上限",   Unit="",    Kind=KLong  },
            new FieldDef { Key="Speed",        Name="移动速度",   Unit="",    Kind=KFloat },
            new FieldDef { Key="OriginSpeed",  Name="原始速度",   Unit="",    Kind=KFloat },
            new FieldDef { Key="AttackDamage", Name="攻击力",     Unit="",    Kind=KInt   },
            new FieldDef { Key="Level",        Name="等级",       Unit="",    Kind=KInt   },
            new FieldDef { Key="Row",          Name="所在行",     Unit="0-6", Kind=KInt   },
            new FieldDef { Key="Armor1",       Name="护甲一当前", Unit="",    Kind=KInt   },
            new FieldDef { Key="Armor1Max",    Name="护甲一上限", Unit="",    Kind=KInt   },
            new FieldDef { Key="Armor2",       Name="护甲二当前", Unit="",    Kind=KInt   },
            new FieldDef { Key="ArmorValue",   Name="护甲值",     Unit="",    Kind=KFloat },
            new FieldDef { Key="TakeDmgMult",  Name="受伤倍率",   Unit="x",   Kind=KFloat },
            new FieldDef { Key="FreezeLevel",  Name="冻结等级",   Unit="",    Kind=KInt   },
            new FieldDef { Key="PoisonLevel",  Name="中毒等级",   Unit="",    Kind=KInt   },
        };

        internal static int FieldCount { get { return Fields.Length; } }
        internal static string FieldName(int i) { return (i >= 0 && i < Fields.Length) ? Fields[i].Name : "?"; }
        internal static string FieldUnit(int i) { return (i >= 0 && i < Fields.Length) ? Fields[i].Unit : ""; }
        internal static int FieldKind(int i) { return (i >= 0 && i < Fields.Length) ? Fields[i].Kind : KInt; }

        /// <summary>读僵尸当前真实数值</summary>
        internal static string GetLive(Zombie z, int fi)
        {
            if (z == null || fi < 0 || fi >= Fields.Length) return "";
            try
            {
                switch (Fields[fi].Key)
                {
                    case "Health":       return z.theHealth.ToString(CultureInfo.InvariantCulture);
                    case "MaxHealth":    return z.theMaxHealth.ToString(CultureInfo.InvariantCulture);
                    case "Speed":        return N(z.theSpeed);
                    case "OriginSpeed":  return N(z.theOriginSpeed);
                    case "AttackDamage": return z.theAttackDamage.ToString(CultureInfo.InvariantCulture);
                    case "Level":        return z.level.ToString(CultureInfo.InvariantCulture);
                    case "Row":          return z.theZombieRow.ToString(CultureInfo.InvariantCulture);
                    case "Armor1":       return z.theFirstArmorHealth.ToString(CultureInfo.InvariantCulture);
                    case "Armor1Max":    return z.theFirstArmorMaxHealth.ToString(CultureInfo.InvariantCulture);
                    case "Armor2":       return z.theSecondArmorHealth.ToString(CultureInfo.InvariantCulture);
                    case "ArmorValue":   return N(z.theArmor);
                    case "TakeDmgMult":  return N(z.takeDmgMultiplier);
                    case "FreezeLevel":  return z.freezeLevel.ToString(CultureInfo.InvariantCulture);
                    case "PoisonLevel":  return z.poisonLevel.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch (Exception e) { Plugin.LogOnce("读僵尸字段/" + Fields[fi].Key, e); }
            return "";
        }

        /// <summary>把用户输入的绝对值写进该僵尸的覆盖项</summary>
        internal static string SetValue(Zombie z, int fi, string text)
        {
            if (z == null || fi < 0 || fi >= Fields.Length) return "没有选中僵尸";
            string key = Fields[fi].Key;
            text = (text ?? "").Trim();
            if (text.Length == 0) return "请输入数值";

            ZombieOverride o = ZombieOverrides.GetOrCreate(z);
            if (Fields[fi].Kind == KFloat)
            {
                float f;
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return "不是合法数字";
                switch (key)
                {
                    case "Speed":       o.Speed = f; break;
                    case "OriginSpeed": o.OriginSpeed = f; break;
                    case "ArmorValue":  o.ArmorValue = f; break;
                    case "TakeDmgMult": o.TakeDmgMult = f; break;
                }
            }
            else if (Fields[fi].Kind == KLong)
            {
                long l;
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return "不是合法整数";
                if (key == "Health") o.Health = l < 0 ? 0L : l;
                else if (key == "MaxHealth") o.MaxHealth = l < 1L ? 1L : l;
            }
            else
            {
                int v;
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return "不是合法整数";
                switch (key)
                {
                    case "AttackDamage": o.AttackDamage = v; break;
                    case "Level":        o.Level = v; break;
                    case "Row":          o.Row = Mathf.Clamp(v, 0, 6); break;
                    case "Armor1":       o.Armor1 = v; break;
                    case "Armor1Max":    o.Armor1Max = v; break;
                    case "Armor2":       o.Armor2 = v; break;
                    case "FreezeLevel":  o.FreezeLevel = v; break;
                    case "PoisonLevel":  o.PoisonLevel = v; break;
                }
            }
            try { ZombieOverrides.Apply(z); } catch { }
            return null;
        }

        internal static bool IsOverridden(Zombie z, int fi)
        {
            if (z == null || fi < 0 || fi >= Fields.Length) return false;
            ZombieOverride o = ZombieOverrides.Get(z);
            if (o == null) return false;
            switch (Fields[fi].Key)
            {
                case "Health":       return o.Health >= 0L;
                case "MaxHealth":    return o.MaxHealth >= 0L;
                case "Speed":        return o.Speed >= 0f;
                case "OriginSpeed":  return o.OriginSpeed >= 0f;
                case "AttackDamage": return o.AttackDamage >= 0;
                case "Level":        return o.Level >= 0;
                case "Row":          return o.Row >= 0;
                case "Armor1":       return o.Armor1 >= 0;
                case "Armor1Max":    return o.Armor1Max >= 0;
                case "Armor2":       return o.Armor2 >= 0;
                case "ArmorValue":   return o.ArmorValue >= 0f;
                case "TakeDmgMult":  return o.TakeDmgMult >= 0f;
                case "FreezeLevel":  return o.FreezeLevel >= 0;
                case "PoisonLevel":  return o.PoisonLevel >= 0;
            }
            return false;
        }

        internal static void ClearField(Zombie z, int fi)
        {
            if (z == null || fi < 0 || fi >= Fields.Length) return;
            ZombieOverride o = ZombieOverrides.Get(z);
            if (o == null) return;
            switch (Fields[fi].Key)
            {
                case "Health":       o.Health = -1L; break;
                case "MaxHealth":    o.MaxHealth = -1L; break;
                case "Speed":        o.Speed = -1f; break;
                case "OriginSpeed":  o.OriginSpeed = -1f; break;
                case "AttackDamage": o.AttackDamage = -1; break;
                case "Level":        o.Level = -1; break;
                case "Row":          o.Row = -1; break;
                case "Armor1":       o.Armor1 = -1; break;
                case "Armor1Max":    o.Armor1Max = -1; break;
                case "Armor2":       o.Armor2 = -1; break;
                case "ArmorValue":   o.ArmorValue = -1f; break;
                case "TakeDmgMult":  o.TakeDmgMult = -1f; break;
                case "FreezeLevel":  o.FreezeLevel = -1; break;
                case "PoisonLevel":  o.PoisonLevel = -1; break;
            }
        }

        internal static void ClearAll(Zombie z) { ZombieOverrides.Clear(z); }

        /// <summary>
        /// 冻结一只僵尸。
        /// **实测坑**：这套构建里 `SetFreeze(30f, 3)` 是空操作（调用不报错，但 `freezeLevel` 一直是 0，
        /// 界面/ESP 上完全看不出冻结）。直接写 `freezeLevel` 属性才是有效的，两个都做。
        /// </summary>
        internal static void Freeze(Zombie z)
        {
            if (z == null) return;
            try { z.SetFreeze(30f, 3); } catch { }
            try { if (z.freezeLevel < 3) z.freezeLevel = 3; } catch { }
        }

        /// <summary>解冻（undo 用：关掉"持续冻结"要让僵尸真的能走）</summary>
        internal static void Thaw(Zombie z)
        {
            if (z == null) return;
            try { if (z.freezeLevel > 0) z.freezeLevel = 0; } catch { }
        }

        // ---------------------------------------------------------------- 行为开关
        internal struct FlagDef { public string Key, Name; }

        internal static readonly FlagDef[] Flags =
        {
            new FlagDef { Key="GodMode",     Name="无敌（持续回满血）" },
            new FlagDef { Key="StopMoving",  Name="停止移动" },
            new FlagDef { Key="KeepFrozen",  Name="持续冻结" },
            new FlagDef { Key="MindControl", Name="魅惑（变友军）" },
        };

        internal static int FlagCount { get { return Flags.Length; } }
        internal static string FlagName(int i) { return (i >= 0 && i < Flags.Length) ? Flags[i].Name : "?"; }

        internal static bool GetFlag(Zombie z, int i)
        {
            if (z == null || i < 0 || i >= Flags.Length) return false;
            ZombieOverride o = ZombieOverrides.Get(z);
            if (o != null)
            {
                switch (Flags[i].Key)
                {
                    case "GodMode":     if (o.GodMode) return true; break;
                    case "StopMoving":  if (o.StopMoving) return true; break;
                    case "KeepFrozen":  if (o.KeepFrozen) return true; break;
                    case "MindControl": if (o.MindControl) return true; break;
                }
            }
            try
            {
                switch (Flags[i].Key)
                {
                    case "GodMode":     return false;             // 只认覆盖项
                    case "MindControl": return z.isMindControlled;
                    case "KeepFrozen":  return z.freezeLevel >= 3;
                    case "StopMoving":  return z.theSpeed == 0f;
                }
            }
            catch { }
            return false;
        }

        internal static void SetFlag(Zombie z, int i, bool v)
        {
            if (z == null || i < 0 || i >= Flags.Length) return;
            ZombieOverride o = ZombieOverrides.GetOrCreate(z);
            switch (Flags[i].Key)
            {
                case "GodMode":     o.GodMode = v; break;
                case "StopMoving":  o.StopMoving = v; break;
                case "KeepFrozen":  o.KeepFrozen = v; break;
                case "MindControl": o.MindControl = v; break;
            }
            try { ZombieOverrides.Apply(z); } catch { }
            // 关掉"持续冻结"时得主动解冻，否则它会一直冻着（freezeLevel 不会自己掉）
            if (Flags[i].Key == "KeepFrozen" && !v) Thaw(z);
        }

        /// <summary>
        /// 僵尸变身：**不能只改 theZombieType**（僵尸没有 ReplaceSprite，贴图/技能不会变），
        /// 所以按"同位置新建一只目标类型 + 让原来那只死掉"来做，新僵尸由游戏完整初始化。
        /// </summary>
        internal static string Transform(Zombie z, int typeId)
        {
            if (z == null) return "没有选中僵尸";
            if (typeId < 0) return "僵尸类型无效";
            int row; float x; bool mind;
            try
            {
                row = z.theZombieRow;
                x = 9.9f;
                try { x = z.transform.position.x; } catch { }
                mind = z.isMindControlled;
            }
            catch (Exception e) { return "读不到僵尸信息 " + e.GetType().Name; }
            if (x <= 0f || x > 12f) x = 9.9f;

            string r = Actions.ActionSpawnZombie(row, typeId, x, mind);
            if (r == null || !r.StartsWith("已在")) return "变身失败：" + r;
            try { z.Die(0); } catch { }
            return null;
        }

        internal static string N(float f) { return f.ToString("0.###", CultureInfo.InvariantCulture); }
    }
}
