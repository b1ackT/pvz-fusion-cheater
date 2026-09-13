using System;
using UnityEngine;

namespace PvzRhCheat
{
    /// <summary>
    /// 左键按住**拖拽框选**：在战场上拉一个方框，松手后把框到的所有植物/僵尸
    /// 一次性加入各自的"多选"集合（植物 页签 / 僵尸 页签里的勾选）。
    ///
    /// 设计要点（都是踩过坑之后定下来的）：
    ///  · 必须拖过 <see cref="MinDrag"/> 像素才算框选 —— 否则游戏里普普通通的
    ///    点击（种植物、收阳光）会被误判成框选。
    ///  · 只有真的进入框选状态才屏蔽游戏的鼠标（<see cref="Active"/> 被
    ///    Patch_Mouse_Update 读到），普通点击完全不干扰游戏。
    ///  · 只"加"不"减"：手抖框到一片不会把已有的勾选清空，想清空有"清空多选"按钮。
    ///  · 事件在 OnGUI 里取（Event.current），和 ESP 方框点击同一套机制。
    /// </summary>
    internal static class BoxSelect
    {
        /// <summary>拖拽超过这么多像素才算"框选"（防误触）</summary>
        internal const float MinDrag = 8f;

        /// <summary>正在框选（用来屏蔽游戏输入）</summary>
        internal static bool Active { get; private set; }

        /// <summary>当前框（屏幕坐标，y 向下，和 IMGUI 一致）</summary>
        internal static Rect Current { get; private set; }

        private static bool _down;
        private static Vector2 _a, _b;
        private static bool _loggedOnce;

        internal static bool Enabled
        {
            get { try { return ModConfig.BoxSelect.Value; } catch { return true; } }
        }

        internal static void Reset()
        {
            _down = false;
            Active = false;
        }

        /// <summary>每帧取一次鼠标事件（必须画 ESP 的那个 OnGUI 里调，事件才对得上）</summary>
        internal static void Frame()
        {
            Event e = Event.current;
            if (e == null) return;
            try
            {
                if (!Enabled) { Reset(); return; }

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    // 点在菜单面板上 = 操作菜单，不开始框选
                    if (MenuUI.BlocksGameInput()) { Reset(); return; }
                    _down = true;
                    _a = _b = e.mousePosition;
                    Active = false;
                    return;
                }

                if (_down && e.type == EventType.MouseDrag)
                {
                    _b = e.mousePosition;
                    if (!Active &&
                        (Mathf.Abs(_b.x - _a.x) > MinDrag || Mathf.Abs(_b.y - _a.y) > MinDrag))
                    {
                        Active = true;
                        if (!_loggedOnce)
                        {
                            _loggedOnce = true;
                            Plugin.Log.LogInfo("[框选] 已开始框选（左键拖拽即多选植物/僵尸）");
                        }
                    }
                    if (Active) { Current = Norm(_a, _b); e.Use(); }
                    return;
                }

                if (e.type == EventType.MouseUp && e.button == 0)
                {
                    if (Active)
                    {
                        Current = Norm(_a, _b);
                        string s = MenuUI.MarqueeSelect(Current);
                        MenuUI.SetStatus(s);
                        Plugin.Log.LogInfo("[框选] " + s);
                        e.Use();
                    }
                    Reset();
                    return;
                }

                // 鼠标在窗口外松开等极端情况：别把状态卡死
                if (_down && e.type == EventType.MouseLeaveWindow) Reset();
            }
            catch (Exception ex) { Plugin.LogOnce("框选/事件", ex); }
        }

        private static Rect Norm(Vector2 a, Vector2 b)
        {
            float x = Mathf.Min(a.x, b.x), y = Mathf.Min(a.y, b.y);
            return new Rect(Mathf.Round(x), Mathf.Round(y),
                            Mathf.Round(Mathf.Abs(a.x - b.x)), Mathf.Round(Mathf.Abs(a.y - b.y)));
        }

        /// <summary>画框（在所有 ESP 之后画，保证在最上层）</summary>
        internal static void Draw()
        {
            if (!Active) return;
            Rect r = Current;
            if (r.width < 2f || r.height < 2f) return;
            try
            {
                Color line = new Color(0.45f, 0.95f, 0.55f, 0.95f);
                UiSkin.Fill(r, new Color(0.35f, 0.85f, 0.55f, 0.16f));
                UiSkin.Fill(new Rect(r.x, r.y, r.width, 1f), line);
                UiSkin.Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), line);
                UiSkin.Fill(new Rect(r.x, r.y, 1f, r.height), line);
                UiSkin.Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), line);

                UiSkin.Text(new Rect(r.x + 2f, r.y - 18f, 340f, 17f),
                    "框选中 " + Mathf.RoundToInt(r.width) + "×" + Mathf.RoundToInt(r.height)
                    + " —— 松开左键把框到的植物/僵尸加入多选",
                    new UiSkin.TextOpt
                    {
                        Align = TextAnchor.MiddleLeft,
                        Style = FontStyle.Bold,
                        Color = new Color(0.70f, 1f, 0.78f, 1f)
                    });
            }
            catch (Exception e) { Plugin.LogOnce("框选/绘制", e); }
        }
    }
}
