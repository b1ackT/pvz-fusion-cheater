using System;
using System.Globalization;
using UnityEngine;

namespace PvzRhCheat
{
    /// <summary>
    /// 诸神（射击 / 诸神进化 / 无尽 / 炼狱 / 十旗·诸神）里的 **幸运** 修改。
    ///
    /// 游戏侧的位置（IL2CPP dump 实证）：
    ///   `GameLevel.RogueShooting.ShootingManager`
    ///     · `public static ShootingManager Instance;`   ← 只有诸神类关卡才存在
    ///     · `public float maxLucky;`        // 0xA0  幸运上限
    ///     · `private float _lucky;`         // 0xA4  幸运当前值
    ///     · `public float Lucky { get; set; }`         ← setter 可能会按上限钳制
    ///     · `public string LuckyString { get; }`       ← 游戏自己那行"当前幸运值…"说明
    ///     · `pityThreshold / pityEnabled / noDiamondCount / totalPityTriggered`（保底，只读展示）
    ///
    /// 幸运的作用（游戏内文本实证）："幸运可以提高好词条出现概率"、
    /// "当前幸运值：{0}\n幸运可以增加从箱子里获取植物的数量和从抽奖中获得的阳光数量"、
    /// 词条"以50的幸运开局，并使幸运上限增加至300"、"命运无常：随机获得 -27~23 幸运"。
    ///
    /// 两条原则：
    ///   1. **默认只"应用一次"**（按按钮 / 输入 / IPC 请求），不常驻；
    ///      想常驻要自己开 `LuckyLock`（游戏会消耗/改动幸运时才需要）。
    ///   2. IPC 线程**不碰原生对象**：请求只登记，主循环（`Tick`）里才真正写游戏字段，
    ///      并且顺手把只读信息缓存成 JSON，`STATE` 直接读缓存。
    /// </summary>
    internal static class LuckDb
    {
        internal static GameLevel.RogueShooting.ShootingManager M()
        {
            try { return GameLevel.RogueShooting.ShootingManager.Instance; }
            catch { return null; }
        }

        /// <summary>当前是不是诸神类模式（能不能读到 ShootingManager）</summary>
        internal static bool Available { get { return M() != null; } }

        internal static float Lucky()
        {
            var m = M(); if (m == null) return -1f;
            try { return m.Lucky; } catch { return -1f; }
        }

        internal static float MaxLucky()
        {
            var m = M(); if (m == null) return -1f;
            try { return m.maxLucky; } catch { return -1f; }
        }

        /// <summary>游戏自己那行幸运说明（"当前幸运值：50/300 …"），读不到就返回空</summary>
        internal static string LuckyString()
        {
            var m = M(); if (m == null) return "";
            try { return m.LuckyString ?? ""; } catch { return ""; }
        }

        /// <summary>模式描述：诸神·射击 / 诸神进化·无尽 / 炼狱 …</summary>
        internal static string ModeText()
        {
            var m = M();
            if (m == null) return "（当前不在诸神类模式）";
            try
            {
                var sb = new System.Text.StringBuilder(120);
                sb.Append("诸神");
                if (m.hellMode) sb.Append("·炼狱");
                if (m.endless) sb.Append("·无尽");
                else if (!m.hellMode) sb.Append("·射击");
                sb.Append("  第 ").Append(m.stage).Append('/').Append(m.maxStage).Append(" 轮");
                sb.Append("  难度 ").Append(m.difficulty);
                sb.Append("  词条分 ").Append(m.debuffPoint);
                return sb.ToString();
            }
            catch { return "诸神（部分字段读不到）"; }
        }

        // ------------------------------------------------------------ 原值记录 / 还原
        private static float _origLucky = -1f, _origMax = -1f;
        private static bool _hasOrig;

        internal static string Remember()
        {
            var m = M();
            if (m == null) return "当前不在诸神模式，没什么可记的";
            _origLucky = Lucky();
            _origMax = MaxLucky();
            _hasOrig = true;
            string r = "已记下原始幸运：" + N(_origLucky) + " / 上限 " + N(_origMax);
            Plugin.Log.LogInfo("[幸运] " + r);
            return r;
        }

        internal static string Restore()
        {
            if (!_hasOrig) return "还没有记录原值（先点一次「记下原值」）";
            var m = M();
            if (m == null) return "当前不在诸神模式";
            try
            {
                m.maxLucky = _origMax;
                m.Lucky = _origLucky;
                string r = "已还原幸运 = " + N(Lucky()) + " / 上限 " + N(MaxLucky());
                Plugin.Log.LogInfo("[幸运] " + r);
                return r;
            }
            catch (Exception e) { return "还原失败：" + e.GetType().Name; }
        }

