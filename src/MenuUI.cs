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
        private static readonly int[] _scroll = new int[5];
        private static readonly bool[] _groupOpen = new bool[16];
        private static readonly List<int> _filtered = new List<int>(800);

        private static bool _dragging;
        private static Vector2 _dragOff;

        // 正在编辑的东西
        private const int EK_NONE = 0, EK_PLANT = 1, EK_CONFIG = 2, EK_FILTER = 3;
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

        private static string _status = "";
        private static double _statusAt;

        private const float Pad = 8f;

        // ---------------------------------------------------------------- 配置分组
        private static readonly string[] GroupNames =
        {
            "通用", "解锁与资源", "关卡内", "战斗", "旅行 / 词条", "天赋", "深渊抽奖券", "经典作弊", "界面与 ESP",
        };

        private static readonly string[] GroupHints =
        {
            "总开关、重刷间隔", "关卡·图鉴·金币", "阳光不减", "无敌 / 伤害倍率", "Roguelike 词条池", "冒险天赋树",
            "抽奖券", "原版经典功能", "字号、外置窗口",
        };

        private static readonly string[][] GroupKeys =
        {
            new[] { "Enabled", "ApplyIntervalSeconds" },
            new[] { "UnlockAllLevels", "Money", "DeveloperMode" },
            new[] { "InfiniteSun", "SunFloor" },
            new[] { "GodModePlants", "PlantDamageMultiplier", "ZombieDamageTakenMultiplier", "OneHitZombies" },
            new[] { "TravelBuffs", "DamageReduction", "LuckyStrike", "DamageAmplification", "PlantZeroHealth", "BuffWhitelist", "UltiBuffWhitelist" },
            new[] { "TalentUnlockAll", "TalentStars", "DisableHardMode" },
            new[] { "AbyssMaxTickets", "AbyssInfiniteTickets" },
            new[] { "AutoCollectSun", "NoCardCooldown", "FreePlanting", "UnlimitedCardUse", "FreezeAllZombies", "ZombiesStopMoving", "AutoKillZombies" },
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
        };

        private static readonly string[] TabNames = { "功能开关", "植物", "融合", "作弊动作", "设置" };

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
                        case 2: TabFusion(cr); break;
                        case 3: TabActions(cr); break;
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
                        "PVZ 融合版 3.9 修改器   v1.1（内置界面）", UiSkin.Bold);

            if (UiSkin.SmallButton(espR, esp ? "ESP: 开" : "ESP: 关", esp, true))
                EspOverlay.ShowEsp = !esp;
            if (UiSkin.SmallButton(hideR, "隐藏 (Insert)", false, true)) Show = false;
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
                s = "Insert 显示/隐藏   ·   F3 开关 ESP   ·   点植物上的方框选中它";
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

            if (_editKind != EK_NONE) { EditKey(e); return; }

            if (e.keyCode == KeyCode.Insert) { Show = !Show; e.Use(); }
            else if (e.keyCode == KeyCode.F3) { EspOverlay.ShowEsp = !EspOverlay.ShowEsp; e.Use(); }
            else if (e.keyCode == KeyCode.Escape && _pickerOpen) { _pickerOpen = false; e.Use(); }
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

            if (kind == EK_PLANT)
            {
                Plant p = Actions.PlantByPtr(ptr);
                if (p == null) { SetStatus("该植物已经不在了"); return; }
                string err = PlantDb.SetValue(p, fi, buf);
                SetStatus(err == null
                    ? ("已修改 " + PlantDb.FieldName(fi) + " = " + buf)
                    : (PlantDb.FieldName(fi) + " 修改失败：" + err));
                return;
            }

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
            int total = 0;
            for (int g = 0; g < GroupKeys.Length; g++)
            {
                total++;
                if (_groupOpen[g]) total += GroupKeys[g].Length;
            }
            int visible = Mathf.Max(1, (int)(cr.height / rh));
            if (_scroll[0] > total - visible) _scroll[0] = Mathf.Max(0, total - visible);
            if (_scroll[0] < 0) _scroll[0] = 0;

            int w = UiSkin.Wheel();
            if (w != 0 && UiSkin.MouseOver(cr)) _scroll[0] = Mathf.Clamp(_scroll[0] + w, 0, Mathf.Max(0, total - visible));

            int k = -1;
            float y = cr.y;
            for (int g = 0; g < GroupKeys.Length && y < cr.yMax; g++)
            {
                k++;
                if (k >= _scroll[0] && k < _scroll[0] + visible)
                {
                    Rect r = new Rect(cr.x, y, cr.width, rh);
                    string title = (_groupOpen[g] ? "[-] " : "[+] ") + GroupNames[g];
                    if (UiSkin.LeftButton(r, title + "    " + GroupHints[g], _groupOpen[g], UiSkin.ColGreen))
                        _groupOpen[g] = !_groupOpen[g];
                    y += rh;
                }

                if (!_groupOpen[g]) continue;

                string[] keys = GroupKeys[g];
                for (int i = 0; i < keys.Length; i++)
                {
                    k++;
                    if (k < _scroll[0] || k >= _scroll[0] + visible) { if (k < _scroll[0] + visible) y += rh; continue; }

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
                        UiSkin.Text(new Rect(r.x + 6f, r.y, r.width - 150f, rh), label, UiSkin.Left);
                        Rect box = new Rect(r.xMax - 140f, r.y + 2f, 96f, rh - 4f);
                        Fill(box, editing ? UiSkin.ColEdit : new Color(0.07f, 0.08f, 0.10f, 1f));
                        UiSkin.Border(box, editing ? UiSkin.ColYellow : UiSkin.ColLine);
                        UiSkin.Text(box, UiSkin.Fit(editing ? _editBuf + "_" : ModConfig.GetString(entry), 13),
                            new UiSkin.TextOpt { Align = TextAnchor.MiddleRight, Style = FontStyle.Bold, Color = UiSkin.ColYellow });
                        UiSkin.Text(new Rect(r.xMax - 40f, r.y, 38f, rh), keys[i], new UiSkin.TextOpt
                        { Align = TextAnchor.MiddleRight, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
                        if (UiSkin.Click(box, 0)) BeginEditConfig(keys[i]);
                    }
                    y += rh;
                }
            }

            // 折叠时把整页填满提示，别留一大块空白
            if (y < cr.yMax - 24f)
            {
                UiSkin.Text(new Rect(cr.x + 6f, y + 6f, cr.width - 12f, Mathf.Min(80f, cr.yMax - y - 10f)),
                    _groupOpen[0] || _groupOpen[1] || _groupOpen[2] || _groupOpen[3]
                        ? "点组名前面的 [−] 可以折叠这一组；滚轮上下滚动。"
                        : "点任意一组的组名（[+] 那一行）展开它，里面就是这一组的开关。\n所有功能默认都是关闭的，需要哪个自己打开。\n\n布尔项：点整行就能勾选/取消。\n数值项：点右边的数字框，直接打字改，回车生效。",
                    new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
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
            int w = UiSkin.Wheel();
            if (w != 0 && UiSkin.MouseOver(larea)) _scroll[1] = Mathf.Clamp(_scroll[1] + w, 0, maxScroll);

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
                Rect r = new Rect(larea.x, y, larea.width, rh);
                string txt = PlantDb.Label(p);
                try { txt += "   " + p.thePlantHealth + "/" + p.thePlantMaxHealth; } catch { }
                if (UiSkin.LeftButton(r, UiSkin.Fit(txt, 26), isSel, isSel ? UiSkin.ColGreen : UiSkin.ColText))
                    Actions.Select(p);
                y += rh;
            }

            Rect clr = new Rect(left.x + 4f, left.yMax - 26f, left.width - 8f, 22f);
            if (UiSkin.Button(clr, "取消选择", false, true)) { Actions.Select(null); }

            // ---------------- 右侧编辑器
            Fill(right, UiSkin.ColBack2);
            UiSkin.Border(right, UiSkin.ColLine);

            if (selP == null)
            {
                UiSkin.Text(new Rect(right.x + 10f, right.y + 10f, right.width - 20f, 120f),
                    "没有选中植物。\n\n1) 点一下场上任意植物上方的方框\n2) 或在左边列表里点一只植物\n\n选中后这里会出现它的全部数值（直接读当前值，改多少就是多少）。",
                    new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
                return;
            }

            long ptr = 0L;
            try { ptr = selP.Pointer.ToInt64(); } catch { }
            UiSkin.Text(new Rect(right.x + 8f, right.y + 3f, right.width - 16f, rh),
                "已选中: " + PlantDb.Label(selP) + "    指针 0x" + ptr.ToString("X"), UiSkin.Bold);

            // 两个数值列
            float colW = (right.width - 20f) / 2f;
            float fy = right.y + rh + 4f;
            int n = PlantDb.FieldCount;
            int rows = (n + 1) / 2;
            float avail = right.height - rh - 4f - 22f * 4f - 6f;
            float cellH = Mathf.Clamp(avail / Mathf.Max(1, rows), 17f, rh);

            for (int fi = 0; fi < n; fi++)
            {
                int col = fi / rows, row = fi % rows;
                Rect cell = new Rect(right.x + 6f + col * colW, fy + row * cellH, colW - 6f, cellH);
                bool editing = _editKind == EK_PLANT && _editField == fi && _editPtr == ptr;
                string val = PlantDb.GetLive(selP, fi);
                if (PlantDb.FieldKind(fi) == PlantDb.KText && string.IsNullOrEmpty(val)) val = "（空）";
                if (PlantDb.FieldKind(fi) != PlantDb.KText && !string.IsNullOrEmpty(PlantDb.FieldUnit(fi)))
                    val += PlantDb.FieldUnit(fi);
                bool ovr = PlantDb.IsOverridden(selP, fi);

                bool reset;
                if (ValueCell(cell, PlantDb.FieldName(fi), val, ovr, editing, UiSkin.ColText, out reset))
                {
                    if (reset) { PlantDb.ClearField(selP, fi); SetStatus("已还原 " + PlantDb.FieldName(fi)); }
                    else BeginEditPlant(selP, fi);
                }
            }

            // 机制勾选
            float gy = fy + rows * cellH + 4f;
            UiSkin.Text(new Rect(right.x + 8f, gy, right.width - 16f, 18f), "机制开关", UiSkin.Bold);
            gy += 19f;
            int fn = PlantDb.FlagCount;
            float fw = (right.width - 20f) / 2f;
            for (int i = 0; i < fn; i++)
            {
                int col = i / 3, row = i % 3;
                Rect r = new Rect(right.x + 6f + col * fw, gy + row * 20f, fw - 6f, 19f);
                bool v = PlantDb.GetFlag(selP, i);
                Rect box = new Rect(r.x + 2f, r.y + (r.height - 17f) * 0.5f, 17f, 17f);
                UiSkin.Checkbox(box, v);
                UiSkin.Text(new Rect(box.xMax + 6f, r.y, r.width - 24f, r.height), PlantDb.FlagName(i),
                    new UiSkin.TextOpt { Align = TextAnchor.MiddleLeft, Style = v ? FontStyle.Bold : FontStyle.Normal, Color = UiSkin.ColText });
                if (UiSkin.Click(r, 0)) { PlantDb.SetFlag(selP, i, !v); SetStatus(PlantDb.FlagName(i) + " = " + (!v)); }
            }

            float by = right.yMax - 24f;
            Rect b1 = new Rect(right.x + 6f, by, 150f, 21f);
            Rect b2 = new Rect(right.x + 162f, by, 110f, 21f);
            Rect b3 = new Rect(right.x + 278f, by, 110f, 21f);
            if (UiSkin.Button(b1, "还原该植物的全部修改", false, true))
            { PlantDb.ClearAll(selP); SetStatus("已还原该植物的全部修改"); }
            if (UiSkin.Button(b2, "刷新融合配方", false, true))
            { PlantDb.InvalidateRecipes(); Actions.InvalidateRecipes(); SetStatus("融合配方已刷新"); }
            if (UiSkin.Button(b3, "去融合页", false, true)) _tab = 2;
        }

        // ================================================================ 页 2 融合
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
            int w = UiSkin.Wheel();
            if (w != 0) _scroll[2] = Mathf.Max(0, _scroll[2] + w);

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
            if (w != 0 && UiSkin.MouseOver(listArea)) _scroll[2] = Mathf.Clamp(_scroll[2] + w, 0, maxScroll);

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

        // ================================================================ 页 3 动作
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

            UiSkin.Text(new Rect(cr.x + 4f, y, cr.width, 20f), "热键", UiSkin.Bold);
            y += 22f;
            UiSkin.Text(new Rect(cr.x + 8f, y, cr.width - 16f, 80f),
                "Insert —— 显示 / 隐藏这个菜单\n" +
                "F3 —— 开关植物 ESP 方框\n" +
                "鼠标左键点方框 —— 选中那株植物\n" +
                "编辑数值时：回车确认，Esc 取消，退格删除\n" +
                "鼠标压在菜单上时，游戏不会响应点击（不会误种植物）",
                new UiSkin.TextOpt { Align = TextAnchor.UpperLeft, Style = FontStyle.Normal, Color = UiSkin.ColDim, Dim = true });
        }

        // ================================================================ 类型选择器
        private static void OpenPicker(int target, int current)
        {
            _pickerOpen = true;
            _pickerTarget = target;
            _pickerFilter = "";
            _pickerPage = 0;
            PlantDb.FilterTypes("");
            SetStatus("选择植物类型：可以打字筛选，回车确认筛选内容");
            _ = current;
        }

        private static void DrawPicker()
        {
            float w = Mathf.Min(470f, Screen.width - 60f);
            float h = Mathf.Min(470f, Screen.height - 60f);
            Rect p = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            Fill(p, new Color(0.07f, 0.08f, 0.10f, 1f));
            UiSkin.Border(p, UiSkin.ColGreen);

            UiSkin.Text(new Rect(p.x + 10f, p.y + 4f, p.width - 20f, 24f),
                _pickerTarget == 0 ? "选择要变成的植物" : (_pickerTarget == 1 ? "选择融合的伙伴植物" : "选择融合结果植物"),
                UiSkin.Bold);

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

            var res = PlantDb.FilterTypes(_pickerFilter);
            float rh = RowH;
            int vis = Mathf.Max(1, (int)((p.height - 32f - 24f - 34f) / rh));
            int pages = Mathf.Max(1, (res.Count + vis - 1) / vis);
            if (_pickerPage >= pages) _pickerPage = pages - 1;
            if (_pickerPage < 0) _pickerPage = 0;

            int w0 = UiSkin.Wheel();
            if (w0 != 0 && UiSkin.MouseOver(p)) _pickerPage = Mathf.Clamp(_pickerPage + w0, 0, pages - 1);

            float y = p.y + 60f;
            int start = _pickerPage * vis;
            for (int i = start; i < res.Count && i < start + vis; i++)
            {
                int idx = res[i];
                int id = PlantDb.TypeIdAt(idx);
                Rect r = new Rect(p.x + 8f, y, p.width - 16f, rh);
                string txt = PlantDb.TypeNameAt(idx) + "   (#" + id + ")";
                if (UiSkin.LeftButton(r, txt, false, UiSkin.ColText))
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
                    else
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
