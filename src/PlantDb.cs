using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PvzRhCheat
{
    /// <summary>
    /// 游戏内菜单的数据层：全部**直接在当前进程里读写游戏对象**，不经过任何 IPC/序列化，
    /// 所以不存在"传输丢数据"的问题。
    ///
    /// 设计原则（按用户要求）：编辑框里显示的是**植物当前的真实数值**，用户改多少就写多少，
    /// 不存在"-1 代表不修改"这种隐藏语义。未改动过的项仍然是"不干预"状态（内部 -1），
    /// 被改动过的项在界面上会用黄色标出来，并且可以单独还原。
    /// </summary>
    internal static class PlantDb
    {
        // ---------------------------------------------------------------- 中文名
        // 必须缓存：Lawnf.GetName 每次调用都会在 Il2Cpp 侧新建一个字符串，
        // 界面每帧要给几十行取名字，不缓存的话内存会一路涨上去（实测把游戏撑爆过）。
        private static readonly Dictionary<int, string> _cnCache = new Dictionary<int, string>(1200);

        internal static string CnName(int typeId)
        {
            if (typeId < 0) return "";
            string s;
            if (_cnCache.TryGetValue(typeId, out s)) return s;
            try { s = Lawnf.GetName((PlantType)typeId) ?? ""; }
            catch { s = ""; }
            // 名字可能因为字符串表还没加载而暂时为空，先不缓存，等加载好了再取
            if (!string.IsNullOrEmpty(s)) _cnCache[typeId] = s;
            return s;
        }

        internal static string CnName(Plant p)
        {
            if (p == null) return "";
            try { return CnName((int)p.thePlantType); } catch { return ""; }
        }

        /// <summary>"豌豆射手(#23)" 这种带编号的显示名，编号便于在小字框里区分同名植物</summary>
        internal static string Label(Plant p)
        {
            if (p == null) return "?";
            int t = -1; try { t = (int)p.thePlantType; } catch { }
            string cn = CnName(t);
            if (string.IsNullOrEmpty(cn)) cn = "未知";
            return cn + " #" + t;
        }

        // ---------------------------------------------------------------- 植物类型表
        private static int[] _tids;
        private static string[] _tnames;

        internal static int TypeCount
        {
            get { BuildTypes(); return _tids.Length; }
        }

        internal static int TypeIdAt(int i) { BuildTypes(); return (i >= 0 && i < _tids.Length) ? _tids[i] : -1; }
        internal static string TypeNameAt(int i) { BuildTypes(); return (i >= 0 && i < _tnames.Length) ? _tnames[i] : ""; }

        private static void BuildTypes()
        {
            if (_tids != null) return;
            var ids = new List<int>(800);
            var nms = new List<string>(800);
            try
            {
                foreach (object o in Enum.GetValues(typeof(PlantType)))
                {
                    int v = Convert.ToInt32(o);
                    if (v < 0) continue;
                    string cn = CnName(v);
                    if (string.IsNullOrEmpty(cn)) cn = o.ToString();
                    ids.Add(v);
                    nms.Add(cn);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[类型表] 枚举失败: " + e.Message); }
            _tids = ids.ToArray();
            _tnames = nms.ToArray();
            Plugin.Log.LogInfo("[类型表] 共 " + _tids.Length + " 种植物类型");
        }

        /// <summary>按名字/编号筛选，返回命中的下标列表（filter 为空时返回全部）</summary>
        internal static List<int> FilterTypes(string filter, bool zombie)
        {
            if (zombie) { BuildZombieTypes(); return Filter(_zids, _znms, filter); }
            BuildTypes();
            return Filter(_tids, _tnames, filter);
        }

        private static List<int> Filter(int[] ids, string[] nms, string filter)
        {
            var res = new List<int>(ids.Length);
            if (string.IsNullOrEmpty(filter))
            {
                for (int i = 0; i < ids.Length; i++) res.Add(i);
                return res;
            }
            string f = filter.Trim();
            int asId;
            bool numeric = int.TryParse(f, NumberStyles.Integer, CultureInfo.InvariantCulture, out asId);
            for (int i = 0; i < ids.Length; i++)
            {
                if (numeric && ids[i] == asId) { res.Add(i); continue; }
                string cn = nms[i];
                if (cn != null && cn.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) res.Add(i);
            }
            return res;
        }

        // ---------------------------------------------------------------- 僵尸类型表
        private static int[] _zids;
        private static string[] _znms;

        internal static int ZombieTypeCount { get { BuildZombieTypes(); return _zids.Length; } }
        internal static int ZombieTypeIdAt(int i) { BuildZombieTypes(); return (i >= 0 && i < _zids.Length) ? _zids[i] : -1; }
        internal static string ZombieTypeNameAt(int i) { BuildZombieTypes(); return (i >= 0 && i < _znms.Length) ? _znms[i] : ""; }

        private static void BuildZombieTypes()
        {
            if (_zids != null) return;
            var ids = new List<int>(700);
            var nms = new List<string>(700);
            try
            {
                foreach (object o in Enum.GetValues(typeof(ZombieType)))
                {
                    int v = Convert.ToInt32(o);
                    if (v < 0) continue;
                    string cn = "";
                    try { cn = Lawnf.GetName((ZombieType)v) ?? ""; } catch { }
                    if (string.IsNullOrEmpty(cn)) cn = o.ToString();
                    ids.Add(v);
                    nms.Add(cn);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[类型表] 僵尸枚举失败: " + e.Message); }
            _zids = ids.ToArray();
            _znms = nms.ToArray();
            Plugin.Log.LogInfo("[类型表] 共 " + _zids.Length + " 种僵尸类型");
        }

        internal static string CnNameZombie(int typeId)
        {
            if (typeId < 0) return "";
            try { return Lawnf.GetName((ZombieType)typeId) ?? ""; }
            catch { return ""; }
        }

        // ---------------------------------------------------------------- 数值字段表
        internal const int KInt = 0, KFloat = 1, KText = 2;

        internal struct FieldDef
        {
            public string Key;    // 写进 PlantOverride 的字段名
            public string Name;   // 界面中文名
            public string Unit;
            public int Kind;
            public bool Live;     // true = 能从植物对象上读到当前值
        }

        internal static readonly FieldDef[] Fields =
        {
            new FieldDef { Key="MaxHealth",        Name="血量上限",     Unit="",   Kind=KInt,   Live=true  },
            new FieldDef { Key="Health",           Name="当前血量",     Unit="",   Kind=KInt,   Live=true  },
            new FieldDef { Key="AttackDamage",     Name="攻击力",       Unit="",   Kind=KInt,   Live=true  },
            new FieldDef { Key="AttackInterval",   Name="攻击间隔",     Unit="秒", Kind=KFloat, Live=true  },
            new FieldDef { Key="SpeedMult",        Name="攻速倍率",     Unit="x",  Kind=KFloat, Live=false },
            new FieldDef { Key="DamageMult",       Name="伤害倍率",     Unit="x",  Kind=KFloat, Live=false },
            new FieldDef { Key="Defence",          Name="防御",         Unit="",   Kind=KFloat, Live=true  },
            new FieldDef { Key="Level",            Name="等级",         Unit="",   Kind=KInt,   Live=true  },
            new FieldDef { Key="Stage",            Name="阶段",         Unit="",   Kind=KInt,   Live=true  },
            new FieldDef { Key="ThePlantType",     Name="植物类型",     Unit="",   Kind=KInt,   Live=true  },
            new FieldDef { Key="SkinType",         Name="皮肤编号",     Unit="",   Kind=KInt,   Live=true  },
            new FieldDef { Key="Scale",            Name="缩放",         Unit="",   Kind=KFloat, Live=true  },
            new FieldDef { Key="BulletType",       Name="子弹类型",     Unit="",   Kind=KInt,   Live=false },
            new FieldDef { Key="BulletDamageMult", Name="子弹伤害倍率", Unit="x",  Kind=KFloat, Live=false },
            new FieldDef { Key="BulletSpeedMult",  Name="子弹速度倍率", Unit="x",  Kind=KFloat, Live=false },
            new FieldDef { Key="BulletPierce",     Name="子弹穿透数",   Unit="",   Kind=KInt,   Live=false },
            new FieldDef { Key="Effects",          Name="附加效果",     Unit="",   Kind=KText,  Live=false },
        };

        internal static int FieldCount { get { return Fields.Length; } }

        internal static string FieldName(int i) { return (i >= 0 && i < Fields.Length) ? Fields[i].Name : "?"; }
        internal static string FieldUnit(int i) { return (i >= 0 && i < Fields.Length) ? Fields[i].Unit : ""; }
        internal static int FieldKind(int i) { return (i >= 0 && i < Fields.Length) ? Fields[i].Kind : KInt; }
        internal static bool FieldIsLive(int i) { return i >= 0 && i < Fields.Length && Fields[i].Live; }

        /// <summary>读植物**当前真实数值**（用户要的就是这个：先读出来，再改）</summary>
        internal static string GetLive(Plant p, int fi)
        {
            if (p == null || fi < 0 || fi >= Fields.Length) return "";
            string key = Fields[fi].Key;
            PlantOverride ov = Overrides.Get(p);
            try
            {
                switch (key)
                {
                    case "MaxHealth":    return p.thePlantMaxHealth.ToString(CultureInfo.InvariantCulture);
                    case "Health":       return p.thePlantHealth.ToString(CultureInfo.InvariantCulture);
                    case "AttackDamage": return p.attackDamage.ToString(CultureInfo.InvariantCulture);
                    case "AttackInterval": return N(p.thePlantAttackInterval);
                    case "Defence":      return N(p.defence);
                    case "Level":        return p.theLevel.ToString(CultureInfo.InvariantCulture);
                    case "Stage":        return p.thePlantStage.ToString(CultureInfo.InvariantCulture);
                    case "ThePlantType": return ((int)p.thePlantType).ToString(CultureInfo.InvariantCulture);
                    case "SkinType":     return p.skinType.ToString(CultureInfo.InvariantCulture);
                    case "Scale":        return N(p.transform.localScale.x);
                }
            }
            catch (Exception e) { Plugin.LogOnce("读植物字段/" + key, e); }

            // 剩下的（倍率/子弹/效果）只有覆盖值，没有"当前值"可读
            if (ov != null)
            {
                switch (key)
                {
                    case "DamageMult":       return N(ov.DamageMult);
                    case "SpeedMult":        return N(ov.SpeedMult);
                    case "BulletType":       return ov.BulletType.ToString(CultureInfo.InvariantCulture);
                    case "BulletDamageMult": return N(ov.BulletDamageMult);
                    case "BulletSpeedMult":  return N(ov.BulletSpeedMult);
                    case "BulletPierce":     return ov.BulletPierce.ToString(CultureInfo.InvariantCulture);
                    case "Effects":          return ov.Effects ?? "";
                }
            }
            switch (key)
            {
                case "DamageMult": case "SpeedMult":
                case "BulletDamageMult": case "BulletSpeedMult": return "1";
                case "BulletType": case "BulletPierce": return "-1";
                case "Effects": return "";
            }
            return "";
        }

        /// <summary>把用户输入的**绝对值**写进该植物的覆盖项</summary>
        internal static string SetValue(Plant p, int fi, string text)
        {
            if (p == null || fi < 0 || fi >= Fields.Length) return "没有选中植物";
            string key = Fields[fi].Key;
            if (Fields[fi].Kind == KText)
            {
                Overrides.GetOrCreate(p).Effects = text ?? "";
                return null;
            }

            text = (text ?? "").Trim();
            if (text.Length == 0) return "请输入数值";

            string kv;
            if (Fields[fi].Kind == KFloat)
            {
                float f;
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                    return "不是合法数字";
                kv = key + "=" + f.ToString("0.###", CultureInfo.InvariantCulture);
            }
            else
            {
                int v;
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    return "不是合法整数";
                kv = key + "=" + v.ToString(CultureInfo.InvariantCulture);
            }
            IpcBridge.ApplyFields(Overrides.GetOrCreate(p), kv);
            // 立刻生效一次，不用等周期施加
            try { Overrides.Apply(p); } catch { }
            return null;
        }

        /// <summary>该项是否已被改动（用于界面高亮）</summary>
        internal static bool IsOverridden(Plant p, int fi)
        {
            if (p == null || fi < 0 || fi >= Fields.Length) return false;
            PlantOverride o = Overrides.Get(p);
            if (o == null) return false;
            switch (Fields[fi].Key)
            {
                case "MaxHealth":        return o.MaxHealth >= 0;
                case "Health":           return o.Health >= 0;
                case "AttackDamage":     return o.AttackDamage >= 0;
                case "AttackInterval":   return o.AttackInterval >= 0f;
                case "Defence":          return o.Defence >= 0f;
                case "Level":            return o.Level >= 0;
                case "Stage":            return o.Stage >= 0;
                case "ThePlantType":     return o.ThePlantType >= 0;
                case "SkinType":         return o.SkinType >= 0;
                case "Scale":            return o.Scale >= 0f;
                case "DamageMult":       return Math.Abs(o.DamageMult - 1f) > 0.0001f;
                case "SpeedMult":        return Math.Abs(o.SpeedMult - 1f) > 0.0001f;
                case "BulletType":       return o.BulletType >= 0;
                case "BulletDamageMult": return Math.Abs(o.BulletDamageMult - 1f) > 0.0001f;
                case "BulletSpeedMult":  return Math.Abs(o.BulletSpeedMult - 1f) > 0.0001f;
                case "BulletPierce":     return o.BulletPierce >= 0;
                case "Effects":          return !string.IsNullOrEmpty(o.Effects);
            }
            return false;
        }

        /// <summary>还原单项</summary>
        internal static void ClearField(Plant p, int fi)
        {
            if (p == null || fi < 0 || fi >= Fields.Length) return;
            PlantOverride o = Overrides.Get(p);
            if (o == null) return;
            switch (Fields[fi].Key)
            {
                case "MaxHealth":        o.MaxHealth = -1; break;
                case "Health":           o.Health = -1; break;
                case "AttackDamage":     o.AttackDamage = -1; break;
                case "AttackInterval":   o.AttackInterval = -1f; break;
                case "Defence":          o.Defence = -1f; break;
                case "Level":            o.Level = -1; break;
                case "Stage":            o.Stage = -1; break;
                case "ThePlantType":     o.ThePlantType = -1; break;
                case "SkinType":         o.SkinType = -1; break;
                case "Scale":            o.Scale = -1f; break;
                case "DamageMult":       o.DamageMult = 1f; break;
                case "SpeedMult":        o.SpeedMult = 1f; break;
                case "BulletType":       o.BulletType = -1; break;
                case "BulletDamageMult": o.BulletDamageMult = 1f; break;
                case "BulletSpeedMult":  o.BulletSpeedMult = 1f; break;
                case "BulletPierce":     o.BulletPierce = -1; break;
                case "Effects":          o.Effects = ""; break;
            }
        }

        internal static void ClearAll(Plant p)
        {
            if (p == null) return;
            Overrides.Clear(p);
        }

        // ---------------------------------------------------------------- 机制开关
        internal struct FlagDef { public string Key; public string Name; }

        internal static readonly FlagDef[] Flags =
        {
            new FlagDef { Key="GodMode",       Name="免伤（打不死）" },
            new FlagDef { Key="Invincible",    Name="无敌字段 invincible" },
            new FlagDef { Key="Undead",        Name="不死字段 undead" },
            new FlagDef { Key="KeepShooting",  Name="持续射击 keepShooting" },
            new FlagDef { Key="AlwaysLightUp", Name="常亮 alwaysLightUp" },
            new FlagDef { Key="Uncrashable",   Name="不碎 uncrashable" },
        };

        internal static int FlagCount { get { return Flags.Length; } }
        internal static string FlagName(int i) { return (i >= 0 && i < Flags.Length) ? Flags[i].Name : "?"; }

        internal static bool GetFlag(Plant p, int i)
        {
            if (p == null || i < 0 || i >= Flags.Length) return false;
            PlantOverride o = Overrides.Get(p);
            if (o != null)
            {
                switch (Flags[i].Key)
                {
                    case "GodMode":       if (o.GodMode) return true; break;
                    case "Invincible":    if (o.Invincible) return true; break;
                    case "Undead":        if (o.Undead) return true; break;
                    case "KeepShooting":  if (o.KeepShooting) return true; break;
                    case "AlwaysLightUp": if (o.AlwaysLightUp) return true; break;
                    case "Uncrashable":   if (o.Uncrashable) return true; break;
                }
            }
            try
            {
                switch (Flags[i].Key)
                {
                    case "GodMode":       return false;             // 免伤只认覆盖项
                    case "Invincible":    return p.invincible;
                    case "Undead":        return p.undead;
                    case "KeepShooting":  return p.keepShooting;
                    case "AlwaysLightUp": return p.alwaysLightUp;
                    case "Uncrashable":   return p.uncrashable;
                }
            }
            catch { }
            return false;
        }

        internal static void SetFlag(Plant p, int i, bool v)
        {
            if (p == null || i < 0 || i >= Flags.Length) return;
            PlantOverride o = Overrides.GetOrCreate(p);
            switch (Flags[i].Key)
            {
                case "GodMode":       o.GodMode = v; break;
                case "Invincible":    o.Invincible = v; break;
                case "Undead":        o.Undead = v; break;
                case "KeepShooting":  o.KeepShooting = v; break;
                case "AlwaysLightUp": o.AlwaysLightUp = v; break;
                case "Uncrashable":   o.Uncrashable = v; break;
            }
            try { Overrides.Apply(p); } catch { }
        }

        // ---------------------------------------------------------------- 融合配方
        private static readonly List<int> _recFlat = new List<int>(64);
        private static int _recFor = -2;

        internal static int RecipeFor { get { return _recFor; } }
        internal static int RecipeCount { get { return _recFlat.Count / 2; } }

        internal static int RecipePartner(int i)
        {
            int k = i * 2;
            return (k >= 0 && k + 1 < _recFlat.Count) ? _recFlat[k] : -1;
        }

        internal static int RecipeResult(int i)
        {
            int k = i * 2 + 1;
            return (k >= 0 && k < _recFlat.Count) ? _recFlat[k] : -1;
        }

        /// <summary>读出游戏自己的融合树：伙伴类型 -> 结果类型</summary>
        internal static void LoadRecipes(int typeId)
        {
            if (_recFor == typeId) return;
            _recFor = typeId;
            _recFlat.Clear();
            if (typeId < 0) return;
            try
            {
                PlantMixTreeNode node = PlantMixTreeManager.GetTree((PlantType)typeId);
                if (node == null) { Plugin.Log.LogInfo("[融合] GetTree 为空: " + typeId); return; }
                var rec = node.Recipes;
                if (rec == null) { Plugin.Log.LogInfo("[融合] 该植物没有配方: " + typeId); return; }
                foreach (var kv in rec)
                {
                    _recFlat.Add((int)kv.Key);
                    _recFlat.Add((int)kv.Value);
                }
                Plugin.Log.LogInfo("[融合] " + CnName(typeId) + " 配方 " + RecipeCount + " 条");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[融合] 读取失败: " + e.GetType().Name + " " + e.Message); }
        }

        internal static void InvalidateRecipes() { _recFor = -2; ParentMapReset(); }

        /// <summary>
        /// 写一条自定义配方：把 "本植物 + 伙伴" 的结果改成 result。
        ///
        /// 只写 PlantMixTreeNode.Recipes（这是我们在本项目里已经验证过无数次能安全读写的字典）。
        /// **绝不碰 MixData._recipes**：它的键是 Il2CppSystem.ValueTuple，
        /// 之前用 out/构造这种类型直接把游戏搞闪退了（详见 2026-09-13 的崩溃记录）。
        /// </summary>
        internal static string AddRecipe(int selfType, int partner, int result)
        {
            if (selfType < 0 || partner < 0 || result < 0) return "类型无效";
            string err = null;
            try
            {
                PlantMixTreeNode node = PlantMixTreeManager.GetTree((PlantType)selfType);
                if (node == null) err = "融合树节点为空";
                else
                {
                    var rec = node.Recipes;
                    if (rec == null) err = "该植物的配方表为空";
                    else rec[(PlantType)partner] = (PlantType)result;
                }
            }
            catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }

            InvalidateRecipes();
            ParentMapReset();

            string msg = CnName(selfType) + " + " + CnName(partner) + " → " + CnName(result) + "   ";
            if (err != null) { msg += "写入失败：" + err; Plugin.Log.LogWarning("[融合] " + msg); return msg; }

            msg += "已写入融合表。";
            // 用游戏自己的查表接口校验（out 的是枚举，二进制兼容，绝对安全）
            msg += "  " + MixProbe(selfType, partner);
            Plugin.Log.LogInfo("[融合] " + msg);
            return msg;
        }

        /// <summary>只读探测：游戏的两张融合表现在各查到什么（out 全是枚举，安全）</summary>
        internal static string MixProbe(int a, int b)
        {
            string s = "";
            try
            {
                PlantType r;
                s += "树查表=" + (PlantMixTreeManager.TryGetMixResult((PlantType)a, (PlantType)b, out r) ? CnName((int)r) : "未命中");
            }
            catch (Exception e) { s += "树查表异常(" + e.GetType().Name + ")"; }
            try
            {
                PlantType r2;
                s += "  MixData=" + (MixData.TryGetMix((PlantType)a, (PlantType)b, out r2, true) ? CnName((int)r2) : "未命中");
            }
            catch (Exception e) { s += "  MixData异常(" + e.GetType().Name + ")"; }
            return s;
        }

        // ---------------------------------------------------------------- 反向表
        // 结果类型 -> "父A + 父B"
        // 只用 GetTree / PlantMixTrees 迭代建立。**不用 MixData.TryGetDisMix（out ValueTuple）
        // 也不用 ChildToParents**，那两个是之前闪退的元凶。
        private static Dictionary<int, string> _parents;
        private static bool _parentsBuilding;

        private static void ParentMapReset() { _parents = null; _parentsBuilding = false; }

        private static void BuildParentMap()
        {
            if (_parents != null || _parentsBuilding) return;
            _parentsBuilding = true;
            var map = new Dictionary<int, string>(1088);
            try
            {
                int recipes = 0;
                var trees = PlantMixTreeManager.PlantMixTrees;
                if (trees == null) Plugin.Log.LogWarning("[融合] PlantMixTrees 为空，无法建立反向表");
                else
                {
                    foreach (var kv in trees)
                    {
                        int self = (int)kv.Key;
                        PlantMixTreeNode node = kv.Value;
                        if (node == null) continue;
                        var rec = node.Recipes;
                        if (rec == null) continue;
                        foreach (var r in rec)
                        {
                            int res = (int)r.Value;
                            if (res < 0) continue;
                            string pair = CnName(self) + " + " + CnName((int)r.Key);
                            string cur;
                            if (map.TryGetValue(res, out cur))
                            {
                                if (cur.Length < 240) map[res] = cur + "   |   " + pair;
                            }
                            else map[res] = pair;
                            recipes++;
                        }
                    }
                }
                _parents = map;
                Plugin.Log.LogInfo("[融合] 反向表建立完成：" + recipes + " 条配方，覆盖 " + map.Count + " 种结果");
            }
            catch (Exception e)
            {
                _parents = map;
                Plugin.Log.LogWarning("[融合] 建立反向表失败: " + e.GetType().Name + " " + e.Message);
            }
            finally { _parentsBuilding = false; }
        }

        /// <summary>反过来查：这种植物是哪两种合出来的</summary>
        internal static string ParentsOf(int typeId)
        {
            if (typeId < 0) return "";
            BuildParentMap();
            string s;
            return (_parents != null && _parents.TryGetValue(typeId, out s)) ? s : "";
        }

        // ------------------------------------------------ 融合（两阶段：先腾格子，再让游戏建新植物）
        //
        // 关键实测结论：
        //   · SetPlant(col,row,type,target != null, ...) 只是把**同一个对象**的 thePlantType 改掉：
        //     指针不变、贴图不变、属性不变。所以那样融合"只是改了名字"（用户实测确认）。
        //   · SetPlant(col,row,type,null, ...) 在**空格子**上会真的新建一株植物
        //     （走 AddToList / SetPlantAttributes / SetTransform / CreatePlantParticle），
        //     模型·血量·子弹·技能都是游戏自己按类型初始化好的。
        //   => 想得到"真融合"，就必须先把格子腾空，再让游戏在空格子上建结果植物。

        internal struct FusePlan
        {
            public int SelfType, PartnerType, ResultType, Col, Row;
            public long OldPtr;
            public string SelfName;
        }

        /// <summary>算出融合结果；返回 null 表示成功</summary>
        internal static string PlanFuse(Plant p, int partnerType, out FusePlan plan)
        {
            plan = default(FusePlan);
            if (p == null) return "没有选中植物";
            if (partnerType < 0) return "伙伴类型无效";
            try
            {
                plan.SelfType = (int)p.thePlantType;
                plan.Col = p.thePlantColumn;
                plan.Row = p.thePlantRow;
                plan.OldPtr = p.Pointer.ToInt64();
            }
            catch (Exception e) { return "读不到植物信息 " + e.GetType().Name; }
            plan.PartnerType = partnerType;
            plan.SelfName = CnName(plan.SelfType) + " #" + plan.SelfType;

            PlantType rt = (PlantType)(-1);
            bool found;
            try { found = PlantMixTreeManager.TryGetMixResult((PlantType)plan.SelfType, (PlantType)partnerType, out rt); }
            catch (Exception e) { return "查融合表出错 " + e.GetType().Name + " " + e.Message; }
            if (!found || (int)rt < 0)
                return plan.SelfName + " + " + CnName(partnerType) + "：游戏融合表里没有这条配方（可以在下面「写入配方」自己加一条）";
            plan.ResultType = (int)rt;
            return null;
        }

        /// <summary>让这一格上的原植物先退场（走游戏自己的 Die）</summary>
        internal static string EjectOld(Plant p)
        {
            if (p == null) return "植物已经不在了";
            try { p.Die(); return null; }
            catch (Exception e)
            {
                // Die 不行就直接销毁
                try { UnityEngine.Object.Destroy(p.gameObject); return null; }
                catch (Exception e2) { return "Die/Destroy 都失败 " + e.GetType().Name + "/" + e2.GetType().Name; }
            }
        }

        internal static Plant CellPlant(int col, int row)
        {
            try
            {
                Board b = Board.Instance;
                if (b == null) return null;
                return Lawnf.GetPlant(col, row, b);
            }
            catch { return null; }
        }

        /// <summary>在空格子上让游戏新建一株植物（完整初始化）</summary>
        internal static string SpawnAt(int col, int row, int typeId, Vector2 pos, out Plant created)
        {
            created = null;
            try
            {
                CreatePlant cp = CreatePlant.Instance;
                if (cp == null) return "CreatePlant.Instance 为空";
                created = cp.SetPlant(col, row, (PlantType)typeId, null, pos, true, true, null);
                if (created == null) return "SetPlant 返回空";
                return null;
            }
            catch (Exception e) { return "SetPlant 异常 " + e.GetType().Name + " " + e.Message; }
        }

        /// <summary>新植物建好之后的收尾：转移用户数值 + 强制刷新贴图/属性 + 选中</summary>
        internal static void FinishNewPlant(long oldPtr, Plant nu)
        {
            if (nu == null) return;
            try
            {
                try { nu.ReplaceSprite(); } catch (Exception e) { Plugin.LogOnce("融合/贴图", e); }
                RefreshAttributes(nu);
                Plant old = Actions.PlantByPtr(oldPtr);
                Overrides.Transfer(old, nu);
                Actions.Select(nu);
            }
            catch (Exception e) { Plugin.Log.LogWarning("[融合] 收尾失败: " + e.Message); }
        }

        /// <summary>兜底：不腾格子，直接把原对象的类型改掉（模型可能不更新）</summary>
        internal static string InPlaceChange(Plant p, int resultType)
        {
            if (p == null) return "植物已经不在了";
            try
            {
                CreatePlant cp = CreatePlant.Instance;
                if (cp == null) return "CreatePlant.Instance 为空";
                Vector2 pos = new Vector2(0f, 0f);
                try { Vector3 wp = p.transform.position; pos = new Vector2(wp.x, wp.y); } catch { }
                Plant res = cp.SetPlant(p.thePlantColumn, p.thePlantRow, (PlantType)resultType, p, pos, true, true, null);
                if (res == null) return "SetPlant 返回空";
                try { res.ReplaceSprite(); } catch (Exception e) { Plugin.LogOnce("融合/贴图", e); }
                RefreshAttributes(res);
                Actions.Select(res);
                return null;
            }
            catch (Exception e) { return "原地改名失败 " + e.GetType().Name + " " + e.Message; }
        }

        /// <summary>CreatePlant.SetPlantAttributes 是私有的，用反射调（作用是把植物属性按类型重设一遍）</summary>
        private static void RefreshAttributes(Plant p)
        {
            if (p == null) return;
            try
            {
                CreatePlant cp = CreatePlant.Instance;
                if (cp == null) return;
                var mi = HarmonyLib.AccessTools.Method(typeof(CreatePlant), "SetPlantAttributes");
                if (mi != null) mi.Invoke(cp, new object[] { p });
            }
            catch (Exception e) { Plugin.LogOnce("融合/刷新属性", e); }
        }

        private static bool TrySetPlant(int col, int row, int typeId, Plant target, Plant from, bool free, out string err)
        {
            err = null;
            try
            {
                CreatePlant cp = CreatePlant.Instance;
                if (cp == null) { err = "CreatePlant.Instance 为空"; return false; }
                Vector2 pos = new Vector2(0f, 0f);
                try { Vector3 wp = from.transform.position; pos = new Vector2(wp.x, wp.y); } catch { }
                Plant res = cp.SetPlant(col, row, (PlantType)typeId, target, pos, free, true, null);
                if (res == null) { err = "SetPlant 返回空"; return false; }
                return true;
            }
            catch (Exception e) { err = "SetPlant 异常 " + e.GetType().Name + " " + e.Message; return false; }
        }

        /// <summary>看这一格现在是不是期望的结果植物，而且不是原来那个对象</summary>
        private static bool CheckResult(Board board, int col, int row, int expect, long oldPtr, out string how)
        {
            how = "";
            try
            {
                if (board == null) return false;
                Plant now = Lawnf.GetPlant(col, row, board);
                if (now == null) { how = "格子上没有植物"; return false; }
                int t = (int)now.thePlantType;
                long np = now.Pointer.ToInt64();
                if (t != expect) { how = "格子上是 " + CnName(t) + "(#" + t + ")，不是期望的 " + CnName(expect); return false; }
                if (np == oldPtr) { how = "类型对了但仍是原来那个对象（没重新初始化）"; return false; }
                Actions.Select(now);
                how = "新对象 ptr=0x" + np.ToString("X");
                return true;
            }
            catch (Exception e) { how = "检查结果异常 " + e.GetType().Name; return false; }
        }

        /// <summary>
        /// 把某株植物原地换成另一种（不查融合表，想变什么就变什么）。
        /// 先试"腾空格子让游戏新建"，不行再退回原地改类型。
        /// </summary>
        internal static string Transform(Plant p, int typeId)
        {
            if (p == null) return "没有选中植物";
            if (typeId < 0) return "类型无效";

            int col, row;
            try { col = p.thePlantColumn; row = p.thePlantRow; }
            catch (Exception e) { return "读不到格子 " + e.GetType().Name; }

            Board board = null;
            try { board = Board.Instance; } catch { }
            long oldPtr = 0L;
            try { oldPtr = p.Pointer.ToInt64(); } catch { }

            // 先把这一格"再种一次目标植物"（target=null 才会真的新建）
            string e1;
            if (TrySetPlant(col, row, typeId, null, p, true, out e1))
            {
                string how;
                if (CheckResult(board, col, row, typeId, oldPtr, out how)) return null;
            }
            // 兜底：原对象改类型 + 强制刷新外观/属性
            try
            {
                Overrides.GetOrCreate(p).ThePlantType = typeId;
                p.thePlantType = (PlantType)typeId;
                try { p.ReplaceSprite(); } catch { }
                RefreshAttributes(p);
                return null;
            }
            catch (Exception e2) { return e2.GetType().Name + ": " + e2.Message; }
        }

        /// <summary>
        /// 用游戏自己的 CreatePlant.SetPlant 在某格放一株植物（targetPlant = null）。
        /// 注意：只有 targetPlant 为 null 时游戏才会**真的新建**一株植物；
        /// targetPlant 给了对象只会把那个对象的类型改掉（贴图/属性都不更新）。
        /// </summary>
        internal static string Spawn(int typeId, int col, int row)
        {
            if (typeId < 0) return "类型无效";
            try
            {
                CreatePlant cp = CreatePlant.Instance;
                if (cp == null) return "CreatePlant.Instance 为空";
                Plant res = cp.SetPlant(col, row, (PlantType)typeId, null, new Vector2(0f, 0f), true, false, null);
                if (res == null) return "SetPlant 返回空";
                return "已在 (" + col + "," + row + ") 放置 " + Label(res);
            }
            catch (Exception e)
            {
                return "放置失败: " + e.GetType().Name + " " + e.Message;
            }
        }

        /// <summary>这种植物在游戏里属于哪一类（基础 / 超级 / 终极 / 二次）—— 全部是 bool 返回，安全</summary>
        internal static string KindOf(int typeId)
        {
            if (typeId < 0) return "";
            var sb = new System.Text.StringBuilder(48);
            try { if (Lawnf.IsBasicPlant((PlantType)typeId)) sb.Append("基础植物  "); } catch { }
            try { if (Lawnf.IsSuperPlant((PlantType)typeId)) sb.Append("超级植物  "); } catch { }
            try { if (Lawnf.IsUltiPlant((PlantType)typeId)) sb.Append("终极植物  "); } catch { }
            try { if (Lawnf.IsSecondPlant((PlantType)typeId)) sb.Append("二次融合  "); } catch { }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// 自检：把「融合」页会用到的全部游戏调用跑一遍，返回文字报告。
        /// 通过 IPC 的 ACTION|FusionTest|&lt;typeId&gt; 触发，不需要看屏幕就能验证有没有踩到会闪退的接口。
        /// </summary>
        internal static string FusionSelfTest(int typeId)
        {
            var sb = new System.Text.StringBuilder(512);
            sb.Append("植物=").Append(CnName(typeId)).Append("(#").Append(typeId).Append(")  ").Append(KindOf(typeId)).Append('\n');

            LoadRecipes(typeId);
            sb.Append("配方 ").Append(RecipeCount).Append(" 条");
            if (RecipeCount > 0)
                sb.Append("，第一条 ").Append(CnName(RecipePartner(0))).Append(" → ").Append(CnName(RecipeResult(0)));
            sb.Append('\n');

            sb.Append("合成来源: ").Append(ParentsOf(typeId) ?? "").Append('\n');

            if (RecipeCount > 0)
                sb.Append("游戏查表(").Append(CnName(RecipePartner(0))).Append("): ").Append(MixProbe(typeId, RecipePartner(0))).Append('\n');

            sb.Append("反向表条数=").Append(_parents == null ? 0 : _parents.Count);
            Plugin.Log.LogInfo("[融合自检] " + sb.ToString().Replace("\n", " | "));
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 其它
        internal static string N(float f) { return f.ToString("0.###", CultureInfo.InvariantCulture); }

        internal static string EffectHint()
        {
            return "多个效果用逗号分隔";
        }
    }
}
