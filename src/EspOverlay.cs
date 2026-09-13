using System;
using UnityEngine;

namespace PvzRhCheat
{
    /// <summary>
    /// 游戏内**只保留 ESP 方框**（这个必须画在游戏里，因为要跟世界坐标对齐）。
    ///
    /// 所有菜单/面板/编辑器都已经搬到**独立窗口** PvzRhCheatUi.exe，
    /// 由插件启动时自动拉起，通过 127.0.0.1:27183 与本插件通信。
    ///
    /// 点方框 = 选中该植物，独立窗口会跟着切到它。
    /// </summary>
    public class EspOverlay : MonoBehaviour
    {
        public static bool ShowEsp   = true;
        public static bool ShowName  = true;
        public static bool ShowHp    = true;
        public static bool ShowIndex = false;
        public static float EspHeight = 0.75f;
        public static float EspWidth  = 120f;

        private static bool _diagLogged;
        private static bool _cjk = true;

        private void OnGUI()
        {
            try
            {
                Diag();
                UiSkin.SetFontSize(Mathf.Clamp(ModConfig.EspFontSize.Value, 8, 48));
                Keys();
                if (ShowEsp) DrawEsp();
            }
            catch (Exception e) { Plugin.LogOnce("ESP", e); }
        }

        /// <summary>默认皮肤在这套构建里没有字体（font == NULL），不补的话文字全是空白</summary>
        private static void Diag()
        {
            if (_diagLogged) return;
            _diagLogged = true;
            UiSkin.Ensure();          // 字体补丁 / box 白底贴图统一在 UiSkin 里做
            _cjk = UiSkin.Cjk;
        }

        private void Keys()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.F3) { ShowEsp = !ShowEsp; e.Use(); }
        }

        private void DrawEsp()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            var arr = Actions.PlantsSnapshot();
            if (arr == null || arr.Count == 0) return;

            int fs = UiSkin.FontSize > 0 ? UiSkin.FontSize : 18;
            float h = fs + 12f;

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
                if (x < -400f || x > Screen.width + 400f || sy < -400f || sy > Screen.height + 400f) continue;

                bool sel = Actions.IsSelected(p);
                var rect = new Rect(Mathf.Round(x - EspWidth * 0.5f), Mathf.Round(sy - h * 0.5f),
                                    Mathf.Round(EspWidth), Mathf.Round(h));
                Fill(rect, sel ? new Color(0.62f, 0.12f, 0.12f, 0.95f) : new Color(0.04f, 0.05f, 0.07f, 0.92f));
                Fill(new Rect(rect.x, rect.y, rect.width, 3f),
                     sel ? new Color(1f, 0.35f, 0.35f, 1f) : new Color(0.20f, 0.85f, 0.45f, 1f));

                string s = "";
                try
                {
                    if (ShowIndex) s = "#" + i + " ";
                    if (ShowName)
                    {
                        string cn = PlantDb.CnName(p);
                        if (string.IsNullOrEmpty(cn)) cn = p.thePlantType.ToString();
                        s += cn;
                    }
                    if (ShowHp) s += (s.Length > 0 ? "  " : "") + p.thePlantHealth + "/" + p.thePlantMaxHealth;
                }
                catch { }

                if (s.Length > 0)
                    UiSkin.Text(rect, s, new UiSkin.TextOpt
                    {
                        Align = TextAnchor.MiddleCenter,
                        Style = ModConfig.EspBold.Value ? FontStyle.Bold : FontStyle.Normal,
                        Color = new Color(0.93f, 0.95f, 0.98f, 1f)
                    });

                // 点方框 = 选中该植物（数值修改在 Insert 菜单的「植物」页里做）
                if (MenuUI.BlocksGameInput()) continue;
                Event e = Event.current;
                if (e != null && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    Actions.Select(p);
                    UiSkin.Click(rect, 0);
                }
            }
        }

        private static void Fill(Rect r, Color c) { UiSkin.Fill(r, c); }
    }
}
