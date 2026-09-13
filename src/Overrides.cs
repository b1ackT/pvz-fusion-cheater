using System;
using System.Collections.Generic;
using UnityEngine;

namespace PvzRhCheat
{
    /// <summary>单株植物的修改项；-1 / 1f 表示"不修改"</summary>
    public class PlantOverride
    {
        // 机制
        public bool GodMode;          // 免伤（跳过 TakeDamage）
        public bool Undead;           // undead 字段
        public bool Invincible;       // invincible 字段
        public bool KeepShooting;     // keepShooting 字段
        public bool AlwaysLightUp;    // alwaysLightUp 字段
        public bool Uncrashable;      // uncrashable 字段

        // 换成别的植物（含融合体）：PlantType 枚举值，-1 = 不修改
        public int   ThePlantType = -1;

        // 数值
        public int   MaxHealth      = -1;
        public int   Health         = -1;
        public int   AttackDamage   = -1;
        public int   Level          = -1;
        public int   Stage          = -1;
        public float AttackInterval = -1f;
        public float Defence        = -1f;
        public float DamageMult     = 1f;   // 该植物打出的伤害倍率
        public float SpeedMult      = 1f;   // 攻速倍率（换算成 AttackInterval 倒数）

        // 模型 / 皮肤
        public int   SkinType       = -1;
        public float Scale          = -1f;

        // 子弹
        public int   BulletType        = -1;  // BulletType 枚举值，-1=不改
        public float BulletDamageMult  = 1f;
        public float BulletSpeedMult   = 1f;
        public int   BulletPierce      = -1;  // maxHitCount
        public int   BulletHitCount    = -1;  // hitCount 重置

        // 效果（EffectType 名字，逗号分隔）
        public string Effects = "";

        public bool HasAnything =>
            GodMode || Undead || Invincible || KeepShooting || AlwaysLightUp || Uncrashable ||
            ThePlantType >= 0 ||
            MaxHealth >= 0 || Health >= 0 || AttackDamage >= 0 || Level >= 0 || Stage >= 0 ||
            AttackInterval >= 0f || Defence >= 0f ||
            Math.Abs(DamageMult - 1f) > 0.0001f || Math.Abs(SpeedMult - 1f) > 0.0001f ||
            SkinType >= 0 || Scale >= 0f ||
            BulletType >= 0 || Math.Abs(BulletDamageMult - 1f) > 0.0001f ||
            Math.Abs(BulletSpeedMult - 1f) > 0.0001f || BulletPierce >= 0 || BulletHitCount >= 0 ||
            !string.IsNullOrEmpty(Effects);

        public void Reset()
        {
            GodMode = Undead = Invincible = KeepShooting = AlwaysLightUp = Uncrashable = false;
            ThePlantType = -1;
            MaxHealth = Health = AttackDamage = Level = Stage = -1;
            AttackInterval = Defence = -1f;
            DamageMult = SpeedMult = 1f;
            SkinType = -1; Scale = -1f;
            BulletType = -1; BulletDamageMult = BulletSpeedMult = 1f;
            BulletPierce = BulletHitCount = -1;
            Effects = "";
        }
    }

    /// <summary>全局默认 + 每株植物覆盖</summary>
    internal static class Overrides
    {
        public static readonly PlantOverride Global = new PlantOverride();
        private static readonly Dictionary<IntPtr, PlantOverride> PerPlant = new Dictionary<IntPtr, PlantOverride>();
        // 记录每株植物被改动前的原始攻击间隔，避免攻速倍率在周期性施加时反复相除（指数叠加）
        private static readonly Dictionary<IntPtr, float> BaseInterval = new Dictionary<IntPtr, float>();
        // 记录已经改过的子弹，避免 InitData 被多次调用时伤害重复翻倍
        private static readonly HashSet<IntPtr> BulletDone = new HashSet<IntPtr>();

        public static bool GlobalEnabled = true;

        public static PlantOverride GetOrCreate(Plant p)
        {
            if (p == null) return null;
            IntPtr key = p.Pointer;
            if (!PerPlant.TryGetValue(key, out var ov))
            {
                ov = new PlantOverride();
                PerPlant[key] = ov;
            }
            return ov;
        }

        public static PlantOverride Get(Plant p)
        {
            if (p == null) return null;
            return PerPlant.TryGetValue(p.Pointer, out var ov) ? ov : null;
        }

        public static void Clear(Plant p)
        {
            if (p == null) return;
            PerPlant.Remove(p.Pointer);
            BaseInterval.Remove(p.Pointer);
        }

        public static void ClearAll() { PerPlant.Clear(); BaseInterval.Clear(); BulletDone.Clear(); }

