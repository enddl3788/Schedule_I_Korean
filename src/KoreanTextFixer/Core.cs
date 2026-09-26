using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime;
using UnityEngine;
#if MELON
using Il2CppTMPro;
#else
using TMPro;
#endif

namespace KoreanTextFixer
{
    internal static class ModLog
    {
        public static Action<string> Info = _ => { };
        public static Action<string> Warn = _ => { };
        public static Action<string> Error = _ => { };
    }

    internal static class Translator
    {
        public const string Version = "1.4.0";

        internal static readonly Dictionary<string, string> Dict = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static readonly Dictionary<string, string> StrippedDict = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static readonly Regex TagRx = new Regex("<[^<>]{1,60}>", RegexOptions.Compiled);
        internal static readonly Dictionary<string, string> Regions = new Dictionary<string, string>
        {
            { "Northtown", "노스타운" }, { "Westville", "웨스트빌" }, { "Downtown", "다운타운" },
            { "Docks", "부두" }, { "Suburbia", "교외" }, { "Uptown", "업타운" }
        };

        private static readonly Regex Token = new Regex("<[^<>]{1,60}>|[^<]+", RegexOptions.Compiled);
        private static readonly Regex Paren = new Regex("^(\\s*)\\((.+)\\)(\\s*)$", RegexOptions.Compiled);
        private static readonly Regex Core = new Regex("^(\\s*)(.*?)(\\s*)$", RegexOptions.Compiled | RegexOptions.Singleline);

        public static void LoadDictionaries(string baseDir)
        {
            string[] files = { "Korean_Base.txt", "Korean_Extracted.txt", "Korean_Composites.txt" };
            foreach (string f in files)
            {
                string p = Path.Combine(baseDir, f);
                if (!File.Exists(p)) continue;
                foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                {
                    if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("sr:") || line.StartsWith("r:")) continue;
                    int eq = -1;
                    for (int i = 0; i < line.Length; i++)
                    {
                        if (line[i] == '=' && (i == 0 || line[i - 1] != '\\')) { eq = i; break; }
                    }
                    if (eq < 1) continue;
                    string k = Unescape(line.Substring(0, eq));
                    string v = Unescape(line.Substring(eq + 1));
                    if (v.Length == 0) continue;
                    if (!Dict.ContainsKey(k)) Dict[k] = v;
                    string kt = k.Trim();
                    if (kt.Length > 0 && !Dict.ContainsKey(kt)) Dict[kt] = v.Trim();
                    if (k.IndexOf('<') >= 0)
                    {
                        string ks = TagRx.Replace(k, "").Trim();
                        if (ks.Length > 10) StrippedDict[ks] = v;
                    }
                }
            }
        }

        private static string Unescape(string s)
        {
            return s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\=", "=").Replace("\\\\", "\\");
        }

