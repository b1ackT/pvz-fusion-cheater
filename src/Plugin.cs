using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace PvzRhCheat
{
    [BepInPlugin(Guid, "PvZ 融合版 3.9 修改器", "1.0.0")]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.dsh.pvzrh.cheat";
        public static new ManualLogSource Log;
        public override void Load()
        {
            Log = base.Log;
            ModConfig.Init(Config);

            var harmony = new Harmony(Guid);

            // 逐个类型打补丁：某个目标失败不影响其它功能
            Type[] patches =
            {
                typeof(Patch_GameApp_Awake),      // 初始施加
                typeof(Patch_GameApp_Update),     // 周期性驱动
                typeof(Patch_Board_UseSun),       // 无限阳光
                typeof(Patch_Board_UseMoney),     // 无限金币
                typeof(Patch_Plant_TakeDamage),   // 植物无敌
                typeof(Patch_Plant_RealTakeDamage),
                typeof(Patch_Plant_DecreaseHealth),
                typeof(Patch_Zombie_TakeDamage),  // 僵尸受伤倍率
                typeof(Patch_Bullet_InitData),    // 子弹覆盖（UI 写入）
                typeof(Patch_Travel_AdvBuffPool), // 词条白名单
                typeof(Patch_Travel_UltiBuffPool),
                typeof(Patch_Advanture_OnInit),   // 天赋全解锁
                typeof(Patch_Abyss_GetTicket),    // 抽奖券拉满
                typeof(Patch_Abyss_UseTicket),    // 抽奖券不消耗
            };
            foreach (Type t in patches) TryPatch(harmony, t);

            CreateOverlay();

            Log.LogInfo("PvZ 融合版修改器已加载（1.0.0）—— INSERT 显示/隐藏菜单，F3 切 ESP");
        }

        private static readonly System.Collections.Generic.HashSet<string> _once =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>同一错误只记一次，避免每帧刷屏</summary>
        public static void LogOnce(string tag, Exception e)
        {
            string k = tag + "|" + e.GetType().Name + "|" + e.Message;
            if (_once.Add(k)) Log?.LogWarning("[" + tag + "] " + e.GetType().Name + ": " + e.Message);
        }

        /// <summary>注入并挂载 IMGUI 叠加层</summary>
        private void CreateOverlay()
        {
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<EspOverlay>();
                var go = new GameObject("PvzRhCheatOverlay");
                GameObject.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<EspOverlay>();
                Log.LogInfo("[UI] 叠加层已创建");
            }
            catch (Exception e)
            {
                Log.LogError("[UI] 创建叠加层失败: " + e.GetType().Name + " " + e.Message);
            }
        }

        private static void TryPatch(Harmony harmony, Type type)
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                Log.LogInfo("[OK]   已打补丁: " + type.Name);
            }
            catch (Exception e)
            {
                Log.LogError("[FAIL] 打补丁失败 " + type.Name + ": " + e.GetType().Name + " " + e.Message);
            }
        }
    }
}