        /// <summary>
        /// 融合以后原来那株被销毁了，把用户为它设过的数值搬到新植物上。
        /// ThePlantType 故意不搬（否则新植物会被立刻改回去）。
        /// </summary>
        public static void Transfer(Plant from, Plant to)
        {
            if (from == null || to == null) return;
            try
            {
                PlantOverride s;
                if (!PerPlant.TryGetValue(from.Pointer, out s) || s == null) return;

                PlantOverride d = new PlantOverride
                {
                    GodMode = s.GodMode, Undead = s.Undead, Invincible = s.Invincible,
                    KeepShooting = s.KeepShooting, AlwaysLightUp = s.AlwaysLightUp,
                    Uncrashable = s.Uncrashable,
                    ThePlantType = -1,
                    MaxHealth = s.MaxHealth, Health = s.Health, AttackDamage = s.AttackDamage,
                    Level = s.Level, Stage = s.Stage,
                    AttackInterval = s.AttackInterval, Defence = s.Defence,
                    DamageMult = s.DamageMult, SpeedMult = s.SpeedMult,
                    SkinType = s.SkinType, Scale = s.Scale,
                    BulletType = s.BulletType, BulletDamageMult = s.BulletDamageMult,
                    BulletSpeedMult = s.BulletSpeedMult, BulletPierce = s.BulletPierce,
                    BulletHitCount = s.BulletHitCount, Effects = s.Effects,
                };
                PerPlant[to.Pointer] = d;
                try { ApplyOne(to, d); } catch { }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[融合] 转移植物修改失败: " + e.Message); }
        }

        /// <summary>把该植物的单株设置写进字段（幂等）。已移除"全局默认"概念。</summary>
        public static void Apply(Plant p)
        {
            if (p == null) return;
            PlantOverride ov;
            if (PerPlant.TryGetValue(p.Pointer, out ov)) ApplyOne(p, ov);
        }

        private static void ApplyOne(Plant p, PlantOverride o)
        {
            if (o == null || !o.HasAnything) return;

            try
            {
                // 换植物 / 融合结果：直接改 thePlantType 并刷新外观
                if (o.ThePlantType >= 0)
                {
                    try
                    {
                        if ((int)p.thePlantType != o.ThePlantType)
                        {
                            p.thePlantType = (PlantType)o.ThePlantType;
                            try { p.ReplaceSprite(); } catch { }
                        }
                    }
                    catch (Exception e) { Plugin.LogOnce("换植物类型", e); }
                }

                if (o.Invincible)    p.invincible = true;
                if (o.Undead)        p.undead = true;
                if (o.KeepShooting)  p.keepShooting = true;
                if (o.AlwaysLightUp) p.alwaysLightUp = true;
                if (o.Uncrashable)   p.uncrashable = true;

                if (o.MaxHealth >= 0)
                {
                    p.thePlantMaxHealth = o.MaxHealth;
                    if (o.GodMode || p.thePlantHealth > o.MaxHealth) p.thePlantHealth = o.MaxHealth;
                }
                if (o.Health >= 0) p.thePlantHealth = o.Health;
                if (o.AttackDamage >= 0) p.attackDamage = o.AttackDamage;
                if (o.Level >= 0)        p.theLevel = o.Level;
                if (o.Stage >= 0)        p.thePlantStage = o.Stage;
                if (o.Defence >= 0f)     p.defence = o.Defence;

                if (o.AttackInterval >= 0f) p.thePlantAttackInterval = o.AttackInterval;
                else if (Math.Abs(o.SpeedMult - 1f) > 0.0001f)
                {
                    IntPtr key = p.Pointer;
                    if (!BaseInterval.TryGetValue(key, out float baseInterval) || baseInterval <= 0f)
                    {
                        baseInterval = p.thePlantAttackInterval;
                        if (baseInterval <= 0f) baseInterval = -1f;   // 该植物没有攻击间隔
                        BaseInterval[key] = baseInterval;
                    }
                    if (baseInterval > 0f) p.thePlantAttackInterval = baseInterval / o.SpeedMult;
                }

                if (o.SkinType >= 0 && p.skinType != o.SkinType)
                {
                    p.skinType = o.SkinType;
                    try { p.ReplaceSprite(); } catch { }
                }
                if (o.Scale >= 0f)
                {
                    Vector3 s = p.transform.localScale;
                    if (Math.Abs(s.x - o.Scale) > 0.001f)
                        p.transform.localScale = new Vector3(o.Scale, o.Scale, s.z);
                }

                if (o.GodMode && p.thePlantHealth < p.thePlantMaxHealth)
                    p.thePlantHealth = p.thePlantMaxHealth;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[应用植物修改] " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>子弹生成后应用子弹相关覆盖</summary>
        public static void ApplyBullet(Bullet b)
        {
            if (b == null) return;
            try
            {
                // 每颗子弹只处理一次，防止 InitData 被重复调用导致伤害指数叠加
                if (!BulletDone.Add(b.Pointer)) return;

                Plant from = null;
                try { from = b.from; } catch { }

                // 子弹覆盖改成只认单株设置
                PlantOverride o = null;
                if (from != null && PerPlant.TryGetValue(from.Pointer, out var per)) o = per;
                if (o == null || !o.HasAnything) return;

                if (o.BulletType >= 0) b.theBulletType = (BulletType)o.BulletType;

                if (Math.Abs(o.BulletDamageMult - 1f) > 0.0001f)
                {
                    int d = b.Damage;
                    long v = (long)(d * (double)o.BulletDamageMult);
                    b.Damage = v > 1_000_000_000L ? 1_000_000_000 : (int)v;
                }
                if (Math.Abs(o.BulletSpeedMult - 1f) > 0.0001f)
                    b.moveSpeed *= o.BulletSpeedMult;

                if (o.BulletPierce >= 0)  b.maxHitCount = o.BulletPierce;
                if (o.BulletHitCount >= 0) b.hitCount = o.BulletHitCount;

                // 该植物的伤害倍率顺带作用在子弹上
                if (Math.Abs(o.DamageMult - 1f) > 0.0001f)
                {
                    int d = b.Damage;
                    long v = (long)(d * (double)o.DamageMult);
                    b.Damage = v > 1_000_000_000L ? 1_000_000_000 : (int)v;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[应用子弹修改] " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
