using System;
using System.Collections.Generic;
using UnityEngine;

namespace PvzRhCheat
{
    /// <summary>
    /// 经典 Lua 式外挂菜单（纯 IMGUI 手绘）+ 植物 ESP。
    ///
    /// 这套 IL2CPP 构建的三个坑（全部已在运行时自动处理）：
    ///  1) 默认皮肤 GUI.skin.label.font = NULL  -> 任何 IMGUI 文字都画不出来
    ///     处理：从 Resources.FindObjectsOfTypeAll&lt;Font&gt;() 抓字体补上
    ///  2) GUI.DrawTexture 被剥离（调用即 NotSupportedException）
    ///     处理：把 GUI.skin.box.normal.background 换成 Texture2D.whiteTexture，
    ///           用 GUI.Box + GUI.color 画不透明纯色矩形
    ///  3) GUI.TextField 被剥离
    ///     处理：自绘文本输入框（捕获 Event.character / Backspace）
    ///
    /// 文案：中文优先，字体缺中文字形时自动回退英文。
    /// 勾选标记：用方块沿笔画铺出手绘 ✓，不依赖字体是否含该字形。
    /// </summary>
    public class EspOverlay : MonoBehaviour
    {
        // ---------------------------------------------------------------- 公开状态
        public static bool ShowMenu  = true;
        public static bool ShowEsp   = true;
        public static bool ShowName  = true;
        public static bool ShowHp    = true;
        public static bool ShowIndex = false;
        public static float EspHeight = 0.75f;
        public static float EspWidth  = 100f;

        private Rect  _win = new Rect(40f, 40f, 740f, 680f);
        private int   _tab;
        private float _scroll;
        private float _contentH = 0f;     // 上一帧实测的内容总高（用于滚动条与 clamp）
        private bool  _dragging;
        private Vector2 _dragOff;
        private bool  _editing;           // 植物页：false=列表 true=编辑器
        private int   _hotSlider = -1;
        private static int _sliderSeq;
        private static bool _diagLogged, _probed;

        private static bool _cjk = true;      // 字体是否含中文字形
        private static string L(string zh, string en) { return _cjk ? zh : en; }


        // ---------------------------------------------------------------- 配色（不透明）
        private static readonly Color CBg      = new Color(0.055f, 0.065f, 0.085f, 1f);
        private static readonly Color CHead    = new Color(0.085f, 0.10f, 0.135f, 1f);
        private static readonly Color CBorder  = new Color(0.25f, 0.31f, 0.40f, 1f);
        private static readonly Color CTabOn   = new Color(0.10f, 0.55f, 0.88f, 1f);
        private static readonly Color CTabOff  = new Color(0.115f, 0.135f, 0.175f, 1f);
        private static readonly Color CAccent  = new Color(0.20f, 0.85f, 0.45f, 1f);
        private static readonly Color CBoxOn   = new Color(0.13f, 0.60f, 0.32f, 1f);
        private static readonly Color CBoxOff  = new Color(0.08f, 0.09f, 0.115f, 1f);
        private static readonly Color CText    = new Color(0.92f, 0.95f, 0.98f, 1f);
        private static readonly Color CDim     = new Color(0.62f, 0.67f, 0.74f, 1f);
        private static readonly Color CTrack   = new Color(0.07f, 0.08f, 0.10f, 1f);
        private static readonly Color CRowAlt  = new Color(0.075f, 0.088f, 0.115f, 1f);
        private static readonly Color CHover   = new Color(0.13f, 0.16f, 0.21f, 1f);
        private static readonly Color CBtn     = new Color(0.115f, 0.14f, 0.18f, 1f);

        private const float RowH    = 27f;
        private const float HeaderH = 30f;
        private const float TabH    = 34f;
        private const float PadX    = 14f;    // 左右留白
        private const float PadBottom = 14f;  // 内容底部留白（避免最后一行被挡）

        // ---------------------------------------------------------------- 绘制原语
        private static bool _fillReady;

        /// <summary>把 box 样式背景换成纯白贴图 -> GUI.Box 变成不透明纯色填充</summary>
        private static void PrepareFill()
        {
            if (_fillReady) return;
            _fillReady = true;
            try
            {
                // 背景换成 1x1 白贴图后，border 拉伸的是同一个像素，无需修改
                GUI.skin.box.normal.background = Texture2D.whiteTexture;
                Plugin.Log.LogInfo("[UI] 已把 box 背景替换为纯白贴图（不透明填充）");
                return;
            }
            catch (Exception e) { Plugin.LogOnce("Fill/prepare", e); }

            // 退路：探测 DrawTexture
            try
            {
                Color oc = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0f);
                GUI.DrawTexture(new Rect(-500f, -500f, 1f, 1f), Texture2D.whiteTexture);
                GUI.color = oc;
                _useDrawTexture = true;
                Plugin.Log.LogInfo("[UI] 退路：使用 GUI.DrawTexture 填充");
            }
            catch { Plugin.Log.LogInfo("[UI] 退路：使用半透明 GUI.Box 填充"); }
        }

        private static bool _useDrawTexture;

