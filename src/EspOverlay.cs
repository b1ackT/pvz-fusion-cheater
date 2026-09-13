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
                // 框选要最先取事件（否则会被下面的方框点击消费掉）
                BoxSelect.Frame();
                if (ShowEsp) DrawEsp();
                if (ModConfig.ZombieEsp.Value) DrawZombieEsp();
                BoxSelect.Draw();
            }
            catch (Exception e) { Plugin.LogOnce("ESP", e); }
        }

        // ---------------------------------------------------------------- 屏幕矩形
        /// <summary>
        /// 一株植物 ESP 方框的屏幕矩形（框选/命中判定共用同一套算法，
        /// 保证"框到哪儿就选中哪儿"，不会跟画出来的框对不上）。
        /// </summary>
        internal static bool TryPlantRect(Plant p, out Rect r)
        {
            r = new Rect(0f, 0f, 0f, 0f);
            if (p == null) return false;
            Transform tr;
            try { tr = p.transform; } catch { return false; }
            return TryRect(tr, EspHeight, out r);
        }

        /// <summary>一只僵尸 ESP 方框的屏幕矩形</summary>
        internal static bool TryZombieRect(Zombie z, out Rect r)
        {
            r = new Rect(0f, 0f, 0f, 0f);
            if (z == null) return false;
            Transform tr;
            try { tr = z.transform; } catch { return false; }
            float h = 1.1f;
            try { h = ModConfig.ZombieEspHeight.Value; } catch { }
            return TryRect(tr, h, out r);
        }

        private static bool TryRect(Transform tr, float height, out Rect r)
        {
            r = new Rect(0f, 0f, 0f, 0f);
            Camera cam = Camera.main;
            if (cam == null || tr == null) return false;
            try
            {
                Vector3 wp = tr.position;
                wp.y += height;
                Vector3 sp = cam.WorldToScreenPoint(wp);
                if (sp.z < 0f) return false;
                float x = sp.x, sy = Screen.height - sp.y;
                if (x < -400f || x > Screen.width + 400f || sy < -400f || sy > Screen.height + 400f) return false;
                int fs = UiSkin.FontSize > 0 ? UiSkin.FontSize : 18;
                float h = fs + 12f;
                r = new Rect(Mathf.Round(x - EspWidth * 0.5f), Mathf.Round(sy - h * 0.5f),
                             Mathf.Round(EspWidth), Mathf.Round(h));
                return true;
            }
            catch { return false; }
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
            // 热键可在游戏内「设置」页自定义，默认 F3
            if (e.keyCode == ModConfig.EspKeyCode()) { ShowEsp = !ShowEsp; e.Use(); }
        }

        private void DrawEsp()
        {
            var arr = Actions.PlantsSnapshot();
            if (arr == null || arr.Count == 0) return;

            for (int i = 0; i < arr.Count; i++)
            {
                Plant p = arr[i];
                if (p == null) continue;

                Rect rect;
                if (!TryPlantRect(p, out rect)) continue;

                bool sel = Actions.IsSelected(p);
                bool marked = MenuUI.IsMarked(p);

                // 多选打勾(蓝) > 单选(红) > 普通(深灰)
                Fill(rect, marked ? new Color(0.08f, 0.16f, 0.34f, 0.95f)
                            : sel ? new Color(0.62f, 0.12f, 0.12f, 0.95f)
                                  : new Color(0.04f, 0.05f, 0.07f, 0.92f));
                Fill(new Rect(rect.x, rect.y, rect.width, 3f),
                     marked ? new Color(0.45f, 0.72f, 1f, 1f)
                          : sel ? new Color(1f, 0.35f, 0.35f, 1f) : new Color(0.20f, 0.85f, 0.45f, 1f));

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

        /// <summary>僵尸 ESP：和植物一样的方框，敌我颜色区分，点一下选中它</summary>
        private void DrawZombieEsp()
        {
            var arr = Actions.ZombiesSnapshot();
            if (arr == null || arr.Count == 0) return;

            for (int i = 0; i < arr.Count; i++)
            {
                Zombie z = arr[i];
                if (z == null) continue;

                Rect rect;
                if (!TryZombieRect(z, out rect)) continue;

                bool sel = Actions.IsZombieSelected(z);
                bool marked = MenuUI.IsZombieMarked(z);
                bool mind = false;
                try { mind = z.isMindControlled; } catch { }

                // 颜色：多选(蓝) > 选中(亮黄) > 被魅惑友军(青绿) > 普通敌人(红)
                Color fill = marked ? new Color(0.08f, 0.16f, 0.34f, 0.95f)
                           : sel ? new Color(0.55f, 0.45f, 0.05f, 0.95f)
                           : mind ? new Color(0.05f, 0.32f, 0.34f, 0.92f)
                                  : new Color(0.34f, 0.06f, 0.08f, 0.92f);
                Color top = marked ? new Color(0.45f, 0.72f, 1f, 1f)
                          : sel ? new Color(1f, 0.85f, 0.35f, 1f)
                          : mind ? new Color(0.35f, 0.95f, 0.90f, 1f)
                                 : new Color(1f, 0.45f, 0.45f, 1f);

                Fill(rect, fill);
                Fill(new Rect(rect.x, rect.y, rect.width, 3f), top);

                string s = "";
                try
                {
                    if (ShowIndex) s = "#" + i + " ";
                    if (ModConfig.ZombieEspName.Value)
                    {
                        string cn = ZombieDb.CnName((int)z.theZombieType);
                        if (string.IsNullOrEmpty(cn)) cn = z.theZombieType.ToString();
                        s += cn;
                    }
                    if (ModConfig.ZombieEspHp.Value)
                    {
                        s += (s.Length > 0 ? "  " : "");
                        long armor = 0;
                        try { armor = z.theFirstArmorHealth; } catch { }
                        s += z.theHealth + "/" + z.theMaxHealth;
                        if (armor > 0) s += " +" + armor;
                    }
                }
                catch { }

                if (s.Length > 0)
                    UiSkin.Text(rect, s, new UiSkin.TextOpt
                    {
                        Align = TextAnchor.MiddleCenter,
                        Style = ModConfig.EspBold.Value ? FontStyle.Bold : FontStyle.Normal,
                        Color = new Color(0.95f, 0.97f, 1f, 1f)
                    });

                if (MenuUI.BlocksGameInput()) continue;
                Event e = Event.current;
                if (e != null && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    Actions.SelectZombie(z);
                    UiSkin.Click(rect, 0);
                }
            }
        }

        private static void Fill(Rect r, Color c) { UiSkin.Fill(r, c); }
    }
}
