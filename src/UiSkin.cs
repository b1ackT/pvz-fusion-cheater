using System;
using UnityEngine;

namespace PvzRhCheat
{
    /// <summary>
    /// 仅使用**这套构建里确认可用**的 IMGUI API 来画界面。
    ///
    /// 已确认被裁剪（调用即抛 NotSupportedException）的接口：
    ///   GUI.DrawTexture / GUI.TextField / GUI.Toggle / GUI.Button(保守起见也自己画)
    /// 所以本文件里只用：GUI.Box（配合 GUI.color 当纯色填充）+ GUI.Label。
    /// 方框、按钮、勾选框、滚动全部手工实现，绝不依赖被裁掉的接口。
    /// </summary>
    internal static class UiSkin
    {
        public static bool Ready { get; private set; }
        /// <summary>皮肤字体是否含中文</summary>
        public static bool Cjk { get; private set; }
        /// <summary>字体里到底有没有 ✓ 字形（只做记录，界面已经不用它画勾了）</summary>
        public static bool HasTickGlyph { get; private set; }

        private static bool _tried;

        // ---- 配色（经典外挂菜单风格：深灰底 + 亮绿高亮）----
        public static readonly Color ColBack     = new Color(0.09f, 0.10f, 0.12f, 1f);
        public static readonly Color ColBack2    = new Color(0.13f, 0.14f, 0.17f, 1f);
        public static readonly Color ColTitle    = new Color(0.16f, 0.18f, 0.22f, 1f);
        public static readonly Color ColLine     = new Color(0.26f, 0.29f, 0.34f, 1f);
        public static readonly Color ColTabOn    = new Color(0.20f, 0.45f, 0.28f, 1f);
        public static readonly Color ColTabOff   = new Color(0.15f, 0.16f, 0.19f, 1f);
        public static readonly Color ColRowOn    = new Color(0.16f, 0.32f, 0.22f, 1f);
        public static readonly Color ColSel      = new Color(0.20f, 0.38f, 0.55f, 1f);
        public static readonly Color ColEdit     = new Color(0.35f, 0.28f, 0.10f, 1f);
        public static readonly Color ColText     = new Color(0.90f, 0.92f, 0.95f, 1f);
        public static readonly Color ColDim      = new Color(0.62f, 0.66f, 0.72f, 1f);
        public static readonly Color ColGreen    = new Color(0.45f, 0.95f, 0.55f, 1f);
        public static readonly Color ColRed      = new Color(1.00f, 0.45f, 0.45f, 1f);
        public static readonly Color ColYellow   = new Color(1.00f, 0.85f, 0.35f, 1f);

        /// <summary>勾选符号（U+2713，LegacyRuntime 字体里已确认存在）</summary>
        public const string Tick = "\u2713";

        /// <summary>
        /// 补齐默认皮肤缺失的字体，并把 box 的背景设成白色贴图（这样 GUI.color 才能当纯色填充用）。
        /// 幂等，重复调用无副作用。
        /// </summary>
        public static void Ensure()
        {
            if (_tried) return;
            _tried = true;

            string name = "NULL";
            try { if (GUI.skin.label.font != null) name = GUI.skin.label.font.name; } catch { }

            if (name == "NULL")
            {
                try
                {
                    var all = Resources.FindObjectsOfTypeAll<Font>();
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null) continue;
                        GUI.skin.label.font = all[i];
                        GUI.skin.box.font = all[i];
                        name = all[i].name;
                        Plugin.Log.LogInfo("[界面] 皮肤缺少字体，已补: " + name);
                        break;
                    }
                }
                catch (Exception e) { Plugin.LogOnce("界面/字体", e); }
            }

            try
            {
                Font f = GUI.skin.label.font;
                if (f != null)
                {
                    Cjk = f.HasCharacter('中');
                    HasTickGlyph = f.HasCharacter('\u2713');
                }
            }
            catch { }
            Plugin.Log.LogInfo("[界面] 字体=" + name + " 含中文=" + Cjk + " 含✓字形=" + HasTickGlyph
                             + "（勾选框已改为自绘，不依赖字体）");

            try { GUI.skin.box.normal.background = Texture2D.whiteTexture; } catch { }

            // 默认 label 会把超出 rect 的文字裁掉（状态栏右半边就是这么被裁掉开头的），
            // 这里统一改成溢出显示；内边距清零，数值才对得准自己的框。
            try { GUI.skin.label.clipping = TextClipping.Overflow; } catch { }
            try { GUI.skin.label.richText = false; } catch { }
            try { ZeroOffsets(GUI.skin.label.padding); } catch { }
            try { ZeroOffsets(GUI.skin.label.margin); } catch { }
            try { ZeroOffsets(GUI.skin.box.padding); } catch { }
            try { ZeroOffsets(GUI.skin.box.margin); } catch { }

