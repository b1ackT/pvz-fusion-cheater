using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PvzRhCheat
{
    /// <summary>
    /// 单只僵尸的修改项。全部按"绝对值"语义：用户看到的是僵尸当前的真实数值，
    /// 改多少写多少；没动过的项保持 -1 / false 表示"不干预"。
    /// </summary>
    public class ZombieOverride
    {
        public long Health = -1L;
        public long MaxHealth = -1L;
        public float Speed = -1f;
        public float OriginSpeed = -1f;
        public float TakeDmgMult = -1f;
        public float ArmorValue = -1f;
        public int AttackDamage = -1;
        public int Level = -1;
        public int Row = -1;
        public int Armor1 = -1;
        public int Armor1Max = -1;
        public int Armor2 = -1;
        public int FreezeLevel = -1;
        public int PoisonLevel = -1;

        // 行为开关
        public bool GodMode;        // 周期回满血
        public bool StopMoving;     // 速度归零
        public bool KeepFrozen;     // 持续冻结
        public bool MindControl;    // 魅惑（变友军）

        public bool HasAnything
        {
            get
            {
                return GodMode || StopMoving || KeepFrozen || MindControl ||
                       Health >= 0L || MaxHealth >= 0L ||
                       Speed >= 0f || OriginSpeed >= 0f || ArmorValue >= 0f ||
                       Math.Abs(TakeDmgMult - (-1f)) > 0.0001f && TakeDmgMult >= 0f ||
                       AttackDamage >= 0 || Level >= 0 || Row >= 0 ||
                       Armor1 >= 0 || Armor1Max >= 0 || Armor2 >= 0 ||
                       FreezeLevel >= 0 || PoisonLevel >= 0;
            }
        }

        public void Reset()
        {
            Health = MaxHealth = -1L;
            Speed = OriginSpeed = ArmorValue = -1f;
            TakeDmgMult = -1f;
            AttackDamage = Level = Row = -1;
            Armor1 = Armor1Max = Armor2 = -1;
            FreezeLevel = PoisonLevel = -1;
            GodMode = StopMoving = KeepFrozen = MindControl = false;
        }
    }

    /// <summary>每只僵尸的覆盖 + 施加 + 原始值缓存</summary>
    internal static class ZombieOverrides
    {
        private static readonly Dictionary<IntPtr, ZombieOverride> Per =
            new Dictionary<IntPtr, ZombieOverride>();
        private static readonly Dictionary<IntPtr, long> BaseHp =
            new Dictionary<IntPtr, long>();
        private static readonly Dictionary<IntPtr, float> BaseSpeed =
            new Dictionary<IntPtr, float>();

        internal static ZombieOverride GetOrCreate(Zombie z)
        {
            if (z == null) return null;
            IntPtr k = z.Pointer;
            ZombieOverride o;
            if (!Per.TryGetValue(k, out o)) { o = new ZombieOverride(); Per[k] = o; }
            return o;
        }

        internal static ZombieOverride Get(Zombie z)
        {
            if (z == null) return null;
            ZombieOverride o;
            return Per.TryGetValue(z.Pointer, out o) ? o : null;
        }

        internal static void Clear(Zombie z)
        {
            if (z == null) return;
            ZombieOverride o;
            if (Per.TryGetValue(z.Pointer, out o) && o != null && o.KeepFrozen)
                try { if (z.freezeLevel > 0) z.freezeLevel = 0; } catch { }   // 还原时要解冻
            Per.Remove(z.Pointer);
            BaseHp.Remove(z.Pointer);
            BaseSpeed.Remove(z.Pointer);
        }

        internal static void ClearAll() { Per.Clear(); BaseHp.Clear(); BaseSpeed.Clear(); }

        internal static void Apply(Zombie z)
        {
            if (z == null) return;
            ZombieOverride o;
            if (!Per.TryGetValue(z.Pointer, out o) || o == null) return;
            try { ApplyOne(z, o); }
            catch (Exception e) { Plugin.LogOnce("僵尸修改", e); }
        }

        private static void ApplyOne(Zombie z, ZombieOverride o)
        {
            IntPtr key = z.Pointer;

            if (o.MaxHealth >= 0L)
            {
                if (!BaseHp.ContainsKey(key) && z.theMaxHealth > 0L) BaseHp[key] = z.theMaxHealth;
                if (z.theMaxHealth != o.MaxHealth)
                {
                    double ratio = z.theMaxHealth > 0L ? (double)z.theHealth / z.theMaxHealth : 1.0;
                    z.theMaxHealth = o.MaxHealth;
                    z.theHealth = (long)(o.MaxHealth * ratio);
                }
            }
            if (o.Health >= 0L) z.theHealth = o.Health;

            if (o.Speed >= 0f) { if (Math.Abs(z.theSpeed - o.Speed) > 0.0001f) z.theSpeed = o.Speed; }
            if (o.OriginSpeed >= 0f) { if (Math.Abs(z.theOriginSpeed - o.OriginSpeed) > 0.0001f) z.theOriginSpeed = o.OriginSpeed; }
            if (o.ArmorValue >= 0f) z.theArmor = o.ArmorValue;
            if (o.TakeDmgMult >= 0f) z.takeDmgMultiplier = o.TakeDmgMult;
            if (o.AttackDamage >= 0) z.theAttackDamage = o.AttackDamage;
            if (o.Level >= 0) z.level = o.Level;
            if (o.Row >= 0) z.theZombieRow = o.Row;
            if (o.Armor1 >= 0) z.theFirstArmorHealth = o.Armor1;
            if (o.Armor1Max >= 0) z.theFirstArmorMaxHealth = o.Armor1Max;
            if (o.Armor2 >= 0) z.theSecondArmorHealth = o.Armor2;
            if (o.FreezeLevel >= 0) z.freezeLevel = o.FreezeLevel;
            if (o.PoisonLevel >= 0) z.poisonLevel = o.PoisonLevel;

            if (o.MindControl && !z.isMindControlled) z.SetMindControl(0);
            // SetFreeze 在这套构建里是空操作，必须同时直接写 freezeLevel（见 ZombieDb.Freeze）
            if (o.KeepFrozen) { try { z.SetFreeze(30f, 3); } catch { } try { if (z.freezeLevel < 3) z.freezeLevel = 3; } catch { } }
            if (o.StopMoving) z.theSpeed = 0f;
            if (o.GodMode && z.theHealth < z.theMaxHealth) z.theHealth = z.theMaxHealth;
        }
    }
}