        private static void Fill(Rect r, Color c)
        {
            PrepareFill();
            // 取整到整像素，避免亚像素采样发糊
            r = new Rect(Mathf.Round(r.x), Mathf.Round(r.y), Mathf.Round(r.width), Mathf.Round(r.height));
            if (r.width < 1f || r.height < 1f) return;
            Color o = GUI.color;
            GUI.color = c;
            try
            {
                if (_useDrawTexture) GUI.DrawTexture(r, Texture2D.whiteTexture);
                else GUI.Box(r, "");
            }
            catch (Exception e) { Plugin.LogOnce("Fill", e); }
            GUI.color = o;
        }

        private static void Text(Rect r, string s, Color c, TextAnchor a = TextAnchor.MiddleLeft, bool bold = false)
        {
            try
            {
                GUIStyle st = GUI.skin.label;
                TextAnchor oa = st.alignment;
                Color oc = GUI.contentColor;
                FontStyle of = st.fontStyle;
                st.alignment = a;
                st.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
                GUI.contentColor = c;
                GUI.Label(r, s);
                st.alignment = oa;
                st.fontStyle = of;
                GUI.contentColor = oc;
            }
            catch (Exception e) { Plugin.LogOnce("Text", e); }
        }

        /// <summary>1px 边框 + 内部填充</summary>
        private static void Frame(Rect r, Color fill, Color border)
        {
            Fill(r, border);
            Fill(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), fill);
        }

        /// <summary>勾选符号：优先用字体里的 ✓ / √，都没有才退回 ASCII 的 v</summary>
        private static string _glyph;

        private static string CheckGlyph()
        {
            if (_glyph != null) return _glyph;
            _glyph = "";
            Font f = null;
            try { f = GUI.skin.label.font; } catch { }
            try { if (f != null && f.HasCharacter('\u2713')) _glyph = "\u2713"; } catch { }   // ✓
            if (_glyph.Length == 0) { try { if (f != null && f.HasCharacter('\u221A')) _glyph = "\u221A"; } catch { } }  // √
            if (_glyph.Length == 0) { try { if (f != null && f.HasCharacter('\u2714')) _glyph = "\u2714"; } catch { } }  // ✔
            if (_glyph.Length == 0) _glyph = "v";
            Plugin.Log.LogInfo("[UI] 勾选符号 = " + _glyph + "  U+" + ((int)_glyph[0]).ToString("X4"));
            return _glyph;
        }

        // ---------------------------------------------------------------- 主循环
        private void OnGUI()
        {
            try
            {
                Diag();
                Probe();
                Keys();
                if (ShowEsp) { try { DrawEsp(); } catch (Exception e) { Plugin.LogOnce("DrawEsp", e); } }
                if (ShowMenu) { try { DrawMenu(); } catch (Exception e) { Plugin.LogOnce("DrawMenu", e); } }
            }
            catch (Exception e) { Plugin.LogOnce("OnGUI", e); }
        }

        private static void Diag()
        {
            if (_diagLogged) return;
            _diagLogged = true;
            string name = "NULL";
            try
            {
                GUIStyle st = GUI.skin.label;
                if (st.font != null) name = st.font.name;
            }
            catch (Exception e) { Plugin.LogOnce("Diag", e); }

            if (name == "NULL")
            {
                try
                {
                    var all = Resources.FindObjectsOfTypeAll<Font>();
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null) continue;
                        GUI.skin.label.font = all[i];
                        try { GUI.skin.box.font = all[i]; } catch { }
                        try { GUI.skin.button.font = all[i]; } catch { }
                        name = all[i].name;
                        Plugin.Log.LogInfo("[UI] 皮肤缺少字体，已补: " + name);
                        break;
                    }
                }
                catch (Exception e) { Plugin.LogOnce("Diag/font", e); }
            }

            try
            {
                Font f = GUI.skin.label.font;
                if (f != null) _cjk = f.HasCharacter('中') && f.HasCharacter('文');
            }
            catch (Exception e) { Plugin.LogOnce("Diag/cjk", e); }

            Plugin.Log.LogInfo("[UI] 字体=" + name + "  含中文=" + _cjk +
                               "  文案=" + (_cjk ? "中文" : "英文"));
        }

        /// <summary>一次性探测被剥离的 API（只记录不支持的）</summary>
        private static void Probe()
        {
            if (_probed) return;
            _probed = true;
            P("GUI.DrawTexture", () => { var oc = GUI.color; GUI.color = Color.white; GUI.DrawTexture(new Rect(0f, 0f, 1f, 1f), Texture2D.whiteTexture); GUI.color = oc; });
            P("GUI.Box", () => { var oc = GUI.color; GUI.color = Color.white; GUI.Box(new Rect(-500f, -500f, 1f, 1f), ""); GUI.color = oc; });
            P("GUI.Label", () => GUI.Label(new Rect(0f, 0f, 1f, 1f), "x"));
            P("GUI.BeginGroup", () => { GUI.BeginGroup(new Rect(0f, 0f, 1f, 1f)); GUI.EndGroup(); });
            P("GUIStyle.normal.background", () => { var b = GUI.skin.box.normal.background; GUI.skin.box.normal.background = b; });
            P("GUIStyle.border", () => { var b = GUI.skin.box.border; GUI.skin.box.border = b; });
            P("GUIStyle.alignment", () => { var st = GUI.skin.label; var a = st.alignment; st.alignment = a; });
            P("GUIStyle.fontStyle", () => { var st = GUI.skin.label; var a = st.fontStyle; st.fontStyle = a; });
            P("Camera.main", () => { var c = Camera.main; });
            P("FindObjectsOfType<Plant>", () => { var a = UnityEngine.Object.FindObjectsOfType<Plant>(); });
        }

        private static void P(string name, Action a)
        {
            try { a(); }
            catch (Exception e) { Plugin.Log.LogInfo("[探针] 该构建不支持 " + name + " (" + e.GetType().Name + ")"); }
        }

        private void Keys()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Insert) { ShowMenu = !ShowMenu; e.Use(); }
            else if (e.keyCode == KeyCode.F3) { ShowEsp = !ShowEsp; e.Use(); }
            else if (e.keyCode == KeyCode.F4) { Overrides.GlobalEnabled = !Overrides.GlobalEnabled; e.Use(); }
        }

        // ---------------------------------------------------------------- 菜单
        private void DrawMenu()
        {
            var head = new Rect(_win.x, _win.y, _win.width, HeaderH);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && head.Contains(e.mousePosition))
            {
                _dragging = true;
                _dragOff = e.mousePosition - new Vector2(_win.x, _win.y);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _dragging)
            {
                _win.x = e.mousePosition.x - _dragOff.x;
                _win.y = e.mousePosition.y - _dragOff.y;
                e.Use();
            }
            else if (e.type == EventType.MouseUp) { _dragging = false; }

            Frame(_win, CBg, CBorder);
            Fill(head, CHead);
            Fill(new Rect(head.x, head.y, 4f, head.height), CAccent);
            Text(new Rect(head.x + 14f, head.y, 380f, head.height), L("PVZ 融合版 3.9  |  修改器", "PVZ FUSION 3.9  |  MOD MENU"), CText);

            if (SmallButton(new Rect(_win.xMax - 28f, head.y + 5f, 20f, 18f), "X")) ShowMenu = false;
            if (SmallButton(new Rect(_win.xMax - 52f, head.y + 5f, 20f, 18f), _win.height > 200f ? "-" : "+"))
                _win.height = _win.height > 200f ? 64f : 600f;

            string[] tabs =
            {
                L("功能", "FEATURES"), L("全局植物", "GLOBAL PLANT"),
                L("植物", "PLANTS"), L("ESP/热键", "ESP / KEYS")
            };
            float tw = (_win.width - 8f) / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                var r = new Rect(_win.x + 4f + i * tw, _win.y + HeaderH, tw - 2f, TabH - 6f);
                bool on = _tab == i;
                Fill(r, on ? CTabOn : CTabOff);
                if (!on && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                {
                    _tab = i; _scroll = 0f; _contentH = 0f; e.Use();
                }
                Text(r, tabs[i], on ? Color.white : CDim, TextAnchor.MiddleCenter);
            }

            if (_win.height <= 64f) return;

            var view = new Rect(_win.x + 4f, _win.y + HeaderH + TabH, _win.width - 8f,
                                _win.height - HeaderH - TabH - 26f);
            Fill(view, CBg);

            if (e.type == EventType.ScrollWheel && view.Contains(e.mousePosition))
            {
                _scroll += e.delta.y * 22f;
                e.Use();
            }

            // 用「上一帧实测的内容高度」算 clamp 与滚动条，避免估算不准导致底部够不到
            float maxScroll = Mathf.Max(0f, _contentH - view.height);
            _scroll = Mathf.Clamp(_scroll, 0f, maxScroll);

            GUI.BeginGroup(view);
            float y = 10f - _scroll;
            DrawContent(ref y, view.width);
            _contentH = (y + _scroll) + PadBottom;     // 实测内容总高 + 底部留白
            GUI.EndGroup();

            if (_contentH > view.height + 0.5f)
            {
                float barH = Mathf.Max(34f, view.height * (view.height / _contentH));
                float t = maxScroll <= 0f ? 0f : Mathf.Clamp01(_scroll / maxScroll);
                Fill(new Rect(view.xMax - 6f, view.y, 5f, view.height), CTrack);
                Fill(new Rect(view.xMax - 6f, view.y + t * (view.height - barH), 5f, barH), CAccent);
            }

            var foot = new Rect(_win.x, _win.yMax - 24f, _win.width, 24f);
            Fill(foot, CHead);
            Text(new Rect(foot.x + PadX, foot.y, foot.width - PadX * 2f, foot.height),
                 L("关卡: ", "Level: ") + Actions.LevelInfo() +
                 L("    植物: ", "    plants: ") + Plants.Count +
                 (_editing ? L("    [编辑中]", "    [editing]") : ""), CDim);
        }

        private bool SmallButton(Rect r, string label)
        {
            Event e = Event.current;
            bool hot = r.Contains(e.mousePosition);
            Frame(r, hot ? CTabOn : CBtn, CBorder);
            Text(r, label, hot ? Color.white : CDim, TextAnchor.MiddleCenter);
            if (hot && e.type == EventType.MouseDown && e.button == 0) { e.Use(); return true; }
            return false;
        }

        // ---------------------------------------------------------------- 内容
        private static List<Plant> Plants => Actions.PlantsSnapshot();


        private void DrawContent(ref float y, float w)
        {
            _sliderSeq = 0;
            if (_tab == 0) { DrawFeatures(ref y, w); return; }
            if (_tab == 1) { DrawGlobalPlant(ref y, w); return; }
            if (_tab == 2) { DrawPlants(ref y, w); return; }
            DrawEspTab(ref y, w);
        }

        private void DrawFeatures(ref float y, float w)
        {
            Section(ref y, w, L("解锁 / 资源", "UNLOCK / RESOURCES"));
            ModConfig.UnlockAllLevels.Value = Toggle(ref y, w, L("解锁全部关卡", "Unlock all levels"), ModConfig.UnlockAllLevels.Value);
            ModConfig.DeveloperMode.Value   = Toggle(ref y, w, L("开发者模式", "Developer mode"), ModConfig.DeveloperMode.Value);
            ModConfig.Money.Value           = LongSlider(ref y, w, L("金币", "Money"), ModConfig.Money.Value, 0, 999_999_999);
            ModConfig.InfiniteSun.Value     = Toggle(ref y, w, L("无限阳光", "Infinite sun"), ModConfig.InfiniteSun.Value);
            ModConfig.SunFloor.Value        = IntRow(ref y, w, L("阳光维持下限", "Sun floor"), ModConfig.SunFloor.Value, 0, 99999, true);
            ModConfig.AbyssMaxTickets.Value      = Toggle(ref y, w, L("深渊抽奖券拉满", "Abyss tickets maxed"), ModConfig.AbyssMaxTickets.Value);
            ModConfig.AbyssInfiniteTickets.Value = Toggle(ref y, w, L("深渊抽奖券不消耗", "Abyss tickets unlimited"), ModConfig.AbyssInfiniteTickets.Value);

            Section(ref y, w, L("战斗", "COMBAT"));
            ModConfig.GodModePlants.Value = Toggle(ref y, w, L("植物无敌", "God mode plants"), ModConfig.GodModePlants.Value);
            ModConfig.OneHitZombies.Value = Toggle(ref y, w, L("僵尸一击必杀", "One-hit zombies"), ModConfig.OneHitZombies.Value);
            ModConfig.PlantDamageMultiplier.Value =
                Slider(ref y, w, L("植物输出倍率", "Plant damage x"), ModConfig.PlantDamageMultiplier.Value, 0.1f, 50f);
            ModConfig.ZombieDamageTakenMultiplier.Value =
                Slider(ref y, w, L("僵尸受伤倍率", "Zombie dmg taken x"), ModConfig.ZombieDamageTakenMultiplier.Value, 0.1f, 50f);

            Section(ref y, w, L("旅行 / 词条", "TRAVEL / BUFF POOL"));
            ModConfig.TravelBuffs.Value = Toggle(ref y, w, L("旅行模式强化", "Travel buffs"), ModConfig.TravelBuffs.Value);
            ModConfig.TravelDamageReduction.Value =
                Slider(ref y, w, L("伤害减免", "Damage reduction"), ModConfig.TravelDamageReduction.Value, 0f, 1f);
            ModConfig.TravelLuckyStrike.Value =
                Slider(ref y, w, L("幸运一击", "Lucky strike"), ModConfig.TravelLuckyStrike.Value, 0f, 10f);
            ModConfig.TravelDamageAmplification.Value =
                Slider(ref y, w, L("伤害增幅", "Damage amp x"), ModConfig.TravelDamageAmplification.Value, 0f, 30f);
            ModConfig.TravelPlantZeroHealth.Value = Toggle(ref y, w, "plantZeroHealth", ModConfig.TravelPlantZeroHealth.Value);
            ModConfig.BuffWhitelist.Value = TextRow(ref y, w, "Buff",
                L("普通词条白名单（点击输入，回车确认）", "Buff whitelist (csv)"), ModConfig.BuffWhitelist.Value);
            ModConfig.UltiBuffWhitelist.Value = TextRow(ref y, w, "Ulti",
                L("终极词条白名单", "Ulti whitelist (csv)"), ModConfig.UltiBuffWhitelist.Value);

            Section(ref y, w, L("天赋", "TALENTS"));
            ModConfig.TalentUnlockAll.Value = Toggle(ref y, w, L("解锁全部天赋", "Unlock all talents"), ModConfig.TalentUnlockAll.Value);
            ModConfig.TalentStars.Value     = IntRow(ref y, w, L("天赋星星", "Talent stars"), ModConfig.TalentStars.Value, 0, 99999, true);
            ModConfig.TalentFreeHardModeOff.Value = Toggle(ref y, w, L("关闭困难模式", "Disable hard mode"), ModConfig.TalentFreeHardModeOff.Value);

            Section(ref y, w, L("其它", "MISC"));
            ModConfig.Enabled.Value = Toggle(ref y, w, L("总开关", "MASTER SWITCH"), ModConfig.Enabled.Value);
            ModConfig.ApplyInterval.Value = Slider(ref y, w, L("施加间隔(秒)", "Apply interval s"), ModConfig.ApplyInterval.Value, 0f, 10f);
        }

        private void DrawGlobalPlant(ref float y, float w)
        {
            Section(ref y, w, L("全局（对所有植物生效）", "GLOBAL (applies to every plant)"));
            Overrides.GlobalEnabled = Toggle(ref y, w, L("启用全局植物默认", "Enable global defaults"), Overrides.GlobalEnabled);
            var g = Overrides.Global;
            g.GodMode       = Toggle(ref y, w, L("免伤", "No damage"), g.GodMode);
            g.Invincible    = Toggle(ref y, w, "invincible", g.Invincible);
            g.Undead        = Toggle(ref y, w, "undead", g.Undead);
            g.KeepShooting  = Toggle(ref y, w, "keepShooting", g.KeepShooting);
            g.AlwaysLightUp = Toggle(ref y, w, "alwaysLightUp", g.AlwaysLightUp);
            g.Uncrashable   = Toggle(ref y, w, "uncrashable", g.Uncrashable);

            Section(ref y, w, L("数值（-1 / 1 = 不修改）", "STATS  (-1 / 1 = keep)"));
            g.MaxHealth      = IntRow(ref y, w, L("最大生命", "Max HP"), g.MaxHealth, -1, 100000, false);
            g.AttackDamage   = IntRow(ref y, w, L("攻击力", "Attack"), g.AttackDamage, -1, 100000, false);
            g.Level          = IntRow(ref y, w, L("等级", "Level"), g.Level, -1, 99, false);
            g.AttackInterval = Slider(ref y, w, L("攻击间隔(秒)", "Attack interval s"), g.AttackInterval, -1f, 5f);
            g.SpeedMult      = Slider(ref y, w, L("攻速倍率", "Attack speed x"), g.SpeedMult, 0.1f, 20f);
            g.DamageMult     = Slider(ref y, w, L("伤害倍率", "Damage x"), g.DamageMult, 0.1f, 50f);

            Section(ref y, w, L("模型 / 子弹", "MODEL / BULLET"));
            g.SkinType = IntRow(ref y, w, L("皮肤 skinType", "Skin skinType"), g.SkinType, -1, 64, false);
            g.Scale    = Slider(ref y, w, L("缩放", "Scale"), g.Scale, -1f, 5f);
            g.BulletType = IntRow(ref y, w, L("子弹类型 ID", "BulletType id"), g.BulletType, -1, 300, false);
            g.BulletDamageMult = Slider(ref y, w, L("子弹伤害倍率", "Bullet damage x"), g.BulletDamageMult, 0.1f, 50f);
            g.BulletSpeedMult  = Slider(ref y, w, L("子弹速度倍率", "Bullet speed x"), g.BulletSpeedMult, 0.1f, 10f);
            g.BulletPierce     = IntRow(ref y, w, L("穿透 maxHitCount", "Pierce maxHitCount"), g.BulletPierce, -1, 500, false);

            if (Button(ref y, w, L("重置全局植物默认", "Reset global defaults"))) g.Reset();
        }

        private void DrawPlants(ref float y, float w)
        {
            if (!_editing || Actions.SelectedPlant() == null) DrawPlantList(ref y, w);
            else DrawPlantEditor(ref y, w);
        }

        private void DrawPlantList(ref float y, float w)
        {
            Section(ref y, w, L("场景中的植物（点一行进入编辑）", "LIVE PLANTS (click a row to edit)"));

            // 关卡类型 + 数据来源，用来判断为什么"没有植物"
            Text(new Rect(PadX, y, w - PadX * 2f, RowH),
                 L("关卡: ", "Level: ") + Actions.LevelInfo() +
                 L("    来源: ", "    source: ") + Actions.PlantSource() +
                 L("    花园植物: ", "    garden: ") + Actions.GardenCount(), CDim);
            y += RowH;

            if (Plants.Count == 0)
            {
                Text(new Rect(PadX, y, w - PadX * 2f, RowH),
                     Actions.IsInLevel()
                        ? L("这一关目前没有植物", "no plants in this level right now")
                        : L("不在关卡内（请先进入一关）", "not in a level - enter one first"), CDim);
                y += RowH;
                if (Actions.GardenCount() > 0)
                {
                    Text(new Rect(PadX, y, w - PadX * 2f, RowH),
                         L("检测到花园植物（GardenPlant），本编辑器不支持该类型",
                           "garden plants detected (GardenPlant) - unsupported type"), CDim);
                    y += RowH;
                }
                return;
            }

            for (int i = 0; i < Plants.Count; i++)
            {
                Plant p = Plants[i];
                var r = new Rect(6f, y, w - 12f, RowH - 3f);
                bool sel = Actions.IsSelected(p);
                Fill(r, sel ? CTabOn : (i % 2 == 1 ? CRowAlt : CBg));
                if (!sel && r.Contains(Event.current.mousePosition)) Fill(r, CHover);

                string type = "?";
                int hp = 0, mx = 0, row = 0, col = 0;
                try
                {
                    type = p.thePlantType.ToString();
                    hp = p.thePlantHealth; mx = p.thePlantMaxHealth;
                    row = p.thePlantRow; col = p.thePlantColumn;
                }
                catch { }
                Text(new Rect(r.x + 12f, r.y, w - 280f, r.height),
                     "#" + i + "   " + type, sel ? Color.white : CText);
                Text(new Rect(r.xMax - 260f, r.y, 120f, r.height),
                     L("行", "r") + row + L(" 列", " c") + col, sel ? Color.white : CDim);
                Text(new Rect(r.xMax - 130f, r.y, 118f, r.height),
                     hp + "/" + mx, sel ? Color.white : CDim, TextAnchor.MiddleRight);

                Event e = Event.current;
                if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                {
                    Actions.Select(p); _editing = true; _scroll = 0f; e.Use();
                }
                y += RowH;
            }
        }

        private void DrawPlantEditor(ref float y, float w)
        {
            Plant p = Actions.SelectedPlant();
            if (p == null) { _editing = false; return; }

            if (Button(ref y, w, L("< 返回植物列表", "< BACK TO LIST"))) { _editing = false; _scroll = 0f; return; }

            string type = "?";
            int hp = 0, mx = 0, row = 0, col = 0;
            try
            {
                type = p.thePlantType.ToString();
                hp = p.thePlantHealth; mx = p.thePlantMaxHealth; row = p.thePlantRow; col = p.thePlantColumn;
            }
            catch { }
            Section(ref y, w, L("正在编辑: ", "EDITING: ") + type);
            Text(new Rect(12f, y, w - 24f, RowH),
                 L("行 ", "row ") + row + L("  列 ", "  col ") + col + L("   生命 ", "   HP ") + hp + "/" + mx, CDim);
            y += RowH;

            var o = Overrides.GetOrCreate(p);
            if (o == null) return;

            Section(ref y, w, L("机制", "MECHANICS"));
            o.GodMode       = Toggle(ref y, w, L("免伤", "No damage"), o.GodMode);
            o.Invincible    = Toggle(ref y, w, "invincible", o.Invincible);
            o.Undead        = Toggle(ref y, w, "undead", o.Undead);
            o.KeepShooting  = Toggle(ref y, w, "keepShooting", o.KeepShooting);
            o.AlwaysLightUp = Toggle(ref y, w, "alwaysLightUp", o.AlwaysLightUp);
            o.Uncrashable   = Toggle(ref y, w, "uncrashable", o.Uncrashable);

            Section(ref y, w, L("数值（-1 / 1 = 不修改）", "STATS  (-1 / 1 = keep)"));
            o.MaxHealth      = IntRow(ref y, w, L("最大生命", "Max HP"), o.MaxHealth, -1, 100000, false);
            o.AttackDamage   = IntRow(ref y, w, L("攻击力", "Attack"), o.AttackDamage, -1, 100000, false);
            o.Level          = IntRow(ref y, w, L("等级", "Level"), o.Level, -1, 99, false);
            o.AttackInterval = Slider(ref y, w, L("攻击间隔(秒)", "Attack interval s"), o.AttackInterval, -1f, 5f);
            o.SpeedMult      = Slider(ref y, w, L("攻速倍率", "Attack speed x"), o.SpeedMult, 0.1f, 20f);
            o.DamageMult     = Slider(ref y, w, L("伤害倍率", "Damage x"), o.DamageMult, 0.1f, 50f);
            o.Defence        = Slider(ref y, w, L("防御", "Defence"), o.Defence, -1f, 500f);

            Section(ref y, w, L("模型 / 皮肤", "MODEL / SKIN"));
            o.SkinType = IntRow(ref y, w, L("皮肤 skinType", "Skin skinType"), o.SkinType, -1, 64, false);
            o.Scale    = Slider(ref y, w, L("缩放", "Scale"), o.Scale, -1f, 5f);
            if (Button(ref y, w, L("立即刷新外观", "Refresh sprite now")))
            {
                try { p.ReplaceSprite(); } catch (Exception e) { Plugin.LogOnce("ReplaceSprite", e); }
            }

            Section(ref y, w, L("子弹", "BULLET"));
            o.BulletType = IntRow(ref y, w, L("子弹类型 ID", "BulletType id"), o.BulletType, -1, 300, false);
            o.BulletDamageMult = Slider(ref y, w, L("子弹伤害倍率", "Bullet damage x"), o.BulletDamageMult, 0.1f, 50f);
            o.BulletSpeedMult  = Slider(ref y, w, L("子弹速度倍率", "Bullet speed x"), o.BulletSpeedMult, 0.1f, 10f);
            o.BulletPierce     = IntRow(ref y, w, L("穿透 maxHitCount", "Pierce maxHitCount"), o.BulletPierce, -1, 500, false);

            Section(ref y, w, L("操作", "ACTIONS"));
            if (Button(ref y, w, L("把这株的设置复制到全局默认", "Copy this to GLOBAL defaults"))) CopyTo(o, Overrides.Global);
            if (Button(ref y, w, L("重置本株", "Reset this plant"))) o.Reset();
        }

        private void DrawEspTab(ref float y, float w)
        {
            Section(ref y, w, L("ESP 叠加层", "ESP OVERLAY"));
            ShowEsp   = Toggle(ref y, w, L("显示 ESP 方框", "Show ESP boxes"), ShowEsp);
            ShowName  = Toggle(ref y, w, L("显示植物名", "Show plant name"), ShowName);
            ShowHp    = Toggle(ref y, w, L("显示血量", "Show HP"), ShowHp);
            ShowIndex = Toggle(ref y, w, L("显示序号", "Show index"), ShowIndex);
            EspHeight = Slider(ref y, w, L("标签高度（世界Y偏移）", "Label height (world Y)"), EspHeight, 0f, 3f);
            EspWidth  = Slider(ref y, w, L("标签宽度（像素）", "Label width (px)"), EspWidth, 40f, 300f);

            Section(ref y, w, L("热键", "HOTKEYS"));
            Text(new Rect(12f, y, w - 24f, RowH), L("INSERT  -  显示 / 隐藏本菜单", "INSERT  -  show / hide this menu"), CDim); y += RowH;
            Text(new Rect(12f, y, w - 24f, RowH), L("F3      -  显示 / 隐藏 ESP 方框", "F3      -  show / hide ESP boxes"), CDim); y += RowH;
            Text(new Rect(12f, y, w - 24f, RowH), L("F4      -  切换全局植物默认", "F4      -  toggle global plant defaults"), CDim); y += RowH;

            Section(ref y, w, L("信息", "INFO"));
            Text(new Rect(12f, y, w - 24f, RowH), L("场景植物数: ", "plants in scene: ") + Plants.Count, CDim); y += RowH;
            if (Button(ref y, w, L("清空全部单株覆盖", "Clear ALL per-plant overrides"))) { Overrides.ClearAll(); _editing = false; }
        }

        // ---------------------------------------------------------------- 控件
        private void Section(ref float y, float w, string title)
        {
            var r = new Rect(4f, y + 4f, w - 8f, 20f);
            Fill(r, CRowAlt);
            Fill(new Rect(r.x, r.y, 3f, r.height), CAccent);
            Text(new Rect(r.x + 12f, r.y, w - 30f, r.height), title, CAccent);
            y += 26f;
        }

        /// <summary>勾选框：绿色实底 + 字体对勾字形（清晰、不糊）</summary>
        private bool Toggle(ref float y, float w, string label, bool value)
        {
            var r = new Rect(6f, y, w - 12f, RowH - 2f);
            Event e = Event.current;
            bool hot = r.Contains(e.mousePosition);
            if (hot) Fill(r, CHover);

            var box = new Rect(r.x + 8f, r.y + 4f, 16f, 16f);
            if (value)
            {
                Fill(box, CBoxOn);
                Text(box, CheckGlyph(), Color.white, TextAnchor.MiddleCenter, true);
            }
            else
            {
                Frame(box, CBoxOff, CBorder);
            }
            Text(new Rect(r.x + 32f, r.y, w - 54f, r.height), label, value ? CText : CDim);

            if (hot && e.type == EventType.MouseDown && e.button == 0) { e.Use(); y += RowH; return !value; }
            y += RowH;
            return value;
        }

        private float Slider(ref float y, float w, string label, float value, float min, float max)
        {
            var r = new Rect(6f, y, w - 12f, RowH - 2f);
            Text(new Rect(r.x + 10f, r.y, 230f, r.height), label, CText);
            Text(new Rect(r.xMax - 74f, r.y, 66f, r.height), value.ToString("0.###"), CAccent, TextAnchor.MiddleRight);

            var track = new Rect(r.x + 250f, r.y + 9f, Mathf.Max(40f, r.width - 336f), 6f);
            Fill(track, CTrack);
            float t = Mathf.Clamp01(max > min ? (value - min) / (max - min) : 0f);
            Fill(new Rect(track.x, track.y, track.width * t, track.height), CAccent);
            Fill(new Rect(track.x + track.width * t - 2f, track.y - 4f, 5f, track.height + 8f), Color.white);

            int id = ++_sliderSeq;
            Event e = Event.current;
            var hit = new Rect(track.x - 6f, track.y - 8f, track.width + 12f, track.height + 16f);
            if (e.type == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition)) { _hotSlider = id; e.Use(); }
            if (_hotSlider == id && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            {
                float nt = Mathf.Clamp01((e.mousePosition.x - track.x) / track.width);
                value = min + nt * (max - min);
                e.Use();
            }
            if (e.type == EventType.MouseUp && _hotSlider == id) _hotSlider = -1;

            y += RowH;
            return value;
        }

        private int IntRow(ref float y, float w, string label, int value, int min, int max, bool slider)
        {
            if (slider)
            {
                float f = Slider(ref y, w, label, value, min, max);
                return (int)Math.Round(f);
            }
            var r = new Rect(6f, y, w - 12f, RowH - 2f);
            Text(new Rect(r.x + 10f, r.y, 230f, r.height), label, CText);

            var box = new Rect(r.x + 250f, r.y + 2f, 72f, RowH - 6f);
            Frame(box, CBoxOff, CBorder);
            Text(box, value.ToString(), CAccent, TextAnchor.MiddleCenter);

            float bx = box.xMax + 6f;
            if (SmallButton(new Rect(bx, box.y, 24f, box.height), "-")) value = Math.Max(min, value - 1);
            if (SmallButton(new Rect(bx + 26f, box.y, 24f, box.height), "+")) value = Math.Min(max, value + 1);
            if (SmallButton(new Rect(bx + 54f, box.y, 46f, box.height), "-10")) value = Math.Max(min, value - 10);
            if (SmallButton(new Rect(bx + 102f, box.y, 46f, box.height), "+10")) value = Math.Min(max, value + 10);

            y += RowH;
            return value;
        }

        private long LongSlider(ref float y, float w, string label, long value, long min, long max)
        {
            float f = Slider(ref y, w, label, value, min, max);
            return (long)Math.Round(f);
        }

        // 自绘文本输入框（GUI.TextField 被剥离）
        private string _focusKey;
        private string _editText = "";

        private string TextRow(ref float y, float w, string key, string label, string value)
        {
            Text(new Rect(12f, y, w - 24f, 16f), label, CDim);
            y += 18f;

            var r = new Rect(10f, y, w - 20f, RowH - 4f);
            bool focused = _focusKey == key;
            Frame(r, focused ? new Color(0.09f, 0.13f, 0.19f, 1f) : CBoxOff, focused ? CAccent : CBorder);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (r.Contains(e.mousePosition)) { _focusKey = key; _editText = value ?? ""; e.Use(); }
                else if (focused) _focusKey = null;
            }

            if (focused)
            {
                if (e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.Backspace)
                    {
                        if (_editText.Length > 0) _editText = _editText.Substring(0, _editText.Length - 1);
                        e.Use();
                    }
                    else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape)
                    {
                        _focusKey = null; e.Use();
                    }
                    else if (e.character != '\0' && e.character >= ' ')
                    {
                        _editText += e.character; e.Use();
                    }
                }
                if (_editText != value) { value = _editText; Actions.InvalidateBuffCache(); }
                float cx = r.x + 6f + EstimateWidth(_editText);
                Fill(new Rect(cx, r.y + 3f, 1.5f, r.height - 6f), CAccent);
            }

            Text(new Rect(r.x + 6f, r.y, r.width - 12f, r.height),
                 focused ? _editText : (value ?? ""), focused ? Color.white : CText);
            y += RowH;
            return value;
        }

        private static float EstimateWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            float w = 0f;
            for (int i = 0; i < s.Length; i++) w += s[i] > 127 ? 13f : 7f;
            return w;
        }

        private bool Button(ref float y, float w, string label)
        {
            var r = new Rect(6f, y, w - 12f, RowH + 2f);
            Event e = Event.current;
            bool hot = r.Contains(e.mousePosition);
            Frame(r, hot ? new Color(0.11f, 0.42f, 0.68f, 1f) : CBtn, CBorder);
            Text(r, label, hot ? Color.white : CText, TextAnchor.MiddleCenter);
            y += RowH + 6f;
            if (hot && e.type == EventType.MouseDown && e.button == 0) { e.Use(); return true; }
            return false;
        }

        private static void CopyTo(PlantOverride src, PlantOverride dst)
        {
            dst.GodMode = src.GodMode; dst.Undead = src.Undead; dst.Invincible = src.Invincible;
            dst.KeepShooting = src.KeepShooting; dst.AlwaysLightUp = src.AlwaysLightUp; dst.Uncrashable = src.Uncrashable;
            dst.MaxHealth = src.MaxHealth; dst.AttackDamage = src.AttackDamage; dst.Level = src.Level; dst.Stage = src.Stage;
            dst.AttackInterval = src.AttackInterval; dst.Defence = src.Defence;
            dst.DamageMult = src.DamageMult; dst.SpeedMult = src.SpeedMult;
            dst.SkinType = src.SkinType; dst.Scale = src.Scale;
            dst.BulletType = src.BulletType; dst.BulletDamageMult = src.BulletDamageMult;
            dst.BulletSpeedMult = src.BulletSpeedMult; dst.BulletPierce = src.BulletPierce;
            dst.Effects = src.Effects;
        }

        // ---------------------------------------------------------------- ESP 方框
        private void DrawEsp()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            // 用游戏自己的植物列表（Board.boardEntity.plantArray），覆盖所有关卡类型
            var arr = Actions.PlantsSnapshot();
            if (arr == null) return;

            for (int i = 0; i < arr.Count; i++)
            {
                Plant p = arr[i];
                if (p == null) continue;

                Vector3 wp;
                try { wp = p.transform.position; } catch { continue; }
                wp.y += EspHeight;

                Vector3 sp = cam.WorldToScreenPoint(wp);
                if (sp.z < 0f) continue;

                float x = sp.x, sy = Screen.height - sp.y;
                if (x < -300f || x > Screen.width + 300f || sy < -300f || sy > Screen.height + 300f) continue;

                bool sel = Actions.IsSelected(p);
                var rect = new Rect(x - EspWidth * 0.5f, sy - 10f, EspWidth, 20f);
                Fill(rect, sel ? new Color(0.62f, 0.12f, 0.12f, 0.92f) : new Color(0.04f, 0.05f, 0.07f, 0.88f));
                Fill(new Rect(rect.x, rect.y, rect.width, 2f), sel ? new Color(1f, 0.35f, 0.35f, 1f) : CAccent);

                string s = "";
                try
                {
                    if (ShowIndex) s = "#" + i + " ";
                    if (ShowName) s += p.thePlantType.ToString();
                    if (ShowHp) s += (s.Length > 0 ? "  " : "") + p.thePlantHealth + "/" + p.thePlantMaxHealth;
                }
                catch { }
                if (s.Length > 0) Text(rect, s, CText, TextAnchor.MiddleCenter);

                Event e = Event.current;
                if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    Actions.Select(p);
                    _editing = true;
                    _tab = 2;
                    ShowMenu = true;
                    e.Use();
                }
            }
        }
    }
}
