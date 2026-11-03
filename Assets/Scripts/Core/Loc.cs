using System;
using System.Collections.Generic;
using System.Globalization;
using FishGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishGame
{
    /// <summary>게임 언어. 순서 = 설정 창 목록 순서 = 표의 열 순서.</summary>
    public enum Language { Korean = 0, English = 1, ChineseSimplified = 2, Russian = 3, Spanish = 4, PortugueseBR = 5 }

    /// <summary>
    /// 다국어. 한국어 문장 자체를 열쇠로 쓴다 — 코드는 Loc.T("탈출 시작")처럼 원래 한국어를 그대로 감싼다.
    ///
    ///   표   Resources/Localization/strings.tsv  (열: ko · en · zh · ru · es · pt)
    ///        칸이 비어 있거나 표에 없는 문장은 한국어 그대로 나온다.
    ///   형식 Loc.F("재화 {0}", n) — 번역문에도 {0}이 그대로 있어야 한다.
    ///   데이터 물고기 · 스킬 · 구역 이름과 설명도 화면에 띄울 때 Loc.T로 감싼다
    ///        (엑셀에서 이름을 바꾸면 그 이름은 표에 새로 넣기 전까지 한국어로 나온다).
    ///   씬   씬에 박힌 글자(TMP)는 LocTextBinder가 씬을 불러올 때마다 표에서 찾아 바꾼다.
    ///   글꼴 중국어는 한국어 글꼴에 없는 간체자가 있어 Resources/Fonts/NotoSansSC를 대체 글꼴로 붙인다.
    /// </summary>
    public static class Loc
    {
        public static readonly string[] NativeNames =
            { "한국어", "English", "简体中文", "Русский", "Español", "Português (Brasil)" };

        const string TablePath = "Localization/strings";
        const string ChineseFontPath = "Fonts/NotoSansSC-Regular";

        static Dictionary<string, string[]> _table;
        static Language _current;
        static bool _ready;

        /// <summary>언어가 바뀐 직후.</summary>
        public static event Action Changed;

        public static Language Current { get { EnsureReady(); return _current; } }

        static void EnsureReady()
        {
            if (_ready) return;
            _ready = true;
            LoadTable();
            _current = Resolve(GameSettings.Data.language);
            ApplyFont();
        }

        /// <summary>-1(자동)이면 운영체제 언어를 따른다. 지원하지 않는 언어는 영어.</summary>
        static Language Resolve(int setting)
        {
            if (setting >= 0 && setting < NativeNames.Length) return (Language)setting;
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Korean: return Language.Korean;
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.Chinese: return Language.ChineseSimplified;
                case SystemLanguage.Russian: return Language.Russian;
                case SystemLanguage.Spanish: return Language.Spanish;
                case SystemLanguage.Portuguese: return Language.PortugueseBR;
                default: return Language.English;
            }
        }

        /// <summary>설정 창에서 고른 언어로 바꾸고 저장한다.</summary>
        public static void SetLanguage(Language lang)
        {
            EnsureReady();
            GameSettings.Data.language = (int)lang;
            GameSettings.Save();
            if (_current == lang) return;
            _current = lang;
            ApplyFont();
            Changed?.Invoke();
        }

        // ── 번역 ────────────────────────────────────────────────
        public static string T(string ko)
        {
            if (string.IsNullOrEmpty(ko)) return ko;
            EnsureReady();
            if (_current == Language.Korean) return ko;
            if (_table != null && _table.TryGetValue(ko, out var row))
            {
                int col = (int)_current;
                if (col < row.Length && !string.IsNullOrEmpty(row[col])) return row[col];
            }
            return ko;
        }

        public static string F(string koFormat, params object[] args)
        {
            string fmt = T(koFormat);
            try { return string.Format(CultureInfo.CurrentCulture, fmt, args); }
            catch (FormatException)
            {
                // 번역문의 {0} 자리가 잘못돼도 게임이 멈추지 않게 한국어 원문으로
                try { return string.Format(CultureInfo.CurrentCulture, koFormat, args); }
                catch (FormatException) { return koFormat; }
            }
        }

        public static string[] T(string[] ko)
        {
            if (ko == null) return null;
            var r = new string[ko.Length];
            for (int i = 0; i < ko.Length; i++) r[i] = T(ko[i]);
            return r;
        }

        /// <summary>이 문장이 표에 있는가 (씬 글자 바인더가 씬 글자 중 번역할 것만 고른다).</summary>
        public static bool Has(string ko)
        {
            EnsureReady();
            return !string.IsNullOrEmpty(ko) && _table != null && _table.ContainsKey(ko);
        }

        // ── 표 ──────────────────────────────────────────────────
        static void LoadTable()
        {
            _table = new Dictionary<string, string[]>();
            var asset = Resources.Load<TextAsset>(TablePath);
            if (asset == null) { Debug.LogWarning("[Loc] 번역 표가 없습니다: Resources/" + TablePath); return; }

            var lines = asset.text.Split('\n');
            for (int i = 1; i < lines.Length; i++)   // 첫 줄은 머리글
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var cols = line.Split('\t');
                for (int c = 0; c < cols.Length; c++) cols[c] = Unescape(cols[c]);
                if (cols[0].Length == 0) continue;
                _table[cols[0]] = cols;
            }
        }

        static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\t", "\t");

        // ── 글꼴 ────────────────────────────────────────────────
        static TMP_FontAsset _chineseFallback;

        static void ApplyFont()
        {
            if (_current != Language.ChineseSimplified || _chineseFallback != null) return;
            var font = Resources.Load<Font>(ChineseFontPath);
            if (font == null) { Debug.LogWarning("[Loc] 중국어 글꼴이 없습니다: Resources/" + ChineseFontPath); return; }
            _chineseFallback = TMP_FontAsset.CreateFontAsset(font, 64, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                                                             2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (_chineseFallback == null) return;
            _chineseFallback.name = "NotoSansSC (runtime)";
            // 쓰이고 있는 글꼴들의 대체 목록에 붙인다. 에디터에서는 이 목록이 에셋에 남지 않게 끝날 때 떼어 낸다.
            foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (fa == _chineseFallback) continue;
                if (fa.fallbackFontAssetTable == null) fa.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (fa.fallbackFontAssetTable.Contains(_chineseFallback)) continue;
                fa.fallbackFontAssetTable.Add(_chineseFallback);
                _patched.Add(fa);
            }
            if (!_cleanupHooked) { _cleanupHooked = true; Application.quitting += RemoveFallback; }
        }

        static readonly List<TMP_FontAsset> _patched = new List<TMP_FontAsset>();
        static bool _cleanupHooked;

        static void RemoveFallback()
        {
            foreach (var fa in _patched)
                if (fa != null && fa.fallbackFontAssetTable != null) fa.fallbackFontAssetTable.Remove(_chineseFallback);
            _patched.Clear();
        }

        // ── 언어 변경 → 화면 다시 그리기 ─────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            EnsureReady();
            LocTextBinder.Install();
        }
    }

    /// <summary>
    /// 씬 · 프리팹에 박힌 TMP 글자를 번역한다. 처음 본 글자가 표에 있으면 그 원문을 기억해 두고,
    /// 씬을 불러올 때와 언어를 바꿀 때 다시 바꿔 넣는다. (코드가 매번 새로 쓰는 글자는 코드 쪽 Loc.T가 맡는다)
    /// </summary>
    public class LocTextBinder : MonoBehaviour
    {
        static LocTextBinder _instance;
        readonly Dictionary<TMP_Text, string> _source = new Dictionary<TMP_Text, string>();

        public static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("~LocTextBinder") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LocTextBinder>();
            SceneManager.sceneLoaded += (s, m) => _instance.Rebind();
            Loc.Changed += () => _instance.Retranslate();
            _instance.Rebind();
        }

        void Rebind()
        {
            var dead = new List<TMP_Text>();
            foreach (var kv in _source) if (kv.Key == null) dead.Add(kv.Key);
            foreach (var d in dead) _source.Remove(d);

            foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (_source.ContainsKey(t)) continue;
                if (Loc.Has(t.text)) _source[t] = t.text;
            }
            Retranslate();
        }

        void Retranslate()
        {
            foreach (var kv in _source)
                if (kv.Key != null) kv.Key.text = Loc.T(kv.Value);
        }
    }
}
