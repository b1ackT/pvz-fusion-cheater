using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace PvzRhCheat
{
    /// <summary>
    /// 给**独立 WinForms 窗口**（PvzRhCheatUi.exe）用的本地 IPC 桥。
    ///
    /// 为什么必须独立进程：BepInEx 自带的 .NET 6 运行时只有 Microsoft.NETCore.App，
    /// 不含 WindowsDesktop（WinForms），所以 UI 只能在游戏进程外自己跑一个，用 TCP 通信。
    ///
    /// 线程模型：Unity API 只能在主线程碰。
    ///   - 主线程每帧调用 PushSnapshot() 生成状态快照（字符串）
    ///   - IPC 线程只做：回快照 / 收指令入队
    ///   - 主线程 DrainCommands() 排空队列并执行
    /// </summary>
    internal static class IpcBridge
    {
        public const int Port = 27183;

        private static TcpListener _listener;
        private static Thread _thread;
        private static volatile bool _running;

        private static volatile string _snapshot = "{}";
        private static volatile string _typesJson = "[]";
        private static bool _typesBuilt;

        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private static readonly List<Client> _clients = new List<Client>();

        private sealed class Client
        {
            public TcpClient Tcp;
            public NetworkStream Stream;
            public readonly byte[] Buf = new byte[8192];
        }

        // ---------------------------------------------------------------- 生命周期
        public static void Start()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Start();
                _running = true;
                _thread = new Thread(Loop) { IsBackground = true, Name = "PvzRhCheatIpc" };
                _thread.Start();
                Plugin.Log.LogInfo("[IPC] 监听 127.0.0.1:" + Port + "（等待外置 UI 连接）");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("[IPC] 启动失败: " + e.Message);
            }
        }

        public static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
        }

        private static void Loop()
        {
            while (_running)
            {
                try
                {
                    if (_listener.Pending())
                    {
                        TcpClient tcp = _listener.AcceptTcpClient();
                        tcp.NoDelay = true;
                        lock (_clients) _clients.Add(new Client { Tcp = tcp, Stream = tcp.GetStream() });
                        Plugin.Log.LogInfo("[IPC] 外置 UI 已连接");
                    }
                    Pump();
                }
                catch (Exception) { if (!_running) return; }
                Thread.Sleep(10);
            }
        }

        private static void Pump()
        {
            List<Client> dead = null;
            lock (_clients)
            {
                for (int i = _clients.Count - 1; i >= 0; i--)
                {
                    Client c = _clients[i];
                    try
                    {
                        if (c.Tcp.Client == null || !c.Tcp.Connected) throw new Exception("closed");
                        while (c.Stream.DataAvailable)
                        {
                            int n = c.Stream.Read(c.Buf, 0, c.Buf.Length);
                            if (n <= 0) throw new Exception("eof");
                            string text = Encoding.UTF8.GetString(c.Buf, 0, n);
                            foreach (string raw in text.Split('\n'))
                            {
                                string cmd = raw.Trim();
                                if (cmd.Length == 0) continue;
                                if (cmd == "STATE") { Send(c, _snapshot); continue; }
                                if (cmd == "TYPES") { Send(c, _typesJson); continue; }
                                if (cmd == "PING") { Send(c, "{\"pong\":1}"); continue; }
                                _queue.Enqueue(cmd);     // 其余指令不回包，避免与 STATE 回复错位
                            }
                        }
                    }
                    catch { (dead ??= new List<Client>()).Add(c); }
                }
                if (dead != null)
                {
                    foreach (Client c in dead) { try { c.Tcp.Close(); } catch { } _clients.Remove(c); }
                    Plugin.Log.LogInfo("[IPC] 外置 UI 已断开");
                }
            }
        }

        private static void Send(Client c, string s)
        {
            if (string.IsNullOrEmpty(s)) s = "{}";
            byte[] d = Encoding.UTF8.GetBytes(s + "\n");
            try { c.Stream.Write(d, 0, d.Length); } catch { }
        }

        // ---------------------------------------------------------------- 主线程
        /// <summary>主线程每帧调用：先执行外置 UI 的指令，再刷新快照</summary>
        public static void Tick()
        {
            Drain();
            BuildTypes();
            BuildSnapshot();
        }

        private static void Drain()
        {
            int guard = 0;
            while (_queue.TryDequeue(out string cmd) && guard++ < 400)
            {
                try { Apply(cmd); }
                catch (Exception e) { Plugin.Log.LogWarning("[IPC] 指令失败 <" + cmd + ">: " + e.Message); }
            }
        }

        private static int I(string s) { return int.Parse(s, CultureInfo.InvariantCulture); }
        private static long L(string s) { return long.Parse(s, CultureInfo.InvariantCulture); }
        private static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        private static void Apply(string cmd)
        {
            string[] p = cmd.Split('|');
            if (p.Length == 0) return;
            string head = p[0];

            switch (head)
            {
                case "CFG":
                    if (p.Length < 3) return;
                    {
                        var e = ModConfig.ByKey(p[1]);
                        if (e == null) { Plugin.Log.LogWarning("[IPC] 未知配置 " + p[1]); return; }
                        ModConfig.SetFromString(e, p[2]);
                        if (p[1] == "BuffWhitelist" || p[1] == "UltiBuffWhitelist") Actions.InvalidateBuffCache();
                    }
                    return;

                case "ESP":
                    if (p.Length < 3) return;
                    bool on = p[2] == "1";
                    switch (p[1])
                    {
                        case "ShowEsp": EspOverlay.ShowEsp = on; break;
                        case "ShowName": EspOverlay.ShowName = on; break;
                        case "ShowHp": EspOverlay.ShowHp = on; break;
                        case "ShowIndex": EspOverlay.ShowIndex = on; break;
                        case "Height": EspOverlay.EspHeight = F(p[2]); break;
                        case "Width": EspOverlay.EspWidth = F(p[2]); break;
                        case "GlobalEnabled": Overrides.GlobalEnabled = on; break;
                    }
                    return;

                case "GLOBAL":
                    // 已移除"全局植物"概念，保留空实现避免旧 UI 报错
                    return;

                case "ACTION":
                    if (p.Length < 2) return;
                    string msg = "";
                    switch (p[1])
                    {
                        case "KillAllZombies": msg = Actions.ActionKillAllZombies(); break;
                        case "NextWave": msg = Actions.ActionNextWave(); break;
                        case "TriggerMowers": msg = Actions.ActionTriggerMowers(); break;
                        case "SetSun": msg = p.Length > 2 ? Actions.ActionSetSun(I(p[2])) : "缺少参数"; break;
                        case "Selected": msg = Actions.SelectedIndex() >= 0 ? ("选中 " + PlantDb.Label(Actions.SelectedPlant())) : "没有选中植物"; break;
                        case "Plants": msg = Actions.PlantsSnapshot().Count + " 只，来源=" + Actions.PlantSource(); break;
                        case "Plant":
                            {
                                // ACTION|Plant|<类型号>|<列>|<行> —— 自检用：让游戏自己在某格放一株植物
                                if (p.Length < 5) { msg = "用法: ACTION|Plant|类型|列|行"; break; }
                                msg = PlantDb.Spawn(I(p[2]), I(p[3]), I(p[4]));
                            }
                            break;
                        case "TravelNext": msg = Actions.ActionTravelNextRound(); break;
                        case "EnterGame":
                            msg = p.Length > 3 ? Actions.ActionEnterGame(I(p[2]), I(p[3]))
                                               : "用法: ACTION|EnterGame|关卡类型|第几关";
                            break;
                        case "KillAllPlants": msg = Actions.ActionKillAllPlants(); break;
                        case "HealPlants": msg = Actions.ActionHealAllPlants(); break;
                        case "UpgradePlants": msg = Actions.ActionUpgradeAllPlants(p.Length > 2 ? I(p[2]) : 10); break;
                        case "MindAll": msg = Actions.ActionMindControlAll(); break;
                        case "ZombieHp": msg = p.Length > 2 ? Actions.ActionSetAllZombieHp(F(p[2])) : "缺少倍率"; break;
                        case "ChangeAllPlants": msg = p.Length > 2 ? Actions.ActionChangeAllPlants(I(p[2])) : "缺少类型"; break;
                        case "ChangeAllZombies": msg = p.Length > 2 ? Actions.ActionChangeAllZombies(I(p[2])) : "缺少类型"; break;
                        case "SpawnZombie":
                            msg = p.Length > 3
                                ? Actions.ActionSpawnZombie(I(p[2]), I(p[3]), p.Length > 4 ? F(p[4]) : 9.9f, p.Length > 5 && p[5] == "1")
                                : "用法: ACTION|SpawnZombie|行|类型|X|是否魅惑";
                            break;
                        case "Mower": msg = p.Length > 3 ? Actions.ActionSpawnMower(I(p[2]), I(p[3])) : "用法: ACTION|Mower|行|类型"; break;
                        case "ExportLineup": msg = Actions.ActionExportLineup(); break;
                        case "ImportLineup": msg = p.Length > 2 ? Actions.ActionImportLineup(p[2]) : "缺少阵容码"; break;
                        case "Zombies": msg = "场上僵尸 " + Actions.ZombieCount() + " 只"; break;
                        case "Cards": msg = Actions.ActionCardInfo(); break;
                        case "ZSel":
                            if (p.Length > 2) { Actions.SelectZombie(Actions.ZombieByPtr(L(p[2]))); msg = "选中僵尸 " + p[2]; }
                            else msg = "缺少指针";
                            break;
                        case "ZFreeze": { Zombie z = Actions.SelectedZombie(); if (z == null) msg = "没有选中僵尸"; else { z.SetFreeze(30f, 3); msg = "已冻结"; } } break;
                        case "ZKill": { Zombie z = Actions.SelectedZombie(); if (z == null) msg = "没有选中僵尸"; else { z.Die(0); msg = "已秒杀"; } } break;
                        case "ZMind":
                            {
                                Zombie z = Actions.SelectedZombie();
                                if (z == null) { msg = "没有选中僵尸"; break; }
                                int lv = p.Length > 2 ? I(p[2]) : 0;
                                z.SetMindControl(lv);
                                msg = "SetMindControl(" + lv + ") 之后 isMindControlled=" + z.isMindControlled;
                            }
                            break;
                        case "ZSet":
                            {
                                Zombie z = Actions.SelectedZombie();
                                if (z == null) { msg = "没有选中僵尸"; break; }
                                if (p.Length < 4) { msg = "用法: ACTION|ZSet|字段下标|值"; break; }
                                int fi = I(p[2]);
                                string e = ZombieDb.SetValue(z, fi, p[3]);
                                msg = e == null ? ("僵尸·" + ZombieDb.FieldName(fi) + " = " + p[3]) : e;
                            }
                            break;
                        case "ZInfo":
                            {
                                Zombie z = Actions.SelectedZombie();
                                if (z == null) { msg = "没有选中僵尸"; break; }
                                var q = new System.Text.StringBuilder(240);
                                for (int i = 0; i < ZombieDb.FieldCount; i++)
                                    q.Append(ZombieDb.FieldName(i)).Append('=').Append(ZombieDb.GetLive(z, i)).Append("  ");
                                for (int i = 0; i < ZombieDb.FlagCount; i++)
                                    q.Append(ZombieDb.FlagName(i)).Append('=').Append(ZombieDb.GetFlag(z, i)).Append("  ");
                                msg = ZombieDb.Label(z) + " | " + q;
                                Plugin.Log.LogInfo("[僵尸] " + msg);
                            }
                            break;
                        case "Tools": msg = Tools.Info(); break;
                        case "SpeedInfo": msg = Actions.SpeedInfo(); break;
                        case "Mark": Actions.SelectByPtr(L(p.Length > 2 ? p[2] : "0")); MenuUI.MarkToggleSelected(); msg = "多选数 = " + MenuUI.MarkCount; break;
                        case "MarkAll": MenuUI.MarkAll(); msg = "多选数 = " + MenuUI.MarkCount; break;
                        case "MarkClear": MenuUI.MarkClear(); msg = "多选数 = " + MenuUI.MarkCount; break;
                        case "MarkCount": msg = "多选数 = " + MenuUI.MarkCount; break;
                        case "BSet":
                            msg = p.Length > 3 ? MenuUI.BatchSet(I(p[2]), p[3]) : "用法: ACTION|BSet|字段下标|值";
                            break;
                        case "RowPlant":
                            msg = p.Length > 4 ? Actions.ActionPlayerPlant(I(p[2]), I(p[3]), I(p[4]))
                                               : "用法: ACTION|RowPlant|类型|列|行";
                            break;
                        case "Speed":
                            if (p.Length > 2) { ModConfig.GameSpeed.Value = F(p[2]); msg = "游戏速度 = " + ModConfig.GameSpeed.Value; }
                            else msg = "缺少倍率";
                            break;
                        case "Fuse":
                            {
                                // ACTION|Fuse|<伙伴类型号>  —— 让当前选中的植物和该伙伴直接融合
                                if (p.Length < 3) { msg = "缺少伙伴类型号"; break; }
                                Plant sp = Actions.SelectedIndex() >= 0 ? Actions.SelectedPlant() : null;
                                if (sp == null) { msg = "没有选中植物"; break; }
                                long ptr = 0L;
                                try { ptr = sp.Pointer.ToInt64(); } catch { }
                                Actions.RequestFuse(ptr, I(p[2]));
                                msg = "已请求融合 " + PlantDb.Label(sp) + " + " + PlantDb.CnName(I(p[2]));
                            }
                            break;
                        case "FusionTest":
                            {
                                int t = p.Length > 2 ? I(p[2]) : -1;
                                if (t < 0)
                                {
                                    Plant sp = Actions.SelectedIndex() >= 0 ? Actions.SelectedPlant() : null;
                                    if (sp == null) { msg = "没有选中植物，也没有给类型号"; break; }
                                    try { t = (int)sp.thePlantType; } catch { t = -1; }
                                }
                                msg = t < 0 ? "类型无效" : PlantDb.FusionSelfTest(t);
                            }
                            break;
                        default: msg = "未知动作 " + p[1]; break;
                    }
                    Plugin.Log.LogInfo("[IPC] 动作 " + p[1] + " -> " + msg);
                    return;

                case "SELECTPTR":
                    if (p.Length < 2) return;
                    Actions.SelectByPtr(L(p[1]));
                    return;

                case "APPLY":
                    // APPLY|<ptr>|<机制开关 kv>|<数值 kv>
                    if (p.Length < 2) return;
                    {
                        Plant pl = Actions.PlantByPtr(L(p[1]));
                        if (pl == null) { Plugin.Log.LogWarning("[IPC] APPLY 找不到植物 ptr=" + p[1]); return; }
                        var ov = Overrides.GetOrCreate(pl);
                        if (p.Length > 2) ApplyFields(ov, p[2]);
                        if (p.Length > 3) ApplyFields(ov, p[3]);
                        Plugin.Log.LogInfo("[IPC] 已应用植物修改 ptr=" + p[1]);
                    }
                    return;

                case "RESETPTR":
                    if (p.Length < 2) return;
                    Overrides.Get(Actions.PlantByPtr(L(p[1])))?.Reset();
                    return;

                // 兼容旧版 UI（按索引）
                case "PLANT":
                    if (p.Length < 3) return;
                    {
                        Plant pl = Actions.PlantAt(I(p[1]));
                        if (pl == null) return;
                        ApplyFields(Overrides.GetOrCreate(pl), p[2]);
                    }
                    return;

                case "RESETPLANT":
                    if (p.Length < 2) return;
                    Overrides.Get(Actions.PlantAt(I(p[1])))?.Reset();
                    return;

                case "SELECT":
                    if (p.Length < 2) return;
                    Actions.SelectByIndex(I(p[1]));
                    return;

                case "CLEARPLANTS":
                    Overrides.ClearAll();
                    return;

                case "RELOADRECIPES":
                    Actions.InvalidateRecipes();
                    return;
            }
            Plugin.Log.LogWarning("[IPC] 未知指令 " + head);
        }

        /// <summary>批量字段："Key=Value;Key=Value"（内置菜单也复用这套字段映射）</summary>
        internal static void ApplyFields(PlantOverride o, string kv)
        {
            if (o == null || string.IsNullOrEmpty(kv)) return;
            foreach (string pair in kv.Split(';'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string k = pair.Substring(0, eq), v = pair.Substring(eq + 1);
                switch (k)
                {
                    case "GodMode": o.GodMode = v == "1"; break;
                    case "Invincible": o.Invincible = v == "1"; break;
                    case "Undead": o.Undead = v == "1"; break;
                    case "KeepShooting": o.KeepShooting = v == "1"; break;
                    case "AlwaysLightUp": o.AlwaysLightUp = v == "1"; break;
                    case "Uncrashable": o.Uncrashable = v == "1"; break;
                    case "ThePlantType": o.ThePlantType = I(v); break;
                    case "MaxHealth": o.MaxHealth = I(v); break;
                    case "Health": o.Health = I(v); break;
                    case "AttackDamage": o.AttackDamage = I(v); break;
                    case "Level": o.Level = I(v); break;
                    case "Stage": o.Stage = I(v); break;
                    case "AttackInterval": o.AttackInterval = F(v); break;
                    case "Defence": o.Defence = F(v); break;
                    case "DamageMult": o.DamageMult = F(v); break;
                    case "SpeedMult": o.SpeedMult = F(v); break;
                    case "SkinType": o.SkinType = I(v); break;
                    case "Scale": o.Scale = F(v); break;
                    case "BulletType": o.BulletType = I(v); break;
                    case "BulletDamageMult": o.BulletDamageMult = F(v); break;
                    case "BulletSpeedMult": o.BulletSpeedMult = F(v); break;
                    case "BulletPierce": o.BulletPierce = I(v); break;
                    case "Effects": o.Effects = v; break;
                    default: Plugin.Log.LogWarning("[IPC] 未知字段 " + k); break;
                }
            }
        }

        // ---------------------------------------------------------------- 快照
        private static void BuildTypes()
        {
            if (_typesBuilt) return;
            _typesBuilt = true;
            try
            {
                var sb = new StringBuilder(16384);
                sb.Append('[');
                bool first = true;
                foreach (object o in Enum.GetValues(typeof(PlantType)))
                {
                    int v = Convert.ToInt32(o);
                    if (v < 0) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":").Append(v)
                      .Append(",\"name\":\"").Append(Esc(o.ToString())).Append('"')
                      .Append(",\"cn\":\"").Append(Esc(CnNameOf(v))).Append('"').Append('}');
                }
                sb.Append(']');
                _typesJson = sb.ToString();
                Plugin.Log.LogInfo("[IPC] PlantType 表已生成");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[IPC] PlantType 枚举失败: " + e.Message);
                _typesJson = "[]";
            }
        }

        private static void BuildSnapshot()
        {
            var sb = new StringBuilder(8192);
            sb.Append('{');
            sb.Append("\"level\":\"").Append(Esc(Actions.LevelInfo())).Append('"');
            sb.Append(",\"source\":\"").Append(Esc(Actions.PlantSource())).Append('"');
            sb.Append(",\"inLevel\":").Append(Actions.IsInLevel() ? 1 : 0);
            sb.Append(",\"garden\":").Append(Actions.GardenCount());
            sb.Append(",\"selPtr\":").Append(Actions.SelectedPtr());
            sb.Append(",\"selected\":").Append(Actions.SelectedIndex());
            sb.Append(",\"count\":").Append(Actions.PlantsSnapshot().Count);
            sb.Append(",\"zcount\":").Append(Actions.ZombiesSnapshot().Count);
            sb.Append(",\"zselPtr\":").Append(Actions.ZombieSelPtr());
            sb.Append(",\"zombies\":").Append(ZombiesJson());
            sb.Append(",\"srcLawnf\":").Append(Actions.SrcLawnf());
            sb.Append(",\"srcBoard\":").Append(Actions.SrcBoard());
            sb.Append(",\"srcFind\":").Append(Actions.SrcFind());
            sb.Append(",\"gameCount\":").Append(Actions.GamePlantCount());

            // cfg
            sb.Append(",\"cfg\":{");
            bool first = true;
            foreach (var kv in ModConfig.All())
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key).Append("\":\"").Append(Esc(ModConfig.GetString(kv.Value))).Append('"');
            }
            sb.Append('}');

            // esp
            sb.Append(",\"esp\":{\"ShowEsp\":").Append(EspOverlay.ShowEsp ? 1 : 0)
              .Append(",\"ShowName\":").Append(EspOverlay.ShowName ? 1 : 0)
              .Append(",\"ShowHp\":").Append(EspOverlay.ShowHp ? 1 : 0)
              .Append(",\"ShowIndex\":").Append(EspOverlay.ShowIndex ? 1 : 0)
              .Append(",\"Height\":\"").Append(N(EspOverlay.EspHeight)).Append('"')
              .Append(",\"Width\":\"").Append(N(EspOverlay.EspWidth)).Append('"')
              .Append(",\"GlobalEnabled\":").Append(Overrides.GlobalEnabled ? 1 : 0)
              .Append('}');

            sb.Append(",\"global\":").Append(Ov(Overrides.Global));

            // plants
            var plants = Actions.PlantsSnapshot();
            sb.Append(",\"plants\":[");            for (int i = 0; i < plants.Count; i++)
            {
                Plant p = plants[i];
                if (i > 0) sb.Append(',');
                string type = "?"; int hp = 0, mx = 0, atk = 0, row = 0, col = 0, lv = 0, skin = 0;
                float itv = 0f, def = 0f;
                try
                {
                    type = p.thePlantType.ToString();
                    hp = p.thePlantHealth; mx = p.thePlantMaxHealth; atk = p.attackDamage;
                    itv = p.thePlantAttackInterval; def = p.defence;
                    row = p.thePlantRow; col = p.thePlantColumn; lv = p.theLevel; skin = p.skinType;
                }
                catch { }

                sb.Append("{\"i\":").Append(i)
                  .Append(",\"ptr\":").Append(SafePtr(p))
                  .Append(",\"type\":\"").Append(Esc(type)).Append('"')
                  .Append(",\"name\":\"").Append(Esc(CnName(p))).Append('"')
                  .Append(",\"typeId\":").Append(SafeTypeId(p))
                  .Append(",\"hp\":").Append(hp).Append(",\"maxhp\":").Append(mx)
                  .Append(",\"atk\":").Append(atk)
                  .Append(",\"itv\":\"").Append(N(itv)).Append('"')
                  .Append(",\"def\":\"").Append(N(def)).Append('"')
                  .Append(",\"lv\":").Append(lv).Append(",\"skin\":").Append(skin)
                  .Append(",\"row\":").Append(row).Append(",\"col\":").Append(col)
                  .Append(",\"sel\":").Append(Actions.IsSelected(p) ? 1 : 0)
                  .Append(",\"live\":").Append(Live(p))
                  .Append(",\"ov\":").Append(Ov(Overrides.Get(p)));

                // 只给「选中的那株」附带融合配方，避免快照过大
                if (Actions.IsSelected(p)) sb.Append(",\"recipes\":").Append(Actions.RecipesJson());
                sb.Append('}');
            }
            sb.Append("]}");
            _snapshot = sb.ToString();
        }

        /// <summary>僵尸数组（给外置脚本/自检用）</summary>
        private static string ZombiesJson()
        {
            var zs = Actions.ZombiesSnapshot();
            var sb = new StringBuilder(2048);
            sb.Append('[');
            for (int i = 0; i < zs.Count; i++)
            {
                Zombie z = zs[i];
                if (z == null) continue;
                if (i > 0) sb.Append(',');
                long hp = 0, mx = 0; int row = 0, typeId = -1, a1 = 0, lv = 0, frz = 0;
                float spd = 0f;
                bool mind = false;
                try
                {
                    hp = z.theHealth; mx = z.theMaxHealth; row = z.theZombieRow;
                    typeId = (int)z.theZombieType; a1 = z.theFirstArmorHealth;
                    lv = z.level; frz = z.freezeLevel; spd = z.theSpeed; mind = z.isMindControlled;
                }
                catch { }
                long ptr = 0L;
                try { ptr = z.Pointer.ToInt64(); } catch { }
                sb.Append("{\"i\":").Append(i)
                  .Append(",\"ptr\":").Append(ptr)
                  .Append(",\"name\":\"").Append(Esc(ZombieDb.CnName(typeId))).Append('"')
                  .Append(",\"typeId\":").Append(typeId)
                  .Append(",\"hp\":").Append(hp).Append(",\"maxhp\":").Append(mx)
                  .Append(",\"armor\":").Append(a1).Append(",\"lv\":").Append(lv)
                  .Append(",\"frz\":").Append(frz)
                  .Append(",\"spd\":\"").Append(N(spd)).Append('"')
                  .Append(",\"row\":").Append(row)
                  .Append(",\"mind\":").Append(mind ? 1 : 0)
                  .Append(",\"sel\":").Append(Actions.IsZombieSelected(z) ? 1 : 0)
                  .Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static int SafeTypeId(Plant p)
        {
            try { return (int)p.thePlantType; } catch { return -1; }
        }

        private static long SafePtr(Plant p)
        {
            try { return p.Pointer.ToInt64(); } catch { return 0L; }
        }

        /// <summary>中文名：Lawnf.GetName 从 LawnStrings/DetailStrings 里取</summary>
        internal static string CnName(Plant p)
        {
            try { return Lawnf.GetName(p.thePlantType) ?? ""; } catch { return ""; }
        }

        internal static string CnNameOf(int typeId)
        {
            try { return Lawnf.GetName((PlantType)typeId) ?? ""; } catch { return ""; }
        }

        /// <summary>植物当前的实时数值（编辑器用它预填）</summary>
        private static string Live(Plant p)
        {
            var sb = new StringBuilder(200);
            sb.Append('{');
            try
            {
                float scale = 1f;
                try { scale = p.transform.localScale.x; } catch { }
                sb.Append("\"MaxHealth\":").Append(p.thePlantMaxHealth)
                  .Append(",\"Health\":").Append(p.thePlantHealth)
                  .Append(",\"AttackDamage\":").Append(p.attackDamage)
                  .Append(",\"Level\":").Append(p.theLevel)
                  .Append(",\"Stage\":").Append(p.thePlantStage)
                  .Append(",\"AttackInterval\":\"").Append(N(p.thePlantAttackInterval)).Append('"')
                  .Append(",\"Defence\":\"").Append(N(p.defence)).Append('"')
                  .Append(",\"SkinType\":").Append(p.skinType)
                  .Append(",\"Scale\":\"").Append(N(scale)).Append('"');
            }
            catch (Exception e) { Plugin.LogOnce("live", e); }
            sb.Append('}');
            return sb.ToString();
        }

        private static string N(float f) { return f.ToString("0.###", CultureInfo.InvariantCulture); }

        private static string Ov(PlantOverride o)
        {
            if (o == null) return "null";
            var sb = new StringBuilder(256);
            sb.Append('{')
              .Append("\"GodMode\":").Append(o.GodMode ? 1 : 0)
              .Append(",\"Invincible\":").Append(o.Invincible ? 1 : 0)
              .Append(",\"Undead\":").Append(o.Undead ? 1 : 0)
              .Append(",\"KeepShooting\":").Append(o.KeepShooting ? 1 : 0)
              .Append(",\"AlwaysLightUp\":").Append(o.AlwaysLightUp ? 1 : 0)
              .Append(",\"Uncrashable\":").Append(o.Uncrashable ? 1 : 0)
              .Append(",\"ThePlantType\":").Append(o.ThePlantType)
              .Append(",\"MaxHealth\":").Append(o.MaxHealth)
              .Append(",\"AttackDamage\":").Append(o.AttackDamage)
              .Append(",\"Level\":").Append(o.Level)
              .Append(",\"Stage\":").Append(o.Stage)
              .Append(",\"AttackInterval\":\"").Append(N(o.AttackInterval)).Append('"')
              .Append(",\"Defence\":\"").Append(N(o.Defence)).Append('"')
              .Append(",\"DamageMult\":\"").Append(N(o.DamageMult)).Append('"')
              .Append(",\"SpeedMult\":\"").Append(N(o.SpeedMult)).Append('"')
              .Append(",\"SkinType\":").Append(o.SkinType)
              .Append(",\"Scale\":\"").Append(N(o.Scale)).Append('"')
              .Append(",\"BulletType\":").Append(o.BulletType)
              .Append(",\"BulletDamageMult\":\"").Append(N(o.BulletDamageMult)).Append('"')
              .Append(",\"BulletSpeedMult\":\"").Append(N(o.BulletSpeedMult)).Append('"')
              .Append(",\"BulletPierce\":").Append(o.BulletPierce)
              .Append(",\"Effects\":\"").Append(Esc(o.Effects)).Append('"')
              .Append('}');
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c < 32 ? ' ' : c); break;
                }
            }
            return sb.ToString();
        }

        public static bool HasClient()
        {
            lock (_clients) return _clients.Count > 0;
        }
    }
}