        // ------------------------------------------------------------ 真正写入（主线程）
        /// <summary>设置幸运与上限（主线程调用）。lucky &lt; 0 表示不动幸运，max &lt; 0 表示不动上限</summary>
        internal static string ApplyNow(float lucky, float max)
        {
            var m = M();
            if (m == null) return "当前不在诸神模式（读不到 ShootingManager.Instance）";
            if (!_hasOrig) { try { _origLucky = m.Lucky; _origMax = m.maxLucky; _hasOrig = true; } catch { } }

            var sb = new System.Text.StringBuilder(120);
            try
            {
                if (max >= 0f)
                {
                    m.maxLucky = max;
                    // 上限压到比当前幸运还低时，游戏的真实行为也是把幸运拉下来
                    try { if (lucky < 0f && m.Lucky > max) m.Lucky = max; } catch { }
                    sb.Append("上限 = ").Append(N(m.maxLucky));
                }
                if (lucky >= 0f)
                {
                    // 先保证上限够大，否则 setter 很可能把值钳回去
                    if (m.maxLucky < lucky) m.maxLucky = lucky;
                    m.Lucky = lucky;
                    float now = m.Lucky;
                    if (Math.Abs(now - lucky) > 0.01f)
                    {
                        // 属性被钳制/被忽略 → 直接写私有字段兜底
                        try { m._lucky = lucky; now = m.Lucky; } catch { }
                    }
                    if (sb.Length > 0) sb.Append("，");
                    sb.Append("幸运 = ").Append(N(now));
                    if (Math.Abs(now - lucky) > 0.01f)
                        sb.Append("（请求 ").Append(N(lucky)).Append("，被游戏钳制了）");
                }
                if (sb.Length == 0) sb.Append("没有要改的值");
                sb.Append("   上限现在 ").Append(N(m.maxLucky));
            }
            catch (Exception e)
            {
                return "写幸运失败：" + e.GetType().Name + " " + e.Message;
            }
            _pendLucky = _pendMax = -1f;
            return sb.ToString();
        }

        // ------------------------------------------------------------ 请求（任意线程）
        private static float _pendLucky = -1f, _pendMax = -1f;

        /// <summary>登记一次修改请求，等主循环消费（IPC 线程用）</summary>
        internal static void Request(float lucky, float max)
        {
            _pendLucky = lucky;
            _pendMax = max;
        }

        /// <summary>主循环：消费请求 + 常驻锁定 + 刷新只读缓存</summary>
        internal static void Tick()
        {
            try
            {
                if (_pendLucky >= 0f || _pendMax >= 0f)
                {
                    float l = _pendLucky, mx = _pendMax;
                    _pendLucky = _pendMax = -1f;
                    string r = ApplyNow(l, mx);
                    Plugin.Log.LogInfo("[幸运] " + r);
                }

                if (ModConfig.LuckyLock.Value)
                {
                    var m = M();
                    if (m != null)
                    {
                        float wantMax = ModConfig.LuckyMaxValue.Value;
                        float want = ModConfig.LuckyValue.Value;
                        try { if (wantMax > 0f && Math.Abs(m.maxLucky - wantMax) > 0.01f) m.maxLucky = wantMax; } catch { }
                        try
                        {
                            if (want > 0f)
                            {
                                if (m.maxLucky < want) m.maxLucky = want;
                                if (Math.Abs(m.Lucky - want) > 0.01f) m.Lucky = want;
                            }
                        }
                        catch { }
                    }
                }
                _json = BuildJson();
            }
            catch (Exception e) { Plugin.LogOnce("幸运/主循环", e); }
        }

        // ------------------------------------------------------------ 只读缓存（给 STATE）
        private static string _json = "";

        internal static string Json { get { return string.IsNullOrEmpty(_json) ? "null" : _json; } }

        private static string BuildJson()
        {
            var m = M();
            var sb = new System.Text.StringBuilder(320);
            sb.Append('{');
            sb.Append("\"on\":").Append(m == null ? 0 : 1);
            if (m == null) { sb.Append('}'); return sb.ToString(); }
            try
            {
                sb.Append(",\"lucky\":").Append(N(m.Lucky));
                sb.Append(",\"max\":").Append(N(m.maxLucky));
                sb.Append(",\"stage\":").Append(m.stage).Append(",\"maxStage\":").Append(m.maxStage);
                sb.Append(",\"difficulty\":").Append(m.difficulty);
                sb.Append(",\"endless\":").Append(m.endless ? 1 : 0).Append(",\"hell\":").Append(m.hellMode ? 1 : 0);
                sb.Append(",\"debuff\":").Append(m.debuffPoint);
                sb.Append(",\"pity\":").Append(m.pityThreshold).Append(",\"pityOn\":").Append(m.pityEnabled ? 1 : 0);
                sb.Append(",\"pityHit\":").Append(m.totalPityTriggered).Append(",\"noDiamond\":").Append(m.noDiamondCount);
                sb.Append(",\"str\":\"").Append(Esc(m.LuckyString)).Append('"');
            }
            catch (Exception e) { sb.Append(",\"err\":\"").Append(e.GetType().Name).Append('"'); }
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>诊断一行（写日志 + 状态栏用）</summary>
        internal static string Info()
        {
            var m = M();
            if (m == null)
            {
                string r0 = "当前不在诸神类模式（ShootingManager.Instance 为空）。进「诸神」系列关卡后这里才有值。";
                Plugin.Log.LogInfo("[幸运] " + r0);
                return r0;
            }
            var sb = new System.Text.StringBuilder(220);
            sb.Append(ModeText());
            sb.Append("  |  幸运 ").Append(N(Lucky())).Append(" / 上限 ").Append(N(MaxLucky()));
            try { sb.Append("  保底阈值 ").Append(m.pityThreshold).Append(m.pityEnabled ? "（启用）" : "（关）"); } catch { }
            try { sb.Append("  已触发 ").Append(m.totalPityTriggered).Append("  无钻计数 ").Append(m.noDiamondCount); } catch { }
            string s = LuckyString();
            if (!string.IsNullOrEmpty(s)) sb.Append("  |  ").Append(s.Replace("\n", " "));
            string r = sb.ToString();
            Plugin.Log.LogInfo("[幸运] " + r);
            return r;
        }

        private static string N(float f) { return f.ToString("0.###", CultureInfo.InvariantCulture); }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
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
    }
}
