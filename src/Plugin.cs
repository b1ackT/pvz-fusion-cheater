using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace PvzRhCheat
{
    [BepInPlugin(Guid, "PvZ 融合版 3.9 修改器", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.dsh.pvzrh.cheat";
        /// <summary>界面标题栏上显示的版本戳，用来确认游戏里跑的是不是最新构建</summary>
        public const string Version = "1.3.0";
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
                typeof(Patch_Mouse_Update),       // 鼠标压在菜单上时不响应游戏点击
                typeof(Patch_BoardSpawner_SummonZombies), // 停止出怪
                typeof(Patch_Lawnf_GetGloveCD),           // 手套无冷却（独立路径）
            };
            foreach (Type t in patches) TryPatch(harmony, t);

            CreateOverlay();
            IpcBridge.Start();
            TryLaunchUi();

            Log.LogInfo("PvZ 融合版修改器已加载（" + Version + "）—— 界面在游戏内；按 " + ModConfig.MenuKeyCode()
                      + " 显示/隐藏菜单，" + ModConfig.EspKeyCode() + " 开关 ESP 方框（热键可在「设置」页自定义）");
        }

        /// <summary>进游戏自动拉起独立窗口（已在运行则不重复启动）</summary>
        private void TryLaunchUi()
        {
            if (!ModConfig.AutoLaunchUi.Value) { Log.LogInfo("[UI] 自动启动已关闭"); return; }
            try
            {
                string root = Paths.GameRootPath;
                string[] cands =
                {
                    Path.Combine(root, "_mod", "ui", "bin", "Release", "net6.0-windows", "PvzRhCheatUi.exe"),
                    Path.Combine(root, "_mod", "ui", "bin", "Release", "PvzRhCheatUi.exe"),
                    Path.Combine(root, "_mod", "ui", "PvzRhCheatUi.exe"),
                };
                string exe = null;
                for (int i = 0; i < cands.Length; i++) if (File.Exists(cands[i])) { exe = cands[i]; break; }
                if (exe == null) { Log.LogWarning("[UI] 找不到 PvzRhCheatUi.exe，未自动启动"); return; }

                var running = System.Diagnostics.Process.GetProcessesByName("PvzRhCheatUi");
                if (running != null && running.Length > 0) { Log.LogInfo("[UI] 独立窗口已在运行，跳过启动"); return; }

                var psi = new System.Diagnostics.ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                };
                System.Diagnostics.Process.Start(psi);
                Log.LogInfo("[UI] 已自动启动独立窗口");
            }
            catch (Exception e)
            {
                Log.LogWarning("[UI] 自动启动失败: " + e.GetType().Name + " " + e.Message);
            }
        }

        private static readonly System.Collections.Generic.HashSet<string> _once =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>同一错误只记一次，避免每帧刷屏</summary>
        public static void LogOnce(string tag, Exception e)
        {
            string k = tag + "|" + e.GetType().Name + "|" + e.Message;
            if (_once.Add(k)) Log?.LogWarning("[" + tag + "] " + e.GetType().Name + ": " + e.Message);
        }

        /// <summary>注入并挂载 IMGUI 叠加层（ESP 方框 + 内置菜单）</summary>
        private void CreateOverlay()
        {
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<EspOverlay>();
                ClassInjector.RegisterTypeInIl2Cpp<MenuOverlay>();
                var go = new GameObject("PvzRhCheatOverlay");
                GameObject.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<EspOverlay>();
                go.AddComponent<MenuOverlay>();
                Log.LogInfo("[UI] 叠加层已创建（ESP + 内置菜单）");
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
