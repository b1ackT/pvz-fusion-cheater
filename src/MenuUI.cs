using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace PvzRhCheat
{
    /// <summary>
    /// 游戏内菜单的**全部实现**。这是个普通托管类，不是注入到 Il2Cpp 的类型，
    /// 所以可以随便用 Rect / Plant / List 之类的签名，不会触发
    /// "Method unstripping failed"。
    ///
    /// 真正的 MonoBehaviour 只有下面那个 8 行的 MenuOverlay。
    /// 所有数据都在本进程内直接读写游戏对象，不经过 IPC。
    /// </summary>
    internal static class MenuUI
    {
        // ---------------------------------------------------------------- 状态
        internal static bool Show = true;

        private static float _px = 26f, _py = 22f;
        private static bool _posInit;
        internal static Rect Panel;

        private static int _tab;
        private static readonly int[] _scroll = new int[7];
        /// <summary>本帧从滚轮拿到的滚动量（正=向下），Frame() 里只取一次</summary>
        private static int _wheel;
        private static readonly bool[] _groupOpen = GroupOpenDefault();

        /// <summary>默认**全部展开**：不然用户打开「功能开关」只看到 10 行组名，会以为开关没了。</summary>
        private static bool[] GroupOpenDefault()
        {
            var a = new bool[16];
            for (int i = 0; i < a.Length; i++) a[i] = true;
            return a;
        }
        private static readonly List<int> _filtered = new List<int>(800);

        private static bool _dragging;
        private static Vector2 _dragOff;

        // 正在编辑的东西
        private const int EK_NONE = 0, EK_PLANT = 1, EK_CONFIG = 2, EK_FILTER = 3, EK_SB = 4, EK_ZOMBIE = 5;
        private static int _editKind = EK_NONE;
        private static int _editField = -1;
        private static string _editKey;
        private static long _editPtr;
        private static string _editBuf = "";
        private static bool _editText;

        // 类型选择器
        private static bool _pickerOpen;
        private static int _pickerTarget;
        private static string _pickerFilter = "";
        private static int _pickerPage;

        // 融合自定义配方
        private static int _recipePartner = -1, _recipeResult = -1;
        /// <summary>true = 选完结果后立刻把配方写进游戏融合表（「改结果…」那条路）</summary>
        private static bool _pickerCommit;
        /// <summary>类型选择器当前是否在选僵尸（僵尸用 ZombieType 枚举）</summary>
        private static bool _pickerZombie;

        // ---------------------------------------------------------------- 沙盒状态
        private const int SB_PTYPE = 0, SB_COL = 1, SB_ROW = 2,
                          SB_ZTYPE = 3, SB_ZROW = 4, SB_ZX = 5, SB_ZMIND = 6,
                          SB_MROW = 7, SB_MTYPE = 8,
                          SB_CPTYPE = 9, SB_CZTYPE = 10;

        private static int _sbPType = 0, _sbCol = 4, _sbRow = 2;
        private static int _sbZType = 0, _sbZRow = 2;
        private static float _sbZX = 9.9f;
        private static bool _sbZMind;
        private static int _sbMRow = 2, _sbMType = 0;
        private static int _sbCPType = 0, _sbCZType = 0;
        private static string _lineup = "";
        private static int _sbEdit;            // 正在编辑哪个沙盒数值

        private static int SbGetInt(int f)
        {
            switch (f)
            {
                case SB_PTYPE: return _sbPType;
                case SB_COL: return _sbCol;
                case SB_ROW: return _sbRow;
                case SB_ZTYPE: return _sbZType;
                case SB_ZROW: return _sbZRow;
                case SB_MROW: return _sbMRow;
                case SB_MTYPE: return _sbMType;
                case SB_CPTYPE: return _sbCPType;
                case SB_CZTYPE: return _sbCZType;
            }
            return 0;
        }

        private static void SbSetInt(int f, int v)
        {
            switch (f)
            {
                case SB_PTYPE: _sbPType = v; break;
                case SB_COL: _sbCol = Mathf.Clamp(v, 0, 8); break;
                case SB_ROW: _sbRow = Mathf.Clamp(v, 0, 6); break;
                case SB_ZTYPE: _sbZType = v; break;
                case SB_ZROW: _sbZRow = Mathf.Clamp(v, 0, 6); break;
                case SB_MROW: _sbMRow = Mathf.Clamp(v, 0, 6); break;
                case SB_MTYPE: _sbMType = Mathf.Clamp(v, 0, 4); break;
                case SB_CPTYPE: _sbCPType = v; break;
                case SB_CZTYPE: _sbCZType = v; break;
            }
        }

        private static string _status = "";
        private static double _statusAt;

        private const float Pad = 8f;

        // ---------------------------------------------------------------- 配置分组
        private static readonly string[] GroupNames =
        {
            "通用", "解锁与资源", "关卡内", "战斗", "旅行 / 词条", "天赋", "深渊抽奖券",
            "经典作弊", "速度 · 僵尸 · 工具", "界面与 ESP",
        };

        private static readonly string[] GroupHints =
        {
            "总开关、重刷间隔", "关卡·图鉴·金币", "阳光不减", "无敌 / 伤害倍率", "Roguelike 词条池", "冒险天赋树",
            "抽奖券", "原版经典功能", "倍速 / 出怪 / 冷却", "字号、外置窗口",
        };

        private static readonly string[][] GroupKeys =
        {
            new[] { "Enabled", "ApplyIntervalSeconds" },
            new[] { "UnlockAllLevels", "Money", "DeveloperMode", "UnlockAllPlants" },
            new[] { "InfiniteSun", "SunFloor" },
            new[] { "GodModePlants", "PlantDamageMultiplier", "ZombieDamageTakenMultiplier", "OneHitZombies" },
            new[] { "TravelBuffs", "DamageReduction", "LuckyStrike", "DamageAmplification", "PlantZeroHealth", "BuffWhitelist", "UltiBuffWhitelist" },
            new[] { "TalentUnlockAll", "TalentStars", "DisableHardMode" },
            new[] { "AbyssMaxTickets", "AbyssInfiniteTickets" },
            new[] { "AutoCollectSun", "NoCardCooldown", "FreePlanting", "UnlimitedCardUse", "FreezeAllZombies", "ZombiesStopMoving", "AutoKillZombies", "PlantWholeLine", "PlantLineDir" },
            new[] { "GameSpeed", "StopZombieSpawn", "ZombieInvincible", "ZombieHpMultiplier", "NoToolCooldown" },
            new[] { "EspFontSize", "EspBold", "MenuFontSize", "AutoLaunchUi" },
        };

        private static readonly Dictionary<string, string> Cn = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Enabled", "总开关（关掉后所有功能停用）" },
            { "ApplyIntervalSeconds", "重刷间隔（秒，0=只应用一次）" },
            { "UnlockAllLevels", "解锁全部关卡 / 图鉴 / 卡牌" },
            { "Money", "金币数量" },
            { "DeveloperMode", "开发者模式" },
            { "InfiniteSun", "阳光不减（消耗不扣除）" },
            { "SunFloor", "阳光保底值" },
            { "GodModePlants", "植物无敌（完全免伤）" },
            { "PlantDamageMultiplier", "植物伤害倍率" },
            { "ZombieDamageTakenMultiplier", "僵尸受伤倍率" },
            { "OneHitZombies", "僵尸一击必杀" },
            { "TravelBuffs", "开启旅行词条修改" },
            { "DamageReduction", "受伤减免（0~1）" },
            { "LuckyStrike", "幸运一击率" },
            { "DamageAmplification", "伤害放大倍率" },
            { "PlantZeroHealth", "内置 plantZeroHealth 标记" },
            { "BuffWhitelist", "词条白名单（中文名，逗号分隔）" },
            { "UltiBuffWhitelist", "终极词条白名单（逗号分隔）" },
            { "TalentUnlockAll", "解锁全部天赋" },
            { "TalentStars", "天赋星级" },
            { "DisableHardMode", "关闭困难模式并清零难度" },
            { "AbyssMaxTickets", "深渊抽奖券拉满" },
            { "AbyssInfiniteTickets", "抽奖券不消耗" },
            { "EspFontSize", "ESP 字号" },
            { "EspBold", "ESP 文字加粗" },
            { "MenuFontSize", "菜单字号" },
            { "AutoLaunchUi", "启动时打开外置窗口（已不推荐）" },
            { "AutoCollectSun", "自动收集阳光与金币" },
            { "NoCardCooldown", "卡片无冷却" },
            { "FreePlanting", "种植物不花阳光" },
            { "UnlimitedCardUse", "卡片使用次数无限" },
            { "FreezeAllZombies", "持续冻结全场僵尸" },
            { "ZombiesStopMoving", "僵尸停止移动" },
            { "AutoKillZombies", "自动秒杀新出现的僵尸" },
            { "PlantWholeLine", "一种种一列（种一株铺满一整条）" },
            { "PlantLineDir", "铺满方向：col=一列(竖) / row=一排(横)" },
            { "GameSpeed", "游戏速度倍率（= 游戏自己的 GameConfig.gameSpeed + Time.timeScale）" },
            { "StopZombieSpawn", "停止出怪（关卡不再放僵尸）" },
            { "ZombieInvincible", "僵尸无敌（僵尸不再掉血）" },
            { "ZombieHpMultiplier", "僵尸血量倍率" },
            { "NoToolCooldown", "手套 / 锤子无冷却" },
            { "UnlockAllPlants", "植物图鉴 / 植物池全解锁" },
        };

        private static readonly string[] TabNames = { "功能开关", "植物", "僵尸", "融合", "沙盒", "作弊动作", "设置" };

        /// <summary>构建时间戳（编译时生成），用来一眼确认跑的是哪一版</summary>
        internal static readonly string BuildStamp =
            new DateTime(2026, 9, 13, 18, 30, 0, DateTimeKind.Local).ToString("yyyy-MM-dd HH:mm");

        // ---------------------------------------------------------------- 对外
        /// <summary>鼠标是否压在菜单上 —— 用来屏蔽游戏自己的鼠标操作</summary>
        internal static bool BlocksGameInput()
        {
            if (!Show) return false;
            try
            {
                Vector3 m = Input.mousePosition;
                Vector2 p = new Vector2(m.x, Screen.height - m.y);
                if (Panel.width <= 1f) return false;
                return Panel.Contains(p);
            }
            catch { return false; }
        }

        internal static void SetStatus(string s)
        {
            _status = s ?? "";
            _statusAt = Time.realtimeSinceStartup;
        }

        // ---------------------------------------------------------------- 主循环
        internal static void Frame()
        {
            try
            {
                Keys();
                if (!Show) return;
                if (!_posInit)
                {
                    _posInit = true;
                    Panel = new Rect(_px, _py, 840f, 620f);
                }
                Panel.width = Mathf.Min(840f, Mathf.Max(600f, Screen.width - 40f));
                Panel.height = Mathf.Min(620f, Mathf.Max(340f, Screen.height - 40f));
                Panel.x = Mathf.Clamp(Panel.x, -Panel.width + 90f, Screen.width - 90f);
                Panel.y = Mathf.Clamp(Panel.y, 0f, Mathf.Max(0f, Screen.height - 34f));

                int fs = 14;
                try { fs = Mathf.Clamp(ModConfig.MenuFontSize.Value, 10, 26); } catch { }
                UiSkin.SetFontSize(fs);

                // 滚轮只取一次：压在面板上就吃掉（免得游戏画面跟着动），
                // 再由当前页签决定用不用。原来是各页签自己调 Wheel()，
                // 既没消费事件、又是"一格只滚一行"，下滑就会觉得又慢又怪。
                _wheel = UiSkin.MouseOver(Panel) ? UiSkin.TakeWheel() : 0;

                Fill(new Rect(Panel.x, Panel.y, Panel.width, Panel.height), UiSkin.ColBack);
                UiSkin.Border(Panel, UiSkin.ColLine);

                TitleBar();
                TabBar();
                StatusBar();

                Rect cr = Content();
                try
                {
                    switch (_tab)
                    {
                        case 0: TabToggles(cr); break;
                        case 1: TabPlants(cr); break;
                        case 2: TabZombies(cr); break;
                        case 3: TabFusion(cr); break;
                        case 4: TabSandbox(cr); break;
                        case 5: TabActions(cr); break;
                        default: TabSettings(cr); break;
                    }
                }
                catch (Exception e) { Plugin.LogOnce("菜单/页" + _tab, e); }

                if (_pickerOpen) DrawPicker();
            }
            catch (Exception e) { Plugin.LogOnce("菜单", e); }
        }

        private static void Fill(Rect r, Color c) { UiSkin.Fill(r, c); }

        private static Rect Content()
        {
            return new Rect(Panel.x + Pad, Panel.y + 26f + 28f + 4f,
                            Panel.width - Pad * 2f, Panel.height - 26f - 28f - 4f - 24f);
        }

        private static float RowH { get { return Mathf.Max(18f, UiSkin.FontSize + 9f); } }

        private static void TitleBar()
        {
            Rect t = new Rect(Panel.x + 1f, Panel.y + 1f, Panel.width - 2f, 25f);
            Fill(t, UiSkin.ColTitle);

            bool esp = EspOverlay.ShowEsp;
            Rect espR = new Rect(t.xMax - 268f, t.y + 2f, 96f, 21f);
            Rect hideR = new Rect(t.xMax - 166f, t.y + 2f, 106f, 21f);
            Rect closeR = new Rect(t.xMax - 54f, t.y + 2f, 48f, 21f);

            UiSkin.Text(new Rect(t.x + 10f, t.y, espR.x - t.x - 16f, t.height),
                        "PVZ 融合版 3.9 修改器   " + Plugin.Version + "（内置界面 · 6 页签）", UiSkin.Bold);

            if (UiSkin.SmallButton(espR, esp ? "ESP: 开" : "ESP: 关", esp, true))
                EspOverlay.ShowEsp = !esp;
            if (UiSkin.SmallButton(hideR, "隐藏 (" + ModConfig.MenuKeyCode() + ")", false, true)) Show = false;
            if (UiSkin.SmallButton(closeR, "", false, true)) Show = false;
            UiSkin.Glyph(closeR, "×", UiSkin.ColRed);

            // 拖动
            Event e = Event.current;
            if (e != null)
            {
                if (e.type == EventType.MouseDown && e.button == 0 && t.Contains(e.mousePosition))
                {
                    _dragging = true;
                    _dragOff = new Vector2(e.mousePosition.x - Panel.x, e.mousePosition.y - Panel.y);
                    e.Use();
                }
                else if (e.type == EventType.MouseDrag && _dragging)
                {
                    Panel.x = Mathf.Clamp(e.mousePosition.x - _dragOff.x, -Panel.width + 80f, Screen.width - 80f);
                    Panel.y = Mathf.Clamp(e.mousePosition.y - _dragOff.y, 0f, Screen.height - 30f);
                    _px = Panel.x; _py = Panel.y;
                    e.Use();
                }
                else if (e.type == EventType.MouseUp) _dragging = false;
            }
        }

        private static void TabBar()
        {
            float y = Panel.y + 27f;
            float w = (Panel.width - Pad * 2f) / TabNames.Length;
            for (int i = 0; i < TabNames.Length; i++)
            {
                Rect r = new Rect(Panel.x + Pad + w * i, y, w - 2f, 26f);
                if (UiSkin.Button(r, TabNames[i], _tab == i, true))
                {
                    if (_tab != i) { _tab = i; _editKind = EK_NONE; }
                }
            }
            Fill(new Rect(Panel.x, y + 26f, Panel.width, 1f), UiSkin.ColLine);
        }

        private static void StatusBar()
        {
            Rect r = new Rect(Panel.x + 1f, Panel.yMax - 23f, Panel.width - 2f, 22f);
            Fill(r, UiSkin.ColTitle);
            string s = _status;
            if (string.IsNullOrEmpty(s) || Time.realtimeSinceStartup - _statusAt > 8.0)
                s = ModConfig.MenuKeyCode() + " 显示/隐藏   ·   " + ModConfig.EspKeyCode() + " 开关 ESP   ·   点植物上的方框选中它";
            UiSkin.Text(new Rect(r.x + 8f, r.y, r.width * 0.58f, r.height), s, UiSkin.Left);

            string info = "关卡 " + Actions.LevelInfo()
                        + "  |  植物 " + Actions.PlantsSnapshot().Count
                        + " (" + Actions.PlantSource() + ")"
                        + "  |  选中 " + (Actions.SelectedIndex() >= 0 ? ShortName(Actions.SelectedPlant()) : "无");
            UiSkin.Text(new Rect(r.x + r.width * 0.58f, r.y, r.width * 0.42f - 10f, r.height),
                        UiSkin.Fit(info, 40), UiSkin.Right);
        }

        private static string ShortName(Plant p)
        {
            if (p == null) return "无";
            int t = -1; try { t = (int)p.thePlantType; } catch { }
            string n = PlantDb.CnName(t);
            return string.IsNullOrEmpty(n) ? ("#" + t) : n;
        }

        // ---------------------------------------------------------------- 键盘
        private static void Keys()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;

            // 正在改键：下一个按下的键就是新热键
            if (_binding != 0) { BindKey(e); return; }

            if (_editKind != EK_NONE) { EditKey(e); return; }

            KeyCode mk = ModConfig.MenuKeyCode();
            KeyCode ek = ModConfig.EspKeyCode();
            if (e.keyCode == mk) { Show = !Show; Plugin.Log.LogInfo("[菜单] 显示/隐藏 = " + Show + "（热键 " + mk + "）"); e.Use(); }
            else if (e.keyCode == ek) { EspOverlay.ShowEsp = !EspOverlay.ShowEsp; e.Use(); }
            else if (e.keyCode == KeyCode.Escape && _pickerOpen) { _pickerOpen = false; e.Use(); }
        }

        /// <summary>改键：1 = 菜单键，2 = ESP 键</summary>
        private static int _binding;

        private static void BindKey(Event e)
        {
            if (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.None && e.character == '\0')
            {
                if (e.keyCode == KeyCode.Escape) { _binding = 0; SetStatus("已取消改键"); e.Use(); }
                return;
            }
            if (e.keyCode == KeyCode.None) return;      // 只吃有 keyCode 的按键
            int which = _binding;
            _binding = 0;
            if (which == 1)
            {
                ModConfig.SetKey(ModConfig.MenuKey, e.keyCode);
                SetStatus("菜单显示/隐藏键已改为 " + e.keyCode);
            }
            else
            {
                ModConfig.SetKey(ModConfig.EspKey, e.keyCode);
                SetStatus("ESP 开关热键已改为 " + e.keyCode);
            }
            e.Use();
        }

        private static void EditKey(Event e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { CommitEdit(); e.Use(); return; }
            if (e.keyCode == KeyCode.Escape) { _editKind = EK_NONE; SetStatus("已取消编辑"); e.Use(); return; }
            if (e.keyCode == KeyCode.Backspace)
            {
                if (_editBuf.Length > 0) _editBuf = _editBuf.Substring(0, _editBuf.Length - 1);
                e.Use();
                return;
            }
            if (e.keyCode == KeyCode.Tab) { CommitEdit(); e.Use(); return; }

            char c = e.character;
            if (c != '\0' && !char.IsControl(c))
            {
                if (_editText || char.IsDigit(c) || c == '.' || c == '-' || c == ',')
                    if (_editBuf.Length < 48) _editBuf += c;
                e.Use();
                return;
            }

            int d = -1;
            if (e.keyCode >= KeyCode.Alpha0 && e.keyCode <= KeyCode.Alpha9) d = e.keyCode - KeyCode.Alpha0;
            else if (e.keyCode >= KeyCode.Keypad0 && e.keyCode <= KeyCode.Keypad9) d = e.keyCode - KeyCode.Keypad0;
            if (d >= 0) { if (_editBuf.Length < 48) _editBuf += (char)('0' + d); e.Use(); return; }
            if (e.keyCode == KeyCode.Minus || e.keyCode == KeyCode.KeypadMinus) { _editBuf += '-'; e.Use(); return; }
            if (e.keyCode == KeyCode.Period || e.keyCode == KeyCode.KeypadPeriod) { _editBuf += '.'; e.Use(); return; }
            if (e.keyCode == KeyCode.Comma) { _editBuf += ','; e.Use(); return; }
        }

        private static void BeginEditPlant(Plant p, int fi)
        {
            _editKind = EK_PLANT;
            _editField = fi;
            _editPtr = 0L;
            try { _editPtr = p.Pointer.ToInt64(); } catch { }
            _editBuf = PlantDb.GetLive(p, fi) ?? "";
            if (_editBuf == "-1") _editBuf = "";
            _editText = PlantDb.FieldKind(fi) == PlantDb.KText;
            SetStatus("正在编辑 " + PlantDb.FieldName(fi) + " —— 输入数值后回车确认，Esc 取消");
        }

        private static void BeginEditConfig(string key)
        {
            var e = ModConfig.ByKey(key);
            if (e == null) return;
            _editKind = EK_CONFIG;
            _editKey = key;
            _editBuf = ModConfig.GetString(e);
            _editText = e.SettingType == typeof(string);
            SetStatus("正在编辑 " + key + " —— 回车确认，Esc 取消");
        }

        private static void CommitEdit()
        {
            int kind = _editKind;
            int fi = _editField;
            long ptr = _editPtr;
            string key = _editKey;
            string buf = _editBuf;
            _editKind = EK_NONE;
            _editField = -1;
            _editKey = null;
            _editBuf = "";

            if (kind == EK_FILTER) { _pickerFilter = buf; _pickerPage = 0; return; }

            if (kind == EK_SB)
            {
                int f = _sbEdit;
                if (f == 99) { _lineup = buf; SetStatus("阵容码已填入（长度 " + buf.Length + "）"); return; }
                if (f == SB_ZX)
                {
                    float x;
                    if (float.TryParse(buf, NumberStyles.Float, CultureInfo.InvariantCulture, out x))
                    { _sbZX = Mathf.Clamp(x, 0.1f, 12f); SetStatus("僵尸 X = " + _sbZX.ToString("0.##", CultureInfo.InvariantCulture)); }
                    else SetStatus("X 不是合法数字");
                    return;
                }
                int v;
                if (!int.TryParse(buf, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                { SetStatus("不是合法整数"); return; }
                SbSetInt(f, v);
                SetStatus("已设置 = " + SbGetInt(f));
                return;
            }

            if (kind == EK_ZOMBIE)
            {
                Zombie z = Actions.ZombieByPtr(ptr);
                if (z == null) { SetStatus("该僵尸已经不在了"); return; }
                string e = ZombieDb.SetValue(z, fi, buf);
                SetStatus(e == null
                    ? ("已修改 僵尸·" + ZombieDb.FieldName(fi) + " = " + buf)
                    : ("僵尸·" + ZombieDb.FieldName(fi) + " 修改失败：" + e));
                return;
            }

            if (kind == EK_PLANT) { SetStatus(BatchSet(fi, buf)); return; }

            if (kind == EK_CONFIG)
            {
                var e = ModConfig.ByKey(key);
                if (e == null) { SetStatus("配置项不存在"); return; }
                ModConfig.SetFromString(e, buf);
                SetStatus(key + " = " + ModConfig.GetString(e));
                return;
            }
        }

        /// <summary>画出"标签 + 可编辑数值框（+ 已改动时的还原按钮）"。
        /// 返回 true 表示这个格子被点了；reset=true 表示点的是「R」还原按钮。</summary>
        private static bool ValueCell(Rect r, string label, string value, bool overridden, bool editing, Color valueColor, out bool reset)
        {
            reset = false;
            float lw = Mathf.Min(96f, r.width * 0.42f);
            UiSkin.Text(new Rect(r.x, r.y, lw - 4f, r.height), label, UiSkin.Left);

            float rstW = overridden ? 17f : 0f;
            Rect box = new Rect(r.x + lw, r.y + 2f, r.width - lw - rstW - 4f, r.height - 4f);
            if (box.width < 30f) box.width = 30f;
            Fill(box, editing ? UiSkin.ColEdit : new Color(0.07f, 0.08f, 0.10f, 1f));
            UiSkin.Border(box, editing ? UiSkin.ColYellow : (overridden ? UiSkin.ColYellow : UiSkin.ColLine));

            string shown = editing ? (_editBuf + "_") : value;
            UiSkin.Text(box, UiSkin.Fit(shown, 12), new UiSkin.TextOpt
            {
                Align = TextAnchor.MiddleCenter,
                Style = editing ? FontStyle.Bold : FontStyle.Normal,
                Color = editing ? UiSkin.ColYellow : (overridden ? UiSkin.ColYellow : valueColor)
            });

            if (overridden)
            {
                Rect rst = new Rect(r.xMax - rstW, r.y + 2f, rstW, r.height - 4f);
                Fill(rst, new Color(0.30f, 0.16f, 0.16f, 1f));
                UiSkin.Border(rst, UiSkin.ColLine);
                UiSkin.Text(rst, "R", new UiSkin.TextOpt
                { Align = TextAnchor.MiddleCenter, Style = FontStyle.Bold, Color = UiSkin.ColRed });
                reset = UiSkin.Click(rst, 0);
                if (reset) return true;
            }

            return UiSkin.Click(box, 0);
        }

        // ================================================================ 页 0 功能开关
        private static void TabToggles(Rect cr)
        {
            float rh = RowH;

            // 展开 / 折叠 全部（默认就是全部展开的，见 GroupOpenDefault）
            Rect bAll = new Rect(cr.x, cr.y, 96f, 22f);
            Rect bNon = new Rect(cr.x + 100f, cr.y, 96f, 22f);
            if (UiSkin.Button(bAll, "展开全部", false, true))
                for (int g = 0; g < GroupKeys.Length; g++) _groupOpen[g] = true;
            if (UiSkin.Button(bNon, "折叠全部", false, true))
                for (int g = 0; g < GroupKeys.Length; g++) _groupOpen[g] = false;
            UiSkin.Text(new Rect(cr.x + 204f, cr.y, cr.width - 210f, 22f),
                "开关默认全是关的。点整行勾选，点右边的数字框改数值。",
                new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });

            Rect area = new Rect(cr.x, cr.y + 26f, cr.width - 18f, cr.height - 26f - 22f);
            cr = area;

            int total = 0;
            for (int g = 0; g < GroupKeys.Length; g++)
            {
                total++;
                if (_groupOpen[g]) total += GroupKeys[g].Length;
            }
            int visible = Mathf.Max(1, (int)(cr.height / rh));
            if (_scroll[0] > total - visible) _scroll[0] = Mathf.Max(0, total - visible);
            if (_scroll[0] < 0) _scroll[0] = 0;

            int w = _wheel;
            if (w != 0 && UiSkin.MouseOver(cr)) { _scroll[0] = Mathf.Clamp(_scroll[0] + w, 0, Mathf.Max(0, total - visible)); _wheel = 0; }

            int k = -1;
            float y = cr.y;
            for (int g = 0; g < GroupKeys.Length && y < cr.yMax; g++)
            {
                k++;
                if (k >= _scroll[0] && k < _scroll[0] + visible)
                {
                    Rect r = new Rect(cr.x, y, cr.width, rh);
                    string title = (_groupOpen[g] ? "[-] " : "[+] ") + GroupNames[g]
                                 + "   (" + GroupKeys[g].Length + " 项)    " + GroupHints[g];
                    if (UiSkin.LeftButton(r, title, _groupOpen[g], UiSkin.ColGreen))
                        _groupOpen[g] = !_groupOpen[g];
                    y += rh;
                }

                if (!_groupOpen[g]) continue;

                string[] keys = GroupKeys[g];
                for (int i = 0; i < keys.Length; i++)
                {
                    k++;
                    // 窗口外的行：不画，**也绝对不要推进 y**。
                    // 之前这里多写了个 if (k < _scroll[0] + visible) y += rh，
                    // 于是"窗口上方"被跳过的行也会把 y 往下推，
                    // 结果往下滚的时候可见行整体被顶到面板底下，看起来就是"UI 消失了一部分"。
                    if (k < _scroll[0] || k >= _scroll[0] + visible) continue;

                    var entry = ModConfig.ByKey(keys[i]);
                    if (entry == null) { y += rh; continue; }
                    string label = Cn.ContainsKey(keys[i]) ? Cn[keys[i]] : keys[i];
                    Rect r = new Rect(cr.x + 14f, y, cr.width - 14f, rh);

                    if (entry.SettingType == typeof(bool))
                    {
                        bool v = ModConfig.GetString(entry) == "True" || ModConfig.GetString(entry) == "true";
                        bool hover = UiSkin.MouseOver(r);
                        if (UiSkin.CheckRow(r, v, label, keys[i], hover))
                        {
                            ModConfig.SetFromString(entry, v ? "0" : "1");
                            SetStatus(keys[i] + " = " + ModConfig.GetString(entry));
                        }
                    }
                    else
                    {
                        bool editing = _editKind == EK_CONFIG && _editKey == keys[i];
                        Fill(r, UiSkin.ColBack2);
                        // 三段固定宽度：中文标签 | 配置键名（右对齐、截断）| 数值框
                        // 之前键名只给 38px，长键名会溢出到数值框上，把数字压在字母上（用户截图里就是 "9999SunFloor"）
                        UiSkin.Text(new Rect(r.x + 6f, r.y, r.width - 288f, rh), label, UiSkin.Left);
                        UiSkin.Text(new Rect(r.xMax - 278f, r.y, 174f, rh), UiSkin.Fit(keys[i], 20),
                            new UiSkin.TextOpt { Align = TextAnchor.MiddleRight, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
                        Rect box = new Rect(r.xMax - 100f, r.y + 2f, 96f, rh - 4f);
                        Fill(box, editing ? UiSkin.ColEdit : new Color(0.07f, 0.08f, 0.10f, 1f));
                        UiSkin.Border(box, editing ? UiSkin.ColYellow : UiSkin.ColLine);
                        UiSkin.Text(box, UiSkin.Fit(editing ? _editBuf + "_" : ModConfig.GetString(entry), 13),
                            new UiSkin.TextOpt { Align = TextAnchor.MiddleCenter, Style = FontStyle.Bold, Color = UiSkin.ColYellow });
                        if (UiSkin.Click(box, 0)) BeginEditConfig(keys[i]);
                    }
                    y += rh;
                }
            }

            // 提示行单独占一条，不要压在最后几行上（之前就压在"词条白名单"那行上，两边都看不清）
            Rect hintBar = new Rect(cr.x + 4f, cr.yMax + 2f, cr.width - 8f, 18f);
            UiSkin.Text(hintBar,
                "共 " + total + " 行，当前显示第 " + (_scroll[0] + 1) + " ~ " + Mathf.Min(total, _scroll[0] + visible) + " 行（滚轮 / ▲▼ 滚动）",
                new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });

            // 滚动条必须画在面板**里面**：之前放在 cr.xMax+2，结果探出面板右边界 9px，
            // 截图里就是那条"伸出边框的灰色带子"
            float barX = cr.xMax + 4f;
            Rect up = new Rect(barX, cr.y, 12f, 20f);
            Rect dn = new Rect(barX, cr.yMax - 20f, 12f, 20f);
            if (UiSkin.Button(up, "▲", false, _scroll[0] > 0)) _scroll[0] = Mathf.Max(0, _scroll[0] - 1);
            if (UiSkin.Button(dn, "▼", false, _scroll[0] < total - visible)) _scroll[0] = Mathf.Min(Mathf.Max(0, total - visible), _scroll[0] + 1);
            Rect track = new Rect(barX + 2f, cr.y + 22f, 8f, cr.height - 44f);
            if (track.height > 10f)
            {
                Fill(track, UiSkin.ColBack);
                float frac = (float)visible / Mathf.Max(1, total);
                float th = Mathf.Max(18f, track.height * frac);
                float tpos = total > visible ? (float)_scroll[0] / (total - visible) : 0f;
                Fill(new Rect(track.x, track.y + (track.height - th) * tpos, track.width, th), UiSkin.ColLine);
            }
        }

        // ================================================================ 页 1 植物
        private static void TabPlants(Rect cr)
        {
            var list = Actions.PlantsSnapshot();
            int sel = Actions.SelectedIndex();
            Plant selP = sel >= 0 ? Actions.PlantAt(sel) : null;

            float lw = 268f;
            Rect left = new Rect(cr.x, cr.y, lw, cr.height);
            Rect right = new Rect(cr.x + lw + 10f, cr.y, cr.width - lw - 10f, cr.height);

            Fill(left, UiSkin.ColBack2);
            UiSkin.Border(left, UiSkin.ColLine);

            float rh = RowH;
            string head = "场上植物 " + list.Count + " 只   来源:" + Actions.PlantSource();
            UiSkin.Text(new Rect(left.x + 6f, left.y + 2f, left.width - 12f, rh), head, UiSkin.Bold);

            Rect larea = new Rect(left.x + 4f, left.y + rh + 2f, left.width - 8f, left.height - rh - 30f);
            int vis = Mathf.Max(1, (int)(larea.height / rh));
            int maxScroll = Mathf.Max(0, list.Count - vis);
            if (_scroll[1] > maxScroll) _scroll[1] = maxScroll;
            if (_scroll[1] < 0) _scroll[1] = 0;
            int w = _wheel;
            if (w != 0 && UiSkin.MouseOver(larea)) { _scroll[1] = Mathf.Clamp(_scroll[1] + w, 0, maxScroll); _wheel = 0; }

            if (list.Count == 0)
            {
                UiSkin.Text(new Rect(larea.x + 4f, larea.y + 4f, larea.width - 8f, rh * 3f),
                    "场上没有读到植物。\n进入关卡后这里会列出全部植物。\n（也可以直接点植物上的 ESP 方框）",
                    new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
            }

            float y = larea.y;
            for (int i = _scroll[1]; i < list.Count && i < _scroll[1] + vis; i++)
            {
                Plant p = list[i];
                if (p == null) continue;
                bool isSel = i == sel;
                bool marked = IsMarked(p);
                Rect r = new Rect(larea.x, y, larea.width, rh);
                string txt = PlantDb.Label(p);
                try { txt += "   " + p.thePlantHealth + "/" + p.thePlantMaxHealth; } catch { }

                // 行底色：多选打勾(蓝) > 单选(绿) > 普通
                Fill(r, marked ? new Color(0.16f, 0.26f, 0.40f, 1f)
                              : isSel ? new Color(0.16f, 0.34f, 0.24f, 1f) : UiSkin.ColBack);
                Rect side = new Rect(r.x, r.y, 4f, r.height);
                if (marked) Fill(side, new Color(0.45f, 0.72f, 1f, 1f));
                else if (isSel) Fill(side, UiSkin.ColGreen);

                // 多选勾选框（点它 = 加入/移出批量修改）
                Rect cb = new Rect(r.x + 8f, r.y + (r.height - UiSkin.CheckSize) * 0.5f, UiSkin.CheckSize, UiSkin.CheckSize);
                UiSkin.Checkbox(cb, marked);
                UiSkin.Text(new Rect(cb.xMax + 5f, r.y, r.width - (cb.xMax - r.x) - 8f, r.height),
                            UiSkin.Fit(txt, 24),
                            new UiSkin.TextOpt
                            {
                                Align = TextAnchor.MiddleLeft,
                                Style = (isSel || marked) ? FontStyle.Bold : FontStyle.Normal,
                                Color = marked ? new Color(0.72f, 0.86f, 1f, 1f)
                                      : isSel ? UiSkin.ColGreen : UiSkin.ColText
                            });

                // 点勾选框 = 多选；点别的 = 单选（并把它设为编辑对象）
                if (UiSkin.Click(cb, 0)) { ToggleMark(p); }
                else if (UiSkin.Click(r, 0)) Actions.Select(p);
                y += rh;
            }

            // 多选工具条
            float bw3 = (left.width - 16f) / 3f;
            Rect m1 = new Rect(left.x + 4f, left.yMax - 50f, bw3, 21f);
            Rect m2 = new Rect(left.x + 8f + bw3, left.yMax - 50f, bw3, 21f);
            Rect m3 = new Rect(left.x + 12f + bw3 * 2f, left.yMax - 50f, bw3, 21f);
            if (UiSkin.Button(m1, "全选", false, list.Count > 0)) MarkAll();
            if (UiSkin.Button(m2, "反选", false, list.Count > 0)) InvertMark();
            if (UiSkin.Button(m3, "清空多选", false, _multi.Count > 0)) MarkClear();
            Rect clr = new Rect(left.x + 4f, left.yMax - 26f, left.width - 8f, 22f);
            if (UiSkin.Button(clr, "取消单选（多选保留）", false, true)) { Actions.Select(null); }

            // ---------------- 右侧编辑器
            Fill(right, UiSkin.ColBack2);
            UiSkin.Border(right, UiSkin.ColLine);

            int nTargets = _multi.Count;
            if (selP == null && nTargets == 0)
            {
                UiSkin.Text(new Rect(right.x + 10f, right.y + 10f, right.width - 20f, 130f),
                    "没有选中植物。\n\n1) 点一下场上任意植物上方的方框\n2) 或在左边列表里点一只植物\n\n" +
                    "【批量修改】把左边列表每一行最左边的勾选框打上勾，\n" +
                    "就可以一次改好几株（勾选后右边所有改动都会应用到它们）。",
                    new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
                return;
            }

            long ptr = 0L;
            try { ptr = selP == null ? 0L : selP.Pointer.ToInt64(); } catch { }

            // 多选时顶部显示醒目提示（批量改的是哪几株）
            float topY = right.y + 3f;
            if (nTargets > 0)
            {
                Rect banner = new Rect(right.x + 6f, right.y + 2f, right.width - 12f, rh);
                Fill(banner, new Color(0.14f, 0.24f, 0.38f, 1f));
                UiSkin.Border(banner, new Color(0.45f, 0.72f, 1f, 1f));
                UiSkin.Text(new Rect(banner.x + 6f, banner.y, banner.width - 12f, banner.height),
                    "★ 批量修改模式：已勾选 " + nTargets + " 株 —— 下面的改动会一次应用到这 " + nTargets + " 株",
                    new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = FontStyle.Bold, Color = new Color(0.80f, 0.90f, 1f, 1f) });
                topY = banner.yMax + 2f;
            }
            else
            {
                UiSkin.Text(new Rect(right.x + 8f, topY, right.width - 16f, rh),
                    "已选中: " + PlantDb.Label(selP) + "    指针 0x" + ptr.ToString("X"), UiSkin.Bold);
                topY += rh;
            }

            // 两个数值列
            float colW = (right.width - 20f) / 2f;
            float fy = topY + 4f;
            int n = PlantDb.FieldCount;
            int rows = (n + 1) / 2;
            float avail = right.yMax - 24f - fy - 22f * 4f - 6f;
            float cellH = Mathf.Clamp(avail / Mathf.Max(1, rows), 17f, rh);

            // 批量修改的目标列表（勾了就用勾的，没勾就用单选的）
            var targets = EditTargets(selP);
            Plant shown = selP != null ? selP : (targets.Count > 0 ? targets[0] : null);

            for (int fi = 0; fi < n; fi++)
            {
                int col = fi / rows, row = fi % rows;
                Rect cell = new Rect(right.x + 6f + col * colW, fy + row * cellH, colW - 6f, cellH);
                bool editing = _editKind == EK_PLANT && _editField == fi && _editPtr == ptr;
                string val = shown == null ? "" : PlantDb.GetLive(shown, fi);
                if (PlantDb.FieldKind(fi) == PlantDb.KText && string.IsNullOrEmpty(val)) val = "（空）";
                if (PlantDb.FieldKind(fi) != PlantDb.KText && !string.IsNullOrEmpty(PlantDb.FieldUnit(fi)))
                    val += PlantDb.FieldUnit(fi);
                bool ovr = shown != null && PlantDb.IsOverridden(shown, fi);

                bool reset;
                if (ValueCell(cell, PlantDb.FieldName(fi), val, ovr, editing, UiSkin.ColText, out reset))
                {
                    if (reset)
                    {
                        int c2 = 0;
                        foreach (Plant tp in targets) { PlantDb.ClearField(tp, fi); c2++; }
                        SetStatus("已还原 " + c2 + " 株的 " + PlantDb.FieldName(fi));
                    }
                    else BeginEditPlant(shown, fi);
                }
            }

            // 机制勾选
            float gy = fy + rows * cellH + 4f;
            UiSkin.Text(new Rect(right.x + 8f, gy, right.width - 16f, 18f),
                nTargets > 0 ? ("机制开关（应用到已勾选的 " + nTargets + " 株）") : "机制开关", UiSkin.Bold);
            gy += 19f;
            int fn = PlantDb.FlagCount;
            float fw = (right.width - 20f) / 2f;
            for (int i = 0; i < fn; i++)
            {
                int col = i / 3, row = i % 3;
                Rect r = new Rect(right.x + 6f + col * fw, gy + row * 20f, fw - 6f, 19f);
                bool v = shown != null && PlantDb.GetFlag(shown, i);
                Rect box = new Rect(r.x + 2f, r.y + (r.height - UiSkin.CheckSize) * 0.5f, UiSkin.CheckSize, UiSkin.CheckSize);
                UiSkin.Checkbox(box, v);
                UiSkin.Text(new Rect(box.xMax + 6f, r.y, r.width - 24f, r.height), PlantDb.FlagName(i),
                    new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = v ? FontStyle.Bold : FontStyle.Normal, Color = UiSkin.ColText });
                if (UiSkin.Click(r, 0))
                {
                    foreach (Plant tp in targets) PlantDb.SetFlag(tp, i, !v);
                    SetStatus(PlantDb.FlagName(i) + " = " + (!v) + "（" + targets.Count + " 株）");
                }
            }

            float by = right.yMax - 24f;
            Rect b1 = new Rect(right.x + 6f, by, 168f, 21f);
            Rect b2 = new Rect(right.x + 180f, by, 110f, 21f);
            Rect b3 = new Rect(right.x + 296f, by, 110f, 21f);
            if (UiSkin.Button(b1, nTargets > 0 ? ("还原已勾选 " + nTargets + " 株的全部修改") : "还原该植物的全部修改", false, true))
            {
                foreach (Plant tp in targets) PlantDb.ClearAll(tp);
                SetStatus("已还原 " + targets.Count + " 株植物的全部修改");
            }
            if (UiSkin.Button(b2, "刷新融合配方", false, true))
            { PlantDb.InvalidateRecipes(); Actions.InvalidateRecipes(); SetStatus("融合配方已刷新"); }
            if (UiSkin.Button(b3, "去融合页", false, true)) _tab = 3;
        }

        // ---------------------------------------------------------------- 植物多选
        private static readonly System.Collections.Generic.HashSet<IntPtr> _multi =
            new System.Collections.Generic.HashSet<IntPtr>();

        internal static int MarkCount { get { return _multi.Count; } }

        private static bool IsMarked(Plant p)
        {
            if (p == null) return false;
            try { return _multi.Contains(p.Pointer); } catch { return false; }
        }

        private static void ToggleMark(Plant p)
        {
            if (p == null) return;
            try
            {
                IntPtr k = p.Pointer;
                if (!_multi.Remove(k)) _multi.Add(k);
                SetStatus(_multi.Count > 0 ? ("已勾选 " + _multi.Count + " 株（改动会一次应用到它们）") : "已清空多选");
            }
            catch { }
        }

        internal static void MarkAll()
        {
            var list = Actions.PlantsSnapshot();
            for (int i = 0; i < list.Count; i++)
            {
                Plant p = list[i];
                if (p == null) continue;
                try { _multi.Add(p.Pointer); } catch { }
            }
            SetStatus("已全选 " + _multi.Count + " 株");
        }

        internal static void InvertMark()
        {
            var list = Actions.PlantsSnapshot();
            for (int i = 0; i < list.Count; i++)
            {
                Plant p = list[i];
                if (p == null) continue;
                try
                {
                    IntPtr k = p.Pointer;
                    if (!_multi.Remove(k)) _multi.Add(k);
                }
                catch { }
            }
            SetStatus("反选后共 " + _multi.Count + " 株");
        }

        internal static void MarkClear() { _multi.Clear(); SetStatus("已清空多选"); }

        /// <summary>把当前"单选中的那株"加入/移出多选（给 IPC 自检用）</summary>
        internal static void MarkToggleSelected() { ToggleMark(Actions.SelectedPlant()); }

        /// <summary>
        /// 把一个数值应用到"当前所有目标植物"（多选就是全部勾选的，否则就是单选那株）。
        /// 界面按回车走这里，IPC 的 BSet 也走这里，保证两条路行为一致。
        /// </summary>
        internal static string BatchSet(int fi, string buf)
        {
            var targets = EditTargets(Actions.SelectedPlant());
            if (targets.Count == 0) return "没有目标植物（不在场上？）";
            string err = null;
            int ok = 0;
            foreach (Plant tp in targets)
            {
                string e = PlantDb.SetValue(tp, fi, buf);
                if (e == null) ok++; else if (err == null) err = e;
            }
            return err == null
                ? ("已把 " + PlantDb.FieldName(fi) + " = " + buf + " 应用到 " + ok + " 株植物")
                : (PlantDb.FieldName(fi) + " 修改失败：" + err);
        }

        /// <summary>批量修改的目标：有勾选就只改勾选的，否则改单选的</summary>
        private static System.Collections.Generic.List<Plant> EditTargets(Plant selP)
        {
            var res = new System.Collections.Generic.List<Plant>();
            var list = Actions.PlantsSnapshot();
            if (_multi.Count > 0)
            {
                // 顺便把已经不在场上的指针清掉，避免勾选数虚高
                var live = new System.Collections.Generic.HashSet<IntPtr>();
                for (int i = 0; i < list.Count; i++)
                {
                    Plant p = list[i];
                    if (p == null) continue;
                    try
                    {
                        IntPtr k = p.Pointer;
                        live.Add(k);
                        if (_multi.Contains(k)) res.Add(p);
                    }
                    catch { }
                }
                _multi.RemoveWhere(k => !live.Contains(k));
            }
            else if (selP != null) res.Add(selP);
            return res;
        }

        // ================================================================ 页 2 僵尸
        private static void TabZombies(Rect cr)
        {
            var list = Actions.ZombiesSnapshot();
            int sel = Actions.ZombieSelIndex();
            Zombie selZ = sel >= 0 ? Actions.ZombieAt(sel) : null;

            float lw = 268f;
            Rect left = new Rect(cr.x, cr.y, lw, cr.height);
            Rect right = new Rect(cr.x + lw + 10f, cr.y, cr.width - lw - 10f, cr.height);

            Fill(left, UiSkin.ColBack2);
            UiSkin.Border(left, UiSkin.ColLine);
            float rh = RowH;
            UiSkin.Text(new Rect(left.x + 6f, left.y + 2f, left.width - 12f, rh),
                "场上僵尸 " + list.Count + " 只   （来源: Lawnf.GetAllZombies）", UiSkin.Bold);

            Rect larea = new Rect(left.x + 4f, left.y + rh + 2f, left.width - 8f, left.height - rh - 30f);
            int vis = Mathf.Max(1, (int)(larea.height / rh));
            int maxScroll = Mathf.Max(0, list.Count - vis);
            if (_scroll[2] > maxScroll) _scroll[2] = maxScroll;
            if (_scroll[2] < 0) _scroll[2] = 0;
            int w = _wheel;
            if (w != 0 && UiSkin.MouseOver(larea)) { _scroll[2] = Mathf.Clamp(_scroll[2] + w, 0, maxScroll); _wheel = 0; }

            if (list.Count == 0)
                UiSkin.Text(new Rect(larea.x + 4f, larea.y + 4f, larea.width - 8f, rh * 3f),
                    "这一关目前没有僵尸。\n僵尸出现后这里会列出来，\n也可以直接点僵尸头上的 ESP 方框选中它。",
                    new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });

            float y = larea.y;
            for (int i = _scroll[2]; i < list.Count && i < _scroll[2] + vis; i++)
            {
                Zombie z = list[i];
                if (z == null) continue;
                bool isSel = i == sel;
                Rect r = new Rect(larea.x, y, larea.width, rh);
                string txt;
                bool mind = false;
                try { mind = z.isMindControlled; } catch { }
                try { txt = ZombieDb.Label(z) + "  " + z.theHealth + "/" + z.theMaxHealth + "  行" + (z.theZombieRow + 1); }
                catch { txt = ZombieDb.Label(z); }
                if (mind) txt = "★" + txt;

                Fill(r, isSel ? new Color(0.16f, 0.34f, 0.24f, 1f)
                              : mind ? new Color(0.10f, 0.24f, 0.26f, 1f) : UiSkin.ColBack);
                if (isSel) Fill(new Rect(r.x, r.y, 4f, r.height), UiSkin.ColGreen);
                else if (mind) Fill(new Rect(r.x, r.y, 4f, r.height), new Color(0.35f, 0.95f, 0.90f, 1f));
                UiSkin.Text(new Rect(r.x + 9f, r.y, r.width - 12f, r.height), UiSkin.Fit(txt, 26),
                    new UiSkin.TextOpt
                    {
                        Align = TextAnchor.MiddleLeft,
                        Style = isSel ? FontStyle.Bold : FontStyle.Normal,
                        Color = isSel ? UiSkin.ColGreen : UiSkin.ColText
                    });
                if (UiSkin.Click(r, 0)) Actions.SelectZombie(z);
                y += rh;
            }

            Rect clr = new Rect(left.x + 4f, left.yMax - 26f, left.width - 8f, 22f);
            if (UiSkin.Button(clr, "取消选择", false, true)) Actions.SelectZombie(null);

            // ---------------- 右侧编辑器
            Fill(right, UiSkin.ColBack2);
            UiSkin.Border(right, UiSkin.ColLine);

            if (selZ == null)
            {
                UiSkin.Text(new Rect(right.x + 10f, right.y + 10f, right.width - 20f, 120f),
                    "没有选中僵尸。\n\n1) 点一下僵尸头上那个红色（或青色）的方框\n2) 或在左边列表里点一只\n\n选中后这里会显示它的全部数值，改多少就是多少。\n" +
                    "★ = 已被魅惑（友军），方框是青色的。",
                    new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
                return;
            }

            long zptr = 0L;
            try { zptr = selZ.Pointer.ToInt64(); } catch { }
            string kind = "";
            try { kind = selZ.isMindControlled ? "  [友军/魅惑]" : "  [敌人]"; } catch { }
            UiSkin.Text(new Rect(right.x + 8f, right.y + 3f, right.width - 16f, rh),
                "已选中: " + ZombieDb.Label(selZ) + kind + "    指针 0x" + zptr.ToString("X"), UiSkin.Bold);

            float colW = (right.width - 20f) / 2f;
            float fy = right.y + rh + 4f;
            int n = ZombieDb.FieldCount;                  // 14 项 -> 7 行
            int rows = (n + 1) / 2;
            float avail = right.height - rh - 4f - 22f * 3f - 6f;
            float cellH = Mathf.Clamp(avail / Mathf.Max(1, rows), 17f, rh);

            for (int fi = 0; fi < n; fi++)
            {
                int col = fi / rows, row = fi % rows;
                Rect cell = new Rect(right.x + 6f + col * colW, fy + row * cellH, colW - 6f, cellH);
                bool editing = _editKind == EK_ZOMBIE && _editField == fi && _editPtr == zptr;
                string val = ZombieDb.GetLive(selZ, fi);
                if (!string.IsNullOrEmpty(ZombieDb.FieldUnit(fi))) val += ZombieDb.FieldUnit(fi);
                bool ovr = ZombieDb.IsOverridden(selZ, fi);

                bool reset;
                if (ValueCell(cell, ZombieDb.FieldName(fi), val, ovr, editing, UiSkin.ColText, out reset))
                {
                    if (reset) { ZombieDb.ClearField(selZ, fi); SetStatus("已还原 " + ZombieDb.FieldName(fi)); }
                    else BeginEditZombie(selZ, fi);
                }
            }

            // 行为开关
            float gy = fy + rows * cellH + 4f;
            UiSkin.Text(new Rect(right.x + 8f, gy, right.width - 16f, 18f), "行为开关", UiSkin.Bold);
            gy += 19f;
            int fn = ZombieDb.FlagCount;
            float fw = (right.width - 20f) / 2f;
            for (int i = 0; i < fn; i++)
            {
                int col = i / 2, row = i % 2;
                Rect r = new Rect(right.x + 6f + col * fw, gy + row * 20f, fw - 6f, 19f);
                bool v = ZombieDb.GetFlag(selZ, i);
                Rect box = new Rect(r.x + 2f, r.y + (r.height - UiSkin.CheckSize) * 0.5f, UiSkin.CheckSize, UiSkin.CheckSize);
                UiSkin.Checkbox(box, v);
                UiSkin.Text(new Rect(box.xMax + 6f, r.y, r.width - 24f, r.height), ZombieDb.FlagName(i),
                    new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = v ? FontStyle.Bold : FontStyle.Normal, Color = UiSkin.ColText });
                if (UiSkin.Click(r, 0)) { ZombieDb.SetFlag(selZ, i, !v); SetStatus(ZombieDb.FlagName(i) + " = " + (!v)); }
            }

            float by = right.yMax - 24f;
            Rect b1 = new Rect(right.x + 6f, by, 132f, 21f);
            Rect b2 = new Rect(right.x + 144f, by, 132f, 21f);
            Rect b3 = new Rect(right.x + 282f, by, 132f, 21f);
            if (UiSkin.Button(b1, "秒杀这只僵尸", false, true)) { try { selZ.Die(0); } catch { } SetStatus("已秒杀"); }
            if (UiSkin.Button(b2, "还原它的全部修改", false, true)) { ZombieDb.ClearAll(selZ); SetStatus("已还原该僵尸的全部修改"); }
            if (UiSkin.Button(b3, "换成别的僵尸…", false, true)) { OpenPicker(7, 0); }
        }

        private static void BeginEditZombie(Zombie z, int fi)
        {
            _editKind = EK_ZOMBIE;
            _editField = fi;
            _editPtr = 0L;
            try { _editPtr = z.Pointer.ToInt64(); } catch { }
            _editBuf = ZombieDb.GetLive(z, fi) ?? "";
            _editText = false;
            SetStatus("正在编辑 僵尸·" + ZombieDb.FieldName(fi) + " —— 输入数值后回车确认，Esc 取消");
        }

        // ================================================================ 页 3 融合
        private static void TabFusion(Rect cr)
        {
            int sel = Actions.SelectedIndex();
            Plant selP = sel >= 0 ? Actions.PlantAt(sel) : null;

            if (selP == null)
            {
                UiSkin.Text(new Rect(cr.x + 8f, cr.y + 8f, cr.width - 16f, 100f),
                    "先在「植物」页（或点场上植物上方的方框）选中一株植物，\n这里会列出它全部的中文融合配方，并且能改掉任意一条配方的结果。",
                    new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
                return;
            }

            int t = -1; try { t = (int)selP.thePlantType; } catch { }
            PlantDb.LoadRecipes(t);
            int recipes = PlantDb.RecipeCount;
            int w = _wheel;   // 只在这里用一次；下面按 listArea 是否被指到才真的滚

            float top = cr.y;
            string kind = PlantDb.KindOf(t);
            UiSkin.Text(new Rect(cr.x + 4f, top, cr.width - 8f, 20f),
                "已选中: " + PlantDb.Label(selP) + "    " + kind + "    配方 " + recipes + " 条", UiSkin.Bold);
            top += 20f;

            string parents = PlantDb.ParentsOf(t);
            if (!string.IsNullOrEmpty(parents))
                UiSkin.Text(new Rect(cr.x + 4f, top, cr.width - 8f, 18f), "合成来源: " + parents,
                    new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = FontStyle.Normal, Color = UiSkin.ColGreen });
            else
                UiSkin.Text(new Rect(cr.x + 4f, top, cr.width - 8f, 18f), "合成来源: 基础植物（不能由两种植物合成得到）",
                    new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
            top += 20f;

            float bottomH = 100f;
            Rect listArea = new Rect(cr.x, top, cr.width, cr.height - (top - cr.y) - bottomH);
            Fill(listArea, UiSkin.ColBack2);
            UiSkin.Border(listArea, UiSkin.ColLine);

            float rh = RowH;
            int vis = Mathf.Max(1, (int)((listArea.height - 24f) / rh));
            int maxScroll = Mathf.Max(0, recipes - vis);
            if (_scroll[2] > maxScroll) _scroll[2] = maxScroll;
            if (w != 0 && UiSkin.MouseOver(listArea)) { _scroll[2] = Mathf.Clamp(_scroll[2] + w, 0, maxScroll); _wheel = 0; }

            UiSkin.Text(new Rect(listArea.x + 6f, listArea.y + 2f, listArea.width - 12f, 20f),
                "伙伴植物  →  当前融合结果      点「直接融合」= 让这株和该伙伴当场融合；点「改结果」= 改掉融合表里这条配方", UiSkin.Bold);

            long selfPtr = 0L;
            try { selfPtr = selP.Pointer.ToInt64(); } catch { }

            float y = listArea.y + 22f;
            if (recipes == 0)
            {
                UiSkin.Text(new Rect(listArea.x + 8f, y, listArea.width - 16f, rh * 2f),
                    "这种植物在游戏里没有任何融合配方。\n可以在下面自己加一条：选伙伴、选结果，然后点「写入配方」。",
                    new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
            }
            for (int i = _scroll[2]; i < recipes && i < _scroll[2] + vis; i++)
            {
                int partner = PlantDb.RecipePartner(i);
                int result = PlantDb.RecipeResult(i);
                Rect r = new Rect(listArea.x + 4f, y, listArea.width - 8f, rh);
                Fill(r, UiSkin.ColBack);
                UiSkin.Text(new Rect(r.x + 8f, r.y, r.width - 200f, rh),
                    UiSkin.Fit(PlantDb.CnName(partner) + "  (#" + partner + ")   →   " + PlantDb.CnName(result) + "  (#" + result + ")", 40),
                    UiSkin.Left);
                Rect bFuse = new Rect(r.xMax - 190f, r.y + 1f, 92f, rh - 2f);
                Rect bEdit = new Rect(r.xMax - 94f, r.y + 1f, 90f, rh - 2f);
                if (UiSkin.Button(bFuse, "直接融合", false, true))
                {
                    Actions.RequestFuse(selfPtr, partner);
                    SetStatus("正在融合：" + PlantDb.CnName(t) + " + " + PlantDb.CnName(partner) + " …");
                }
                if (UiSkin.Button(bEdit, "改结果…", false, true))
                {
                    _recipePartner = partner;
                    _pickerCommit = true;
                    OpenPicker(2, result);
                }
                y += rh;
            }

            // 自定义配方
            Rect cus = new Rect(cr.x, cr.yMax - bottomH + 2f, cr.width, bottomH - 2f);
            Fill(cus, UiSkin.ColBack2);
            UiSkin.Border(cus, UiSkin.ColLine);
            UiSkin.Text(new Rect(cus.x + 6f, cus.y + 2f, cus.width - 12f, 18f),
                "自己加一条 / 换一条配方", UiSkin.Bold);

            Rect p1 = new Rect(cus.x + 6f, cus.y + 22f, cus.width * 0.30f, 22f);
            Rect p2 = new Rect(cus.x + 6f + cus.width * 0.31f, cus.y + 22f, cus.width * 0.30f, 22f);
            Rect p3 = new Rect(cus.xMax - 118f, cus.y + 22f, 112f, 22f);

            string pn = _recipePartner >= 0 ? (PlantDb.CnName(_recipePartner) + " (#" + _recipePartner + ")") : "点这里选伙伴…";
            string rn = _recipeResult >= 0 ? (PlantDb.CnName(_recipeResult) + " (#" + _recipeResult + ")") : "点这里选结果…";
            if (UiSkin.Button(p1, "伙伴: " + UiSkin.Fit(pn, 14), _recipePartner >= 0, true))
            { _pickerCommit = false; OpenPicker(1, _recipePartner); }
            if (UiSkin.Button(p2, "结果: " + UiSkin.Fit(rn, 14), _recipeResult >= 0, true))
            { _pickerCommit = false; OpenPicker(2, _recipeResult); }
            if (UiSkin.Button(p3, "写入配方", false, true))
            {
                if (_recipePartner < 0 || _recipeResult < 0) SetStatus("请先选好伙伴和结果");
                else SetStatus(PlantDb.AddRecipe(t, _recipePartner, _recipeResult));
            }

            Rect t1 = new Rect(cus.x + 6f, cus.y + 48f, 150f, 22f);
            Rect t2 = new Rect(cus.x + 162f, cus.y + 48f, 190f, 22f);
            if (UiSkin.Button(t1, "用伙伴直接融合", false, _recipePartner >= 0))
            {
                Actions.RequestFuse(selfPtr, _recipePartner);
                SetStatus("正在融合：" + PlantDb.CnName(t) + " + " + PlantDb.CnName(_recipePartner) + " …");
            }
            if (UiSkin.Button(t2, "直接变成「结果」那种", false, _recipeResult >= 0))
            {
                string err = PlantDb.Transform(selP, _recipeResult);
                SetStatus(err == null ? ("已直接变身成 " + PlantDb.CnName(_recipeResult)) : ("变身失败: " + err));
            }
            UiSkin.Text(new Rect(cus.x + 358f, cus.y + 48f, cus.width - 364f, 22f),
                "「直接融合」会先让原植物退场、再让游戏在空格子上新建结果植物（模型/血量/子弹都按融合体重算）",
                new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });

            UiSkin.Text(new Rect(cus.x + 6f, cus.y + 74f, cus.width - 12f, 20f),
                "「写入配方」的含义：" + PlantDb.CnName(t) + " 与所选伙伴放在一起时，融合结果换成你所选的那种。写入后会立刻用游戏自己的查表接口校验。",
                new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
        }

        // ================================================================ 页 3 沙盒
        private static void TabSandbox(Rect cr)
        {
            float rh = RowH;
            float y = cr.y + 4f;
            float colW = (cr.width - 14f) / 2f;

            UiSkin.Text(new Rect(cr.x + 4f, y, cr.width, 20f),
                "沙盒：直接在场地上放东西 / 全员变身 / 阵容码。数值框点一下就能改，回车确认。", UiSkin.Bold);
            y += 22f;

            // ---------- 左：植物放置
            Rect L = new Rect(cr.x, y, colW, 132f);
            Fill(L, UiSkin.ColBack2); UiSkin.Border(L, UiSkin.ColLine);
            UiSkin.Text(new Rect(L.x + 6f, L.y + 2f, L.width - 12f, 20f), "植物放置", UiSkin.Bold);
            if (UiSkin.Button(new Rect(L.x + 6f, L.y + 24f, L.width - 12f, 22f),
                "植物: " + PlantDb.CnName(_sbPType) + "   (#" + _sbPType + ")", true, true))
                OpenPicker(3, _sbPType);
            SbNum(new Rect(L.x + 6f, L.y + 50f, (L.width - 18f) / 2f, 22f), "列", SB_COL);
            SbNum(new Rect(L.x + 12f + (L.width - 18f) / 2f, L.y + 50f, (L.width - 18f) / 2f, 22f), "行", SB_ROW);
            if (UiSkin.Button(new Rect(L.x + 6f, L.y + 76f, L.width - 12f, 24f), "放置植物", false, true))
            {
                string r = PlantDb.Spawn(_sbPType, _sbCol, _sbRow);
                SetStatus(r);
            }
            UiSkin.Text(new Rect(L.x + 6f, L.y + 102f, L.width - 12f, 26f),
                "走游戏自己的 CreatePlant.SetPlant，模型/血量都正常初始化。",
                new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });

            // ---------- 右：僵尸放置
            Rect R = new Rect(cr.x + colW + 10f, y, colW, 132f);
            Fill(R, UiSkin.ColBack2); UiSkin.Border(R, UiSkin.ColLine);
            UiSkin.Text(new Rect(R.x + 6f, R.y + 2f, R.width - 12f, 20f), "僵尸放置", UiSkin.Bold);
            if (UiSkin.Button(new Rect(R.x + 6f, R.y + 24f, R.width - 12f, 22f),
                "僵尸: " + PlantDb.CnNameZombie(_sbZType) + "   (#" + _sbZType + ")", true, true))
                OpenPicker(4, _sbZType);
            SbNum(new Rect(R.x + 6f, R.y + 50f, 60f, 22f), "行", SB_ZROW);
            SbNum(new Rect(R.x + 72f, R.y + 50f, 76f, 22f), "X", SB_ZX);
            Rect mindR = new Rect(R.x + 154f, R.y + 50f, R.width - 160f, 22f);
            if (UiSkin.Button(mindR, _sbZMind ? "魅惑: 开" : "魅惑: 关", _sbZMind, true)) _sbZMind = !_sbZMind;
            if (UiSkin.Button(new Rect(R.x + 6f, R.y + 76f, R.width - 12f, 24f), "放置僵尸", false, true))
                SetStatus(Actions.ActionSpawnZombie(_sbZRow, _sbZType, _sbZX, _sbZMind));
            if (UiSkin.Button(new Rect(R.x + 6f, R.y + 102f, R.width - 12f, 24f), "清空全场僵尸", false, true))
                SetStatus(Actions.ActionKillAllZombies());

            y += 140f;

            // ---------- 小推车 / 群体变身
            Rect L2 = new Rect(cr.x, y, colW, 108f);
            Fill(L2, UiSkin.ColBack2); UiSkin.Border(L2, UiSkin.ColLine);
            UiSkin.Text(new Rect(L2.x + 6f, L2.y + 2f, L2.width - 12f, 20f), "小推车放置", UiSkin.Bold);
            SbNum(new Rect(L2.x + 6f, L2.y + 24f, 60f, 22f), "行", SB_MROW);
            SbNum(new Rect(L2.x + 72f, L2.y + 24f, 110f, 22f), "类型 0-4", SB_MTYPE);
            if (UiSkin.Button(new Rect(L2.x + 6f, L2.y + 50f, L2.width - 12f, 24f), "放置小推车", false, true))
                SetStatus(Actions.ActionSpawnMower(_sbMRow, _sbMType));
            UiSkin.Text(new Rect(L2.x + 6f, L2.y + 76f, L2.width - 12f, 30f),
                "0=草地 1=泳池 2=清洁车 3=草地射手 4=阳光车",
                new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });

            Rect R2 = new Rect(cr.x + colW + 10f, y, colW, 108f);
            Fill(R2, UiSkin.ColBack2); UiSkin.Border(R2, UiSkin.ColLine);
            UiSkin.Text(new Rect(R2.x + 6f, R2.y + 2f, R2.width - 12f, 20f), "全员变身", UiSkin.Bold);
            if (UiSkin.Button(new Rect(R2.x + 6f, R2.y + 24f, R2.width - 12f, 22f),
                "所有植物 → " + PlantDb.CnName(_sbCPType) + " (#" + _sbCPType + ")", true, true))
                OpenPicker(5, _sbCPType);
            if (UiSkin.Button(new Rect(R2.x + 6f, R2.y + 50f, (R2.width - 18f) / 2f, 24f), "执行", false, true))
                SetStatus(Actions.ActionChangeAllPlants(_sbCPType));
            if (UiSkin.Button(new Rect(R2.x + 12f + (R2.width - 18f) / 2f, R2.y + 50f, (R2.width - 18f) / 2f, 24f),
                "杀光植物", false, true))
                SetStatus(Actions.ActionKillAllPlants());
            if (UiSkin.Button(new Rect(R2.x + 6f, R2.y + 78f, R2.width - 12f, 24f),
                "所有僵尸 → " + PlantDb.CnNameZombie(_sbCZType) + " (#" + _sbCZType + ")", true, true))
                OpenPicker(6, _sbCZType);

            y += 116f;

            // ---------- 阵容码
            Rect lu = new Rect(cr.x, y, cr.width, Mathf.Max(80f, cr.yMax - y - 4f));
            Fill(lu, UiSkin.ColBack2); UiSkin.Border(lu, UiSkin.ColLine);
            UiSkin.Text(new Rect(lu.x + 6f, lu.y + 2f, lu.width - 12f, 20f),
                "阵容码：把当前场上的植物+僵尸导出成一行文本，也可以粘回来一键复原", UiSkin.Bold);

            bool luEdit = _editKind == EK_SB && _sbEdit == 99;
            Rect luBox = new Rect(lu.x + 6f, lu.y + 24f, lu.width - 12f, 24f);
            Fill(luBox, luEdit ? UiSkin.ColEdit : new Color(0.07f, 0.08f, 0.10f, 1f));
            UiSkin.Border(luBox, luEdit ? UiSkin.ColYellow : UiSkin.ColLine);
            UiSkin.Text(luBox, "阵容码: " + (luEdit ? _editBuf + "_" : (_lineup.Length == 0 ? "（点这里粘贴 PVZRH1;... 或先点导出）" : UiSkin.Fit(_lineup, 70))),
                new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = FontStyle.Normal, Color = luEdit ? UiSkin.ColYellow : UiSkin.ColText });
            if (UiSkin.Click(luBox, 0)) { _editKind = EK_SB; _sbEdit = 99; _editBuf = _lineup; _editText = true; }

            float bw = (lu.width - 24f) / 3f;
            if (UiSkin.Button(new Rect(lu.x + 6f, lu.y + 52f, bw, 24f), "导出阵容码", false, true))
            { _lineup = Actions.ActionExportLineupCode(); SetStatus("阵容码已生成，可以直接用 Ctrl+A/Ctrl+C 复制输入框内容（也写进日志了）"); }
            if (UiSkin.Button(new Rect(lu.x + 12f + bw, lu.y + 52f, bw, 24f), "应用阵容码", false, _lineup.Length > 0))
                SetStatus(Actions.ActionImportLineup(_lineup));
            if (UiSkin.Button(new Rect(lu.x + 18f + bw * 2f, lu.y + 52f, bw, 24f), "清空输入框", false, true))
            { _lineup = ""; SetStatus("已清空"); }

            UiSkin.Text(new Rect(lu.x + 6f, lu.y + 80f, lu.width - 12f, 34f),
                "格式：PVZRH1;P列,行,植物ID;Z行,僵尸ID,是否魅惑,X —— 纯本地文本，不联网、不校验卡密。",
                new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
        }

        /// <summary>沙盒里的"标签 + 可编辑数值"小控件</summary>
        private static void SbNum(Rect r, string label, int field)
        {
            float lw = 54f;
            if (label == "类型 0-4")
            {
                lw = 62f;
                UiSkin.Text(new Rect(r.x, r.y, lw - 2f, r.height), label, UiSkin.Left);
            }
            else UiSkin.Text(new Rect(r.x, r.y, lw - 4f, r.height), label, UiSkin.Left);

            Rect box = new Rect(r.x + lw, r.y, r.width - lw, r.height);
            bool editing = _editKind == EK_SB && _sbEdit == field;
            Fill(box, editing ? UiSkin.ColEdit : new Color(0.07f, 0.08f, 0.10f, 1f));
            UiSkin.Border(box, editing ? UiSkin.ColYellow : UiSkin.ColLine);
            string shown;
            if (field == SB_ZX) shown = _sbZX.ToString("0.##", CultureInfo.InvariantCulture);
            else shown = SbGetInt(field).ToString(CultureInfo.InvariantCulture);
            UiSkin.Text(box, editing ? _editBuf + "_" : shown, new UiSkin.TextOpt
            { Align = TextAnchor.MiddleCenter, Style = editing ? FontStyle.Bold : FontStyle.Normal, Color = editing ? UiSkin.ColYellow : UiSkin.ColText });

            if (UiSkin.Click(box, 0))
            {
                _editKind = EK_SB;
                _sbEdit = field;
                _editBuf = shown;
                _editText = false;
            }
        }

        // ================================================================ 页 4 作弊动作
        private static void TabActions(Rect cr)
        {
            float bw = (cr.width - 24f) / 2f;
            float bh = 34f;
            float x0 = cr.x + 4f, x1 = cr.x + 8f + bw;
            float y = cr.y + 6f;

            if (UiSkin.Button(new Rect(x0, y, bw, bh), "秒杀全场僵尸", false, true)) SetStatus(Actions.ActionKillAllZombies());
            if (UiSkin.Button(new Rect(x1, y, bw, bh), "立刻开始下一波", false, true)) SetStatus(Actions.ActionNextWave());
            y += bh + 8f;
            if (UiSkin.Button(new Rect(x0, y, bw, bh), "触发全部小推车", false, true)) SetStatus(Actions.ActionTriggerMowers());
            if (UiSkin.Button(new Rect(x1, y, bw, bh), "阳光设为 9999", false, true)) SetStatus(Actions.ActionSetSun(9999));
            y += bh + 8f;
            if (UiSkin.Button(new Rect(x0, y, bw, bh), "一键解锁全部内容 + 补满资源", false, true))
            { Actions.UnlockAndResources(); SetStatus("已执行解锁与资源补满"); }
            if (UiSkin.Button(new Rect(x1, y, bw, bh), "补满当前阳光", false, true))
            { Actions.TopUpSun(); SetStatus("已补满阳光"); }
            y += bh + 8f;
            if (UiSkin.Button(new Rect(x0, y, bw, bh), "恢复所有植物的原始状态", false, true))
            { Actions.RestorePlants(); SetStatus("已恢复所有植物"); }
            if (UiSkin.Button(new Rect(x1, y, bw, bh), "清空全部植物修改记录", false, true))
            { Overrides.ClearAll(); SetStatus("已清空全部植物修改"); }
            y += bh + 8f;

            // ---- 对齐 Modified-Plus 的批量动作 ----
            if (UiSkin.Button(new Rect(x0, y, bw, bh), "全体植物升到 10 级", false, true))
                SetStatus(Actions.ActionUpgradeAllPlants(10));
            if (UiSkin.Button(new Rect(x1, y, bw, bh), "所有植物满血", false, true))
                SetStatus(Actions.ActionHealAllPlants());
            y += bh + 8f;
            if (UiSkin.Button(new Rect(x0, y, bw, bh), "魅惑全场僵尸（变友军）", false, true))
                SetStatus(Actions.ActionMindControlAll());
            if (UiSkin.Button(new Rect(x1, y, bw, bh), "僵尸血量 ×10 并回满", false, true))
                SetStatus(Actions.ActionSetAllZombieHp(10.0));
            y += bh + 8f;
            if (UiSkin.Button(new Rect(x0, y, bw, bh), "下一回合（旅行模式）", false, true))
                SetStatus(Actions.ActionTravelNextRound());
            if (UiSkin.Button(new Rect(x1, y, bw, bh), "杀死全部植物", false, true))
                SetStatus(Actions.ActionKillAllPlants());
            y += bh + 8f;
            if (UiSkin.Button(new Rect(x0, y, bw, bh), "进入冒险模式 第 1 关", false, true))
                SetStatus(Actions.ActionEnterGame(0, 1));
            if (UiSkin.Button(new Rect(x1, y, bw, bh), "回到主菜单", false, true))
                SetStatus(Actions.ActionEnterGame(-1, 1));
            y += bh + 8f;
            // 一键把**所有**作弊项关掉（含配置里被写脏的），回到"什么都不改"的状态
            // 一种种一列 / 一排
            {
                bool line = ModConfig.PlantWholeLine.Value;
                bool vertical = ModConfig.PlantLineDir.Value != "row";
                if (UiSkin.Button(new Rect(x0, y, bw, bh), line ? "一种种一列: 开" : "一种种一列: 关", line, true))
                { ModConfig.PlantWholeLine.Value = !line; SetStatus("一种种一列 = " + (!line)); }
                if (UiSkin.Button(new Rect(x1, y, bw, bh), vertical ? "方向: 一列（竖）" : "方向: 一排（横）", vertical, true))
                { ModConfig.PlantLineDir.Value = vertical ? "row" : "col"; SetStatus("铺满方向改为 " + (vertical ? "一排（横）" : "一列（竖）")); }
            }
            y += bh + 8f;

            // 游戏自己的倍速档位（GameSpeedMgr.Gears）：直接点它给的档
            UiSkin.Text(new Rect(x0, y, bw, 18f), "游戏倍速（游戏自己的档位 + 自定义）",
                new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
            y += 19f;
            float gx = x0;
            float gw = 58f;
            try
            {
                var gears = GameSpeedMgr.Gears;
                if (gears != null)
                {
                    for (int i = 0; i < gears.Count && i < 6; i++)
                    {
                        float gv = gears[i];
                        if (UiSkin.Button(new Rect(gx, y, gw, bh), gv.ToString("0.##") + "x", Math.Abs(ModConfig.GameSpeed.Value - gv) < 0.01f, true))
                        { ModConfig.GameSpeed.Value = gv; SetStatus("游戏倍速 = " + gv.ToString("0.##") + "x"); }
                        gx += gw + 6f;
                    }
                }
            }
            catch { }
            if (UiSkin.Button(new Rect(gx, y, gw, bh), "0.5x", Math.Abs(ModConfig.GameSpeed.Value - 0.5f) < 0.01f, true))
            { ModConfig.GameSpeed.Value = 0.5f; SetStatus("游戏倍速 = 0.5x"); }
            gx += gw + 6f;
            if (UiSkin.Button(new Rect(gx, y, gw, bh), "1x", Math.Abs(ModConfig.GameSpeed.Value - 1f) < 0.01f, true))
            { ModConfig.GameSpeed.Value = 1f; SetStatus("游戏倍速 = 1x（恢复）"); }
            gx += gw + 6f;
            if (UiSkin.Button(new Rect(gx, y, 96f, bh), "速度诊断", false, true))
                SetStatus(Actions.SpeedInfo());
            y += bh + 8f;

            if (UiSkin.Button(new Rect(x0, y, bw, bh), "★ 一键关闭全部作弊", true, true))
            {
                int n = ModConfig.ForceAllOff();
                SetStatus("已把 " + n + " 个作弊项恢复为关闭/中性值（立即生效，无需重启）");
            }
            if (UiSkin.Button(new Rect(x1, y, bw, bh), "只关总开关（Enabled）", false, true))
            {
                ModConfig.Enabled.Value = false;
                SetStatus("总开关已关闭：所有功能停止生效（再点「功能开关」页的「总开关」可恢复）");
            }
            y += bh + 12f;

            Fill(new Rect(cr.x, y, cr.width, cr.height - (y - cr.y) - 4f), UiSkin.ColBack2);
            UiSkin.Border(new Rect(cr.x, y, cr.width, cr.height - (y - cr.y) - 4f), UiSkin.ColLine);
            UiSkin.Text(new Rect(cr.x + 8f, y + 4f, cr.width - 16f, cr.height - (y - cr.y) - 12f),
                "说明：\n" +
                "· 上面这些是「一次性动作」，点一下立刻生效一次，不是常驻开关。\n" +
                "· 常驻功能（自动收集、僵尸冻结、一击必杀等）在「功能开关」页里。\n" +
                "· 所有功能默认全部关闭，需要哪个自己打开。",
                new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
        }

        // ================================================================ 页 4 设置
        private static void TabSettings(Rect cr)
        {
            float y = cr.y + 6f;
            float rh = RowH + 4f;

            // ---------------- 版本信息（一眼看清跑的是哪一版） ----------------
            Rect ver = new Rect(cr.x + 4f, y, cr.width - 8f, 44f);
            Fill(ver, new Color(0.13f, 0.20f, 0.16f, 1f));
            UiSkin.Border(ver, UiSkin.ColGreen);
            UiSkin.Text(new Rect(ver.x + 8f, ver.y + 2f, ver.width - 16f, 20f),
                "当前版本  v" + Plugin.Version + "    （内置界面 · 7 页签）", UiSkin.Bold);
            string build = "构建时间 " + BuildStamp + "    补丁 " + Plugin.PatchOk + "/" + Plugin.PatchTotal;
            UiSkin.Text(new Rect(ver.x + 8f, ver.y + 21f, ver.width - 16f, 20f), build,
                new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
            y += 50f;

            UiSkin.Text(new Rect(cr.x + 4f, y, cr.width, 20f), "界面字体", UiSkin.Bold);
            y += 22f;

            Rect a = new Rect(cr.x + 4f, y, 34f, rh);
            Rect b = new Rect(cr.x + 44f, y, 60f, rh);
            Rect c = new Rect(cr.x + 112f, y, 34f, rh);
            if (UiSkin.Button(a, "-", false, true))
            { ModConfig.MenuFontSize.Value = Mathf.Max(10, ModConfig.MenuFontSize.Value - 1); }
            UiSkin.Text(b, ModConfig.MenuFontSize.Value.ToString(CultureInfo.InvariantCulture),
                new UiSkin.TextOpt { Align = TextAnchor.MiddleCenter, Style = FontStyle.Bold, Color = UiSkin.ColYellow });
            if (UiSkin.Button(c, "+", false, true))
            { ModConfig.MenuFontSize.Value = Mathf.Min(26, ModConfig.MenuFontSize.Value + 1); }
            UiSkin.Text(new Rect(cr.x + 154f, y, cr.width - 158f, rh), "菜单字号（10 ~ 26）", UiSkin.Left);
            Rect reset = new Rect(cr.xMax - 150f, y, 146f, rh);
            if (UiSkin.Button(reset, "菜单窗口复位到左上角", false, true))
            { Panel.x = 26f; Panel.y = 22f; _px = 26f; _py = 22f; SetStatus("菜单窗口已复位"); }
            y += rh + 6f;

            bool esp = EspOverlay.ShowEsp;
            if (UiSkin.CheckRow(new Rect(cr.x + 4f, y, cr.width - 8f, rh), esp, "显示 ESP 方框（植物上方的信息框）", "F3", UiSkin.MouseOver(new Rect(cr.x + 4f, y, cr.width - 8f, rh))))
                EspOverlay.ShowEsp = !esp;
            y += rh;
            if (UiSkin.CheckRow(new Rect(cr.x + 4f, y, cr.width - 8f, rh), EspOverlay.ShowName, "方框里显示植物名称", "", true))
                EspOverlay.ShowName = !EspOverlay.ShowName;
            y += rh;
            if (UiSkin.CheckRow(new Rect(cr.x + 4f, y, cr.width - 8f, rh), EspOverlay.ShowHp, "方框里显示血量", "", true))
                EspOverlay.ShowHp = !EspOverlay.ShowHp;
            y += rh;
            if (UiSkin.CheckRow(new Rect(cr.x + 4f, y, cr.width - 8f, rh), EspOverlay.ShowIndex, "方框里显示序号", "", true))
                EspOverlay.ShowIndex = !EspOverlay.ShowIndex;
            y += rh + 6f;

            UiSkin.Text(new Rect(cr.x + 4f, y, cr.width, 20f), "植物读数诊断（哪条读取路径通了）", UiSkin.Bold);
            y += 22f;
            string diag = "关卡=" + Actions.LevelInfo()
                        + "   采用来源=" + Actions.PlantSource()
                        + "   场上植物=" + Actions.PlantsSnapshot().Count
                        + "   游戏侧计数=" + Actions.GamePlantCount() + "\n"
                        + "各来源命中: Lawnf(全量)=" + Actions.SrcLawnf()
                        + "   byRow(按行)=" + Actions.SrcByRow()
                        + "   boardEntity=" + Actions.SrcBoard()
                        + "   FindObjectsOfType=" + Actions.SrcFind()
                        + "   网格兜底=" + Actions.SrcGrid()
                        + "   花园=" + Actions.GardenCount();
            UiSkin.Text(new Rect(cr.x + 8f, y, cr.width - 16f, 60f), diag,
                new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
            y += 62f;

            // ---------------- 热键（可自定义） ----------------
            UiSkin.Text(new Rect(cr.x + 4f, y, cr.width, 20f), "热键（点按钮后按新键即可改，Esc 取消）", UiSkin.Bold);
            y += 22f;

            float krh = 24f;
            UiSkin.Text(new Rect(cr.x + 8f, y, 110f, krh), "菜单 显示/隐藏", UiSkin.Left);
            Rect mk = new Rect(cr.x + 122f, y, 130f, krh);
            string mkLabel = _binding == 1 ? "请按新键…" : ModConfig.MenuKeyCode().ToString();
            if (UiSkin.Button(mk, mkLabel, _binding == 1, true)) { _binding = 1; SetStatus("请按下想用来显示/隐藏菜单的按键（Esc 取消）"); }
            UiSkin.Text(new Rect(cr.x + 260f, y, cr.width - 268f, krh),
                "默认 F1。建议用 F1~F12 或 Insert 这类游戏不会用到的键。",
                new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
            y += krh + 4f;

            UiSkin.Text(new Rect(cr.x + 8f, y, 110f, krh), "ESP 方框开关", UiSkin.Left);
            Rect ek = new Rect(cr.x + 122f, y, 130f, krh);
            string ekLabel = _binding == 2 ? "请按新键…" : ModConfig.EspKeyCode().ToString();
            if (UiSkin.Button(ek, ekLabel, _binding == 2, true)) { _binding = 2; SetStatus("请按下想用来开关 ESP 的按键（Esc 取消）"); }
            UiSkin.Text(new Rect(cr.x + 260f, y, cr.width - 268f, krh),
                "默认 F3。",
                new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
            y += krh + 8f;

            UiSkin.Text(new Rect(cr.x + 8f, y, cr.width - 16f, 76f),
                "鼠标左键点 ESP 方框 —— 选中那株植物\n" +
                "编辑数值时：回车确认，Esc 取消，退格删除\n" +
                "鼠标压在菜单上时，游戏不会响应点击（不会误种植物）\n" +
                "热键会写进配置文件，下次启动依然有效",
                new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
        }

        // ================================================================ 类型选择器
        // target: 0=把选中植物变成 1=融合伙伴 2=融合结果 3=沙盒植物 4=沙盒僵尸
        //         5=群体变身(植物) 6=群体变身(僵尸)
        private static void OpenPicker(int target, int current)
        {
            _pickerOpen = true;
            _pickerTarget = target;
            _pickerZombie = target == 4 || target == 6 || target == 7;
            _pickerFilter = "";
            _pickerPage = 0;
            PlantDb.FilterTypes("", _pickerZombie);
            SetStatus(_pickerZombie ? "选择僵尸类型：可以打字筛选，回车确认筛选内容"
                                    : "选择植物类型：可以打字筛选，回车确认筛选内容");
            _ = current;
        }

        private static void DrawPicker()
        {
            float w = Mathf.Min(470f, Screen.width - 60f);
            float h = Mathf.Min(470f, Screen.height - 60f);
            Rect p = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            Fill(p, new Color(0.07f, 0.08f, 0.10f, 1f));
            UiSkin.Border(p, UiSkin.ColGreen);

            string title;
            switch (_pickerTarget)
            {
                case 0: title = "选择要变成的植物"; break;
                case 1: title = "选择融合的伙伴植物"; break;
                case 2: title = "选择融合结果植物"; break;
                case 3: title = "沙盒：选择要放置的植物"; break;
                case 4: title = "沙盒：选择要放置的僵尸"; break;
                case 5: title = "群体变身：所有植物变成"; break;
                case 6: title = "群体变身：所有僵尸变成"; break;
                default: title = "把选中的僵尸变成"; break;
            }
            UiSkin.Text(new Rect(p.x + 10f, p.y + 4f, p.width - 90f, 24f), title, UiSkin.Bold);

            Rect closeR = new Rect(p.xMax - 70f, p.y + 5f, 60f, 22f);
            if (UiSkin.Button(closeR, "关闭", false, true)) { _pickerOpen = false; return; }

            Rect fbox = new Rect(p.x + 10f, p.y + 32f, p.width - 20f, 24f);
            bool editing = _editKind == EK_FILTER;
            Fill(fbox, editing ? UiSkin.ColEdit : new Color(0.12f, 0.13f, 0.16f, 1f));
            UiSkin.Border(fbox, editing ? UiSkin.ColYellow : UiSkin.ColLine);
            UiSkin.Text(fbox, "筛选: " + (editing ? _editBuf + "_" : (_pickerFilter.Length == 0 ? "（点这里打字，中文名或编号）" : _pickerFilter)),
                new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = FontStyle.Normal, Color = editing ? UiSkin.ColYellow : UiSkin.ColText });
            if (UiSkin.Click(fbox, 0))
            {
                _editKind = EK_FILTER;
                _editBuf = _pickerFilter;
                _editText = true;
            }

            var res = PlantDb.FilterTypes(_pickerFilter, _pickerZombie);
            float rh = RowH;
            int vis = Mathf.Max(1, (int)((p.height - 32f - 24f - 34f) / rh));
            int pages = Mathf.Max(1, (res.Count + vis - 1) / vis);
            if (_pickerPage >= pages) _pickerPage = pages - 1;
            if (_pickerPage < 0) _pickerPage = 0;

            int w0 = _wheel;
            if (w0 != 0 && UiSkin.MouseOver(p)) { _pickerPage = Mathf.Clamp(_pickerPage + w0, 0, pages - 1); _wheel = 0; }

            float y = p.y + 60f;
            int start = _pickerPage * vis;
            for (int i = start; i < res.Count && i < start + vis; i++)
            {
                int idx = res[i];
                int id = _pickerZombie ? PlantDb.ZombieTypeIdAt(idx) : PlantDb.TypeIdAt(idx);
                string nm = _pickerZombie ? PlantDb.ZombieTypeNameAt(idx) : PlantDb.TypeNameAt(idx);
                Rect r = new Rect(p.x + 8f, y, p.width - 16f, rh);
                if (UiSkin.LeftButton(r, nm + "   (#" + id + ")", false, UiSkin.ColText))
                {
                    _pickerOpen = false;
                    if (_pickerTarget == 0)
                    {
                        int s2 = Actions.SelectedIndex();
                        Plant sp = s2 >= 0 ? Actions.PlantAt(s2) : null;
                        string e2 = PlantDb.Transform(sp, id);
                        SetStatus(e2 == null ? ("已变成 " + PlantDb.CnName(id)) : ("变身失败: " + e2));
                    }
                    else if (_pickerTarget == 1) { _recipePartner = id; SetStatus("伙伴 = " + PlantDb.CnName(id)); }
                    else if (_pickerTarget == 2)
                    {
                        _recipeResult = id;
                        if (_pickerCommit && _recipePartner >= 0)
                        {
                            int st = -1;
                            int s3 = Actions.SelectedIndex();
                            Plant sp2 = s3 >= 0 ? Actions.PlantAt(s3) : null;
                            if (sp2 != null) { try { st = (int)sp2.thePlantType; } catch { } }
                            if (st < 0) SetStatus("没有选中植物，无法写入配方");
                            else SetStatus(PlantDb.AddRecipe(st, _recipePartner, id));
                        }
                        else SetStatus("结果 = " + PlantDb.CnName(id));
                        _pickerCommit = false;
                    }
                    else if (_pickerTarget == 3) { SbSetInt(SB_PTYPE, id); SetStatus("沙盒植物 = " + PlantDb.CnName(id)); }
                    else if (_pickerTarget == 4) { SbSetInt(SB_ZTYPE, id); SetStatus("沙盒僵尸 = " + PlantDb.CnNameZombie(id)); }
                    else if (_pickerTarget == 5) { SbSetInt(SB_CPTYPE, id); SetStatus("群体变身目标 = " + PlantDb.CnName(id)); }
                    else if (_pickerTarget == 6) { SbSetInt(SB_CZTYPE, id); SetStatus("群体变身僵尸目标 = " + ZombieDb.CnName(id)); }
                    else
                    {
                        int zs = Actions.ZombieSelIndex();
                        Zombie zz = zs >= 0 ? Actions.ZombieAt(zs) : null;
                        string e3 = ZombieDb.Transform(zz, id);
                        SetStatus(e3 == null ? ("已变身成 " + ZombieDb.CnName(id)) : ("变身失败: " + e3));
                    }
                }
                y += rh;
            }

            UiSkin.Text(new Rect(p.x + 10f, p.yMax - 28f, 200f, 22f),
                "共 " + res.Count + " 项   第 " + (_pickerPage + 1) + "/" + pages + " 页", UiSkin.Left);
            Rect pv = new Rect(p.xMax - 160f, p.yMax - 28f, 70f, 22f);
            Rect nx = new Rect(p.xMax - 84f, p.yMax - 28f, 74f, 22f);
            if (UiSkin.Button(pv, "上一页", false, _pickerPage > 0)) _pickerPage--;
            if (UiSkin.Button(nx, "下一页", false, _pickerPage < pages - 1)) _pickerPage++;
        }
    }

    /// <summary>
    /// 注入到 Il2Cpp 的最小外壳：只有一个 OnGUI，参数和返回值都是 void，
    /// 绝不会有 Il2Cpp 不能编组的签名。
    /// </summary>
    public class MenuOverlay : MonoBehaviour
    {
        private void OnGUI()
        {
            try
            {
                UiSkin.Ensure();
                MenuUI.Frame();
            }
            catch (Exception e) { Plugin.LogOnce("菜单外壳", e); }
        }
    }

    /// <summary>
    /// 鼠标压在菜单上时，屏蔽游戏自己的鼠标处理（Mouse.Update 是它全部点击逻辑的入口）。
    /// 这样点菜单就不会同时在游戏里种植物、点卡片。
    /// </summary>
    [HarmonyPatch(typeof(Mouse), "Update")]
    internal static class Patch_Mouse_Update
    {
        private static bool Prefix() { return !MenuUI.BlocksGameInput(); }
    }
}