            Ready = name != "NULL";
            if (!Ready) Plugin.Log.LogWarning("[界面] 没能给皮肤补上字体，界面文字会是空白");
            else if (!Cjk) Plugin.Log.LogWarning("[界面] 当前字体不含中文，中文会显示成方块（字体=" + name + "）");
        }

        /// <summary>Il2Cpp 的 RectOffset 没有 4 参数构造，得逐个赋值</summary>
        private static void ZeroOffsets(RectOffset o)
        {
            if (o == null) return;
            o.left = 0; o.right = 0; o.top = 0; o.bottom = 0;
        }

        /// <summary>纯色填充（用 Box + GUI.color，绕开被裁掉的 DrawTexture）</summary>
        public static void Fill(Rect r, Color c)
        {
            Color o = GUI.color;
            GUI.color = c;
            try { GUI.Box(r, ""); }
            catch (Exception e) { Plugin.LogOnce("界面/填充", e); }
            finally { GUI.color = o; }
        }

        /// <summary>1px 边框</summary>
        public static void Border(Rect r, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, 1f), c);
            Fill(new Rect(r.x, r.y + r.height - 1f, r.width, 1f), c);
            Fill(new Rect(r.x, r.y, 1f, r.height), c);
            Fill(new Rect(r.x + r.width - 1f, r.y, 1f, r.height), c);
        }

        public struct TextOpt
        {
            public TextAnchor Align;
            public FontStyle Style;
            public Color Color;
            public bool Dim;
        }

        public static TextOpt Left  { get { return new TextOpt { Align = TextAnchor.MiddleLeft,   Style = FontStyle.Normal, Color = ColText }; } }
        public static TextOpt Center{ get { return new TextOpt { Align = TextAnchor.MiddleCenter, Style = FontStyle.Normal, Color = ColText }; } }
        public static TextOpt Right { get { return new TextOpt { Align = TextAnchor.MiddleRight,  Style = FontStyle.Normal, Color = ColText }; } }
        public static TextOpt Bold  { get { return new TextOpt { Align = TextAnchor.MiddleLeft,   Style = FontStyle.Bold,   Color = ColText }; } }

        private static int _fontSize = -1;

        public static void SetFontSize(int px)
        {
            if (_fontSize == px) return;
            _fontSize = px;
            try { GUI.skin.label.fontSize = px; GUI.skin.box.fontSize = px; } catch { }
        }

        public static int FontSize { get { return _fontSize; } }

        public static void Text(Rect r, string s, TextOpt o)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextAnchor oa = GUI.skin.label.alignment;
            FontStyle of = GUI.skin.label.fontStyle;
            Color oc = GUI.contentColor;
            bool ow = GUI.skin.label.wordWrap;
            try
            {
                GUI.skin.label.alignment = o.Align;
                GUI.skin.label.fontStyle = o.Style;
                GUI.skin.label.wordWrap = false;
                GUI.contentColor = o.Dim ? ColDim : o.Color;
                GUI.Label(r, s);
            }
            catch (Exception e) { Plugin.LogOnce("界面/文字", e); }
            finally
            {
                GUI.skin.label.alignment = oa;
                GUI.skin.label.fontStyle = of;
                GUI.skin.label.wordWrap = ow;
                GUI.contentColor = oc;
            }
        }

        public static void Text(Rect r, string s) { Text(r, s, Left); }

        /// <summary>
        /// 画勾。**故意不用字体里的 ✓ 字形**：
        /// 这套构建的字体在某些字号下画不出 U+2713，Unity 会画"缺字方框"，
        /// 表现就是一个亮绿色的大方块（用户截图里那个"超大绿色像素"就是它）。
        /// 改成用整数坐标的小方块拼一个勾，跟字体完全无关，任何字号都清晰。
        /// </summary>
        public static void DrawCheck(Rect box, Color c)
        {
            float s = Mathf.Min(box.width, box.height);
            // 小方块取 s/6：14px 的框里就是 2px 一格，勾形最干净
            // （取 s/4.5 会变成 3px，关节处糊成一坨，实测对比过）
            float cw = Mathf.Max(2f, Mathf.Round(s / 6f));
            Stroke(box, cw, 0.14f, 0.50f, 0.40f, 0.78f, c);      // 短臂
            Stroke(box, cw, 0.36f, 0.78f, 0.86f, 0.24f, c);      // 长臂
        }

        private static void Stroke(Rect box, float cw, float x0, float y0, float x1, float y1, Color c)
        {
            float w = box.width, h = box.height;
            float dx = (x1 - x0) * w, dy = (y1 - y0) * h;
            int n = Mathf.Max(2, Mathf.RoundToInt(Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) / cw));
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                float x = box.x + (x0 + (x1 - x0) * t) * w - cw * 0.5f;
                float y = box.y + (y0 + (y1 - y0) * t) * h - cw * 0.5f;
                Fill(new Rect(Mathf.Round(x), Mathf.Round(y), cw, cw), c);
            }
        }

        /// <summary>画一个清晰的勾选框</summary>
        public static void Checkbox(Rect box, bool on)
        {
            Fill(box, on ? new Color(0.14f, 0.42f, 0.22f, 1f) : new Color(0.17f, 0.18f, 0.22f, 1f));
            Border(box, on ? ColGreen : ColLine);
            if (!on) return;
            Rect inner = new Rect(box.x + 2f, box.y + 2f, box.width - 4f, box.height - 4f);
            DrawCheck(inner, new Color(0.92f, 1f, 0.92f, 1f));
        }

        /// <summary>绘制一个"勾选框 + 标题 + 描述"的整行开关，返回是否被点击</summary>
        public static bool CheckRow(Rect row, bool value, string title, string desc, bool hover)
        {
            Fill(row, hover ? ColRowOn : ColBack2);
            Rect box = new Rect(row.x + 6f, row.y + (row.height - 18f) * 0.5f, 18f, 18f);
            Checkbox(box, value);

            Text(new Rect(box.xMax + 8f, row.y, row.width - 210f, row.height), title,
                 value ? Bold : Left);
            if (!string.IsNullOrEmpty(desc))
                Text(new Rect(row.xMax - 196f, row.y, 190f, row.height), desc, new TextOpt
                { Align = TextAnchor.MiddleRight, Style = FontStyle.Normal, Color = ColDim, Dim = true });
            return Click(row, 0);
        }

        /// <summary>
        /// 标题栏上的小按钮：底色必须和标题栏拉开对比，
        /// 否则按钮框和标题栏同色，看起来就像"没有按钮"（实测踩过）。
        /// </summary>
        public static bool SmallButton(Rect r, string label, bool on, bool enabled)
        {
            bool hover = enabled && MouseOver(r);
            Fill(r, on ? new Color(0.20f, 0.48f, 0.30f, 1f)
                       : hover ? new Color(0.32f, 0.36f, 0.43f, 1f)
                               : new Color(0.24f, 0.27f, 0.32f, 1f));
            Border(r, on ? ColGreen : new Color(0.40f, 0.44f, 0.51f, 1f));
            Text(r, label, new TextOpt
            {
                Align = TextAnchor.MiddleCenter,
                Style = on ? FontStyle.Bold : FontStyle.Normal,
                Color = enabled ? ColText : ColDim
            });
            return enabled && Click(r, 0);
        }

        /// <summary>画一个符号（× 之类），用比框稍大的字号，免得糊成一小坨</summary>
        public static void Glyph(Rect r, string glyph, Color c)
        {
            int old = _fontSize;
            SetFontSize(Mathf.RoundToInt(r.height) + 2);
            try { Text(r, glyph, new TextOpt { Align = TextAnchor.MiddleCenter, Style = FontStyle.Bold, Color = c }); }
            finally { SetFontSize(old <= 0 ? 14 : old); }
        }

        /// <summary>普通按钮，返回是否被点击</summary>
        public static bool Button(Rect r, string label, bool on, bool enabled)
        {
            bool hover = enabled && MouseOver(r);
            Color baseC = !enabled ? new Color(0.14f, 0.15f, 0.17f, 1f)
                        : on ? new Color(0.20f, 0.46f, 0.30f, 1f)
                             : hover ? new Color(0.24f, 0.27f, 0.33f, 1f)
                                     : new Color(0.17f, 0.19f, 0.23f, 1f);
            Fill(r, baseC);
            Border(r, on ? ColGreen : ColLine);
            Text(r, label, new TextOpt
            {
                Align = TextAnchor.MiddleCenter,
                Style = on ? FontStyle.Bold : FontStyle.Normal,
                Color = enabled ? ColText : new Color(0.52f, 0.55f, 0.58f, 1f)
            });
            return enabled && Click(r, 0);
        }

        /// <summary>左对齐按钮</summary>
        public static bool LeftButton(Rect r, string label, bool on, Color tint)
        {
            bool hover = r.Contains(Event.current != null ? Event.current.mousePosition : new Vector2(-9999f, -9999f));
            Fill(r, hover ? new Color(0.24f, 0.27f, 0.33f, 1f) : new Color(0.17f, 0.19f, 0.23f, 1f));
            if (on) Fill(new Rect(r.x, r.y, 3f, r.height), ColGreen);
            Border(r, ColLine);
            Text(new Rect(r.x + 9f, r.y, r.width - 12f, r.height), label,
                 new TextOpt { Align = TextAnchor.MiddleLeft, Style = on ? FontStyle.Bold : FontStyle.Normal, Color = tint });
            return Click(r, 0);
        }

        /// <summary>鼠标左键是否按在这个矩形里（并吃掉事件）</summary>
        public static bool Click(Rect r, int button)
        {
            Event e = Event.current;
            if (e == null) return false;
            if (e.type != EventType.MouseDown || e.button != button) return false;
            if (!r.Contains(e.mousePosition)) return false;
            e.Use();
            return true;
        }

        public static bool MouseOver(Rect r)
        {
            Event e = Event.current;
            return e != null && r.Contains(e.mousePosition);
        }

        public static int Wheel()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.ScrollWheel) return 0;
            float d = e.delta.y;
            if (Math.Abs(d) < 0.01f) return 0;
            return d > 0f ? 1 : -1;   // 向下滚 = 内容往下
        }

        public static string Fit(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= maxChars) return s;
            return s.Substring(0, Math.Max(1, maxChars - 1)) + "…";
        }
    }
}