        public static string Translate(string src)
        {
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c >= 0xAC00 && c <= 0xD7A3) return null;
            }
            string t = src.Trim();
            if (t.Length == 0) return null;

            // 1) 통짜 매칭
            string v;
            if (Dict.TryGetValue(t, out v)) return src.Replace(t, v);

            // 1-b) 태그 무시 매칭 (게임이 <h1>을 <color>로 바꿔 표시하는 경우)
            if (src.IndexOf('<') >= 0)
            {
                string stripped = TagRx.Replace(src, "").Trim();
                string sv;
                if (stripped.Length > 10 && StrippedDict.TryGetValue(stripped, out sv))
                {
                    var cm = Regex.Match(src, "<color[^<>]*>");
                    if (cm.Success) sv = sv.Replace("<h1>", cm.Value).Replace("</h>", "</color>");
                    else sv = TagRx.Replace(sv, "");
                    return sv;
                }
            }

            // 2) 태그 보존 조각 번역
            if (src.IndexOf('<') >= 0)
            {
                var sb = new StringBuilder(src.Length + 16);
                bool changed = false;
                foreach (Match m in Token.Matches(src))
                {
                    string tok = m.Value;
                    if (tok.Length > 0 && tok[0] == '<') { sb.Append(tok); continue; }
                    string rep = TranslatePlain(tok);
                    if (rep != null) { sb.Append(rep); changed = true; }
                    else sb.Append(tok);
                }
                if (changed) return sb.ToString();
                return null;
            }

            // 3) 평문 조각
            return TranslatePlain(src);
        }

        private static string TranslatePlain(string tok)
        {
            var cm = Core.Match(tok);
            string lead = cm.Groups[1].Value, core = cm.Groups[2].Value, tail = cm.Groups[3].Value;
            if (core.Length == 0) return null;

            string v;
            if (Dict.TryGetValue(core, out v)) return lead + v + tail;

            var pm = Paren.Match(tok);
            if (pm.Success)
            {
                string inner = pm.Groups[2].Value;
                string iv;
                if (Regions.TryGetValue(inner, out iv) || Dict.TryGetValue(inner, out iv))
                    return pm.Groups[1].Value + "(" + iv + ")" + pm.Groups[3].Value;
            }
            return null;
        }
    }

    // 번역기(XUAT)가 놓치는 텍스트(SetCharArray 경로 등)를 주기적으로 훑어 교체한다.
    internal sealed class TextScanner
    {
        private float _nextRefresh;
        private float _nextStat;
        private int _errors;
        private bool _dead;
        private int _replaced;
        private int _cursor;
        private readonly List<TMP_Text> _tmps = new List<TMP_Text>(512);
        private readonly List<UnityEngine.UI.Text> _uis = new List<UnityEngine.UI.Text>(128);
        private readonly Dictionary<int, string> _seen = new Dictionary<int, string>();
        private const int PerFrame = 30;      // 프레임당 처리 개수 (스파이크 방지)
        private const float RefreshEvery = 3f; // 목록 재수집 주기

        public void Tick()
        {
            if (_dead) return;
            try
            {
                int total = _tmps.Count + _uis.Count;
                if (_cursor >= total && Time.unscaledTime >= _nextRefresh)
                {
                    RefreshLists();
                    _nextRefresh = Time.unscaledTime + RefreshEvery;
                    _cursor = 0;
                    total = _tmps.Count + _uis.Count;
                }
                int end = Math.Min(_cursor + PerFrame, total);
                for (; _cursor < end; _cursor++)
                {
                    if (_cursor < _tmps.Count) ProcessTmp(_tmps[_cursor]);
                    else ProcessUi(_uis[_cursor - _tmps.Count]);
                }
            }
            catch (Exception e)
            {
                _errors++;
                ModLog.Warn("fixer error(" + _errors + "): " + e.Message);
                if (_errors >= 5) { _dead = true; ModLog.Error("too many errors - fixer disabled"); }
            }
            if (Time.unscaledTime >= _nextStat)
            {
                _nextStat = Time.unscaledTime + 60f;
                ModLog.Info("stats: replaced=" + _replaced + " tracked=" + (_tmps.Count + _uis.Count));
            }
        }

        private void RefreshLists()
        {
            _tmps.Clear();
            _uis.Clear();
            var t = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<TMP_Text>());
            if (t != null) { foreach (var o in t) { var c = o.TryCast<TMP_Text>(); if (c != null) _tmps.Add(c); } }
            var u = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<UnityEngine.UI.Text>());
            if (u != null) { foreach (var o in u) { var c = o.TryCast<UnityEngine.UI.Text>(); if (c != null) _uis.Add(c); } }
        }

        private void ProcessTmp(TMP_Text tmp)
        {
            try
            {
                if (tmp == null || !tmp.isActiveAndEnabled) return;
                string cur = tmp.text;
                string outp = Check(cur, tmp.GetInstanceID());
                if (outp != null) { tmp.text = outp; _replaced++; }
            }
            catch { }
        }

        private void ProcessUi(UnityEngine.UI.Text ut)
        {
            try
            {
                if (ut == null || !ut.isActiveAndEnabled) return;
                string cur = ut.text;
                string outp = Check(cur, ut.GetInstanceID());
                if (outp != null) { ut.text = outp; _replaced++; }
            }
            catch { }
        }

        private string Check(string cur, int id)
        {
            if (string.IsNullOrEmpty(cur)) return null;
            string last;
            if (_seen.TryGetValue(id, out last) && last == cur) return null;

            string fixedText = Translator.Translate(cur);
            if (fixedText != null && fixedText != cur)
            {
                _seen[id] = fixedText;
                return fixedText;
            }
            _seen[id] = cur;
            return null;
        }
    }
}
