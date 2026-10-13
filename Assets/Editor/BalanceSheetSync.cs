using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FishGame.Core;
using FishGame.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace FishGame.EditorTools
{
    /// <summary>
    /// 구글 밸런스 시트 → 게임 데이터(에셋) 동기화.
    ///
    ///   시트 탭 4개를 CSV로 받아(gviz) GameDatabase · SkillNode · FishSpecies · ZoneData에 써 넣는다.
    ///   열 제목의 [필드이름]이 곧 C# 필드 이름이다. [ ]가 없는 열은 참고용이라 무시한다.
    ///
    ///   플레이어   [key] 열의 필드에 [value] 열의 값을 GameDatabase에 넣는다 (행 단위 키-값)
    ///   스킬       [id] = SkillNode.id
    ///   물고기     [id] = FishSpecies 에셋 이름
    ///   구역       [id] = ZoneData 에셋 이름
    ///
    /// 자동 동기화가 켜져 있으면 몇 초마다 시트를 확인해, 바뀐 값만 에셋에 쓰고 저장한다.
    /// 플레이 중에도 반영된다 (ScriptableObject라 다음 판부터, 스킬트리 화면은 즉시).
    ///
    /// 시트는 "링크가 있는 모든 사용자 — 뷰어"로 공유돼 있어야 한다 (로그인 없이 CSV를 받기 때문).
    /// 받은 CSV는 Library/BalanceSheetCache에 보관 — 콘텐츠 에셋 생성(1번 메뉴)이 끝나면 다시 덮어쓴다.
    /// </summary>
    [InitializeOnLoad]
    public static class BalanceSheetSync
    {
        const string DefaultSheetId = "1sri-A8ohQzfXva2lW2mgFyNMDWKW29oDTpHbQJP1nw4";
        const string PrefSheetId = "FishGame.BalanceSheet.Id";
        const string PrefAuto = "FishGame.BalanceSheet.Auto";
        const double PollSeconds = 5d;
        const int TimeoutSeconds = 15;

        static readonly string[] Tabs = { "플레이어", "스킬", "물고기", "구역" };
        static readonly string CacheDir = Path.Combine("Library", "BalanceSheetCache");

        const string Menu = "FishGame/7. 밸런스 시트/";

        public static string SheetId
        {
            get
            {
                var id = EditorPrefs.GetString(PrefSheetId, DefaultSheetId);
                return string.IsNullOrWhiteSpace(id) ? DefaultSheetId : id.Trim();
            }
            set => EditorPrefs.SetString(PrefSheetId, ExtractId(value));
        }

        public static bool AutoSync
        {
            get => EditorPrefs.GetBool(PrefAuto, true);
            set => EditorPrefs.SetBool(PrefAuto, value);
        }

        static double _nextPoll;
        static Fetch _pending;
        static bool _manual;
        static string _lastError;

        static BalanceSheetSync()
        {
            EditorApplication.update += Tick;
            _nextPoll = EditorApplication.timeSinceStartup + 3d;
        }

        // ── 메뉴 ──────────────────────────────────────────────────
        [MenuItem(Menu + "지금 불러오기", false, 7)]
        static void PullNow()
        {
            _manual = true;
            StartFetch();
        }

        [MenuItem(Menu + "자동 동기화 (저장하면 몇 초 안에 반영)", false, 8)]
        static void ToggleAuto()
        {
            AutoSync = !AutoSync;
            Debug.Log($"[밸런스 시트] 자동 동기화 {(AutoSync ? "켬" : "끔")}");
        }

        [MenuItem(Menu + "자동 동기화 (저장하면 몇 초 안에 반영)", true)]
        static bool ToggleAutoValidate()
        {
            UnityEditor.Menu.SetChecked(Menu + "자동 동기화 (저장하면 몇 초 안에 반영)", AutoSync);
            return true;
        }

        [MenuItem(Menu + "시트 열기", false, 20)]
        static void OpenSheet() => Application.OpenURL($"https://docs.google.com/spreadsheets/d/{SheetId}/edit");

        [MenuItem(Menu + "시트 주소 설정…", false, 21)]
        static void SetAddress() => SheetAddressWindow.Open();

        // ── 폴링 ──────────────────────────────────────────────────
        static void Tick()
        {
            if (_pending != null)
            {
                if (!_pending.IsDone) return;
                var f = _pending;
                _pending = null;
                Finish(f);
                return;
            }
            if (!AutoSync || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            StartFetch();
        }

        static void StartFetch()
        {
            if (_pending != null) return;
            _nextPoll = EditorApplication.timeSinceStartup + PollSeconds;
            _pending = new Fetch(SheetId);
        }

        static void Finish(Fetch f)
        {
            bool manual = _manual;
            _manual = false;

            if (f.Error != null)
            {
                // 같은 오류는 한 번만 알린다 (5초마다 콘솔을 채우지 않게)
                if (manual || f.Error != _lastError)
                    Debug.LogWarning("[밸런스 시트] 불러오지 못했습니다: " + f.Error);
                _lastError = f.Error;
                _nextPoll = EditorApplication.timeSinceStartup + PollSeconds * 6;   // 오류면 30초 뒤에 다시
                return;
            }
            if (_lastError != null) Debug.Log("[밸런스 시트] 다시 연결됐습니다.");
            _lastError = null;

            Directory.CreateDirectory(CacheDir);
            foreach (var kv in f.Csv) File.WriteAllText(CachePath(kv.Key), kv.Value, Encoding.UTF8);

            var db = FindDatabase();
            if (db == null) { Debug.LogWarning("[밸런스 시트] GameDatabase 에셋을 찾지 못했습니다."); return; }
            int n = Apply(db, f.Csv, out string report);
            if (n > 0) Debug.Log($"[밸런스 시트] 값 {n}개 반영\n{report}");
            else if (manual) Debug.Log("[밸런스 시트] 바뀐 값이 없습니다." + (report.Length > 0 ? "\n" + report : ""));
        }

        /// <summary>마지막으로 받은 시트 값을 다시 덮어쓴다. 콘텐츠 에셋 생성 직후에 부른다.</summary>
        public static void ReapplyCache(GameDatabase db)
        {
            if (db == null) return;
            var csv = new Dictionary<string, string>();
            foreach (var tab in Tabs)
                if (File.Exists(CachePath(tab))) csv[tab] = File.ReadAllText(CachePath(tab), Encoding.UTF8);
            if (csv.Count == 0) return;
            int n = Apply(db, csv, out string report);
            Debug.Log($"[밸런스 시트] 콘텐츠 생성 후 시트 값 {n}개를 다시 적용했습니다." + (n > 0 ? "\n" + report : ""));
        }

        static string CachePath(string tab) => Path.Combine(CacheDir, tab + ".csv");

        static GameDatabase FindDatabase()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(ContentGenerator.DatabasePath);
            if (db != null) return db;
            foreach (var guid in AssetDatabase.FindAssets("t:GameDatabase"))
            {
                db = AssetDatabase.LoadAssetAtPath<GameDatabase>(AssetDatabase.GUIDToAssetPath(guid));
                if (db != null) return db;
            }
            return null;
        }

        // ── 적용 ──────────────────────────────────────────────────
        static int Apply(GameDatabase db, Dictionary<string, string> csv, out string report)
        {
            var log = new StringBuilder();
            int changed = 0;
            var dirty = new HashSet<UnityEngine.Object>();

            if (csv.TryGetValue("플레이어", out var player))
                changed += ApplyKeyValue(db, ParseCsv(player), log, dirty);

            if (csv.TryGetValue("스킬", out var skills))
            {
                var map = new Dictionary<string, UnityEngine.Object>();
                foreach (var s in db.skills) if (s != null && !string.IsNullOrEmpty(s.id)) map[s.id] = s;
                changed += ApplyTable("스킬", ParseCsv(skills), map, log, dirty);
            }
            if (csv.TryGetValue("물고기", out var fish))
            {
                var map = new Dictionary<string, UnityEngine.Object>();
                foreach (var s in db.allFish) if (s != null) map[s.name] = s;
                changed += ApplyTable("물고기", ParseCsv(fish), map, log, dirty);
            }
            if (csv.TryGetValue("구역", out var zones))
            {
                var map = new Dictionary<string, UnityEngine.Object>();
                foreach (var z in db.zones) if (z != null) map[z.name] = z;
                changed += ApplyTable("구역", ParseCsv(zones), map, log, dirty);
            }

            if (dirty.Count > 0)
            {
                foreach (var o in dirty) EditorUtility.SetDirty(o);
                AssetDatabase.SaveAssets();
                // 플레이 중이면 스킬트리 · HUD가 새 이름 · 가격을 다시 그리게 한다
                if (Application.isPlaying && GameManager.Instance != null) GameManager.Instance.NotifyProgressChanged();
            }
            report = log.ToString().TrimEnd();
            return changed;
        }

        /// <summary>플레이어 탭: 행마다 [key] 필드에 [value] 값.</summary>
        static int ApplyKeyValue(GameDatabase db, List<string[]> rows, StringBuilder log, HashSet<UnityEngine.Object> dirty)
        {
            if (rows.Count == 0) return 0;
            var fields = HeaderFields(rows[0]);
            int keyCol = Array.IndexOf(fields, "key"), valCol = Array.IndexOf(fields, "value");
            if (keyCol < 0 || valCol < 0) { log.AppendLine("· 플레이어 탭에 [key] / [value] 열이 없습니다."); return 0; }

            var so = new SerializedObject(db);
            int changed = 0;
            for (int r = 1; r < rows.Count; r++)
            {
                string key = Cell(rows[r], keyCol).Trim();
                if (key.Length == 0) continue;
                var p = so.FindProperty(key);
                if (p == null) { log.AppendLine($"· 플레이어: [{key}] 필드를 찾을 수 없음 (무시)"); continue; }
                if (TrySet(p, Cell(rows[r], valCol), out string before, out string after))
                {
                    changed++;
                    log.AppendLine($"· 플레이어 {key}: {before} → {after}");
                }
            }
            if (changed > 0) { so.ApplyModifiedPropertiesWithoutUndo(); dirty.Add(db); }
            return changed;
        }

        /// <summary>스킬 · 물고기 · 구역 탭: [id] 열로 에셋을 찾고 나머지 [필드] 열을 넣는다.</summary>
        static int ApplyTable(string tab, List<string[]> rows, Dictionary<string, UnityEngine.Object> map,
                              StringBuilder log, HashSet<UnityEngine.Object> dirty)
        {
            if (rows.Count == 0) return 0;
            var fields = HeaderFields(rows[0]);
            int idCol = Array.IndexOf(fields, "id");
            if (idCol < 0) { log.AppendLine($"· {tab} 탭에 [id] 열이 없습니다."); return 0; }

            int changed = 0;
            var missingField = new HashSet<string>();
            for (int r = 1; r < rows.Count; r++)
            {
                string id = Cell(rows[r], idCol).Trim();
                if (id.Length == 0) continue;
                if (!map.TryGetValue(id, out var asset)) { log.AppendLine($"· {tab}: '{id}' 에셋이 없음 (무시)"); continue; }

                var so = new SerializedObject(asset);
                int before = changed;
                for (int c = 0; c < fields.Length; c++)
                {
                    string f = fields[c];
                    if (f == null || c == idCol) continue;
                    var p = so.FindProperty(f);
                    if (p == null) { missingField.Add(f); continue; }
                    if (TrySet(p, Cell(rows[r], c), out string b, out string a))
                    {
                        changed++;
                        log.AppendLine($"· {tab} {id}.{f}: {b} → {a}");
                    }
                }
                if (changed > before) { so.ApplyModifiedPropertiesWithoutUndo(); dirty.Add(asset); }
            }
            foreach (var f in missingField) log.AppendLine($"· {tab}: [{f}] 필드를 찾을 수 없음 (무시)");
            return changed;
        }

        /// <summary>값이 실제로 달라졌을 때만 써 넣는다. 빈 칸은 글자 필드가 아니면 건너뛴다.</summary>
        static bool TrySet(SerializedProperty p, string raw, out string before, out string after)
        {
            before = after = null;
            string s = (raw ?? "").Trim();
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float:
                {
                    if (!TryNumber(s, out double v)) return false;
                    float nv = (float)v;
                    if (Math.Abs(p.floatValue - nv) <= 1e-6f * Math.Max(1f, Math.Abs(nv))) return false;
                    before = Fmt(p.floatValue); after = Fmt(nv);
                    p.floatValue = nv;
                    return true;
                }
                case SerializedPropertyType.Integer:
                {
                    if (!TryNumber(s, out double v)) return false;
                    int nv = (int)Math.Round(v);
                    if (p.intValue == nv) return false;
                    before = p.intValue.ToString(); after = nv.ToString();
                    p.intValue = nv;
                    return true;
                }
                case SerializedPropertyType.Boolean:
                {
                    if (s.Length == 0) return false;
                    string l = s.ToLowerInvariant();
                    bool nv = l == "1" || l == "true" || l == "o" || l == "y" || l == "yes" || l == "켬";
                    if (!nv && !(l == "0" || l == "false" || l == "x" || l == "n" || l == "no" || l == "끔")) return false;
                    if (p.boolValue == nv) return false;
                    before = p.boolValue ? "1" : "0"; after = nv ? "1" : "0";
                    p.boolValue = nv;
                    return true;
                }
                case SerializedPropertyType.Enum:
                {
                    if (s.Length == 0) return false;
                    var names = p.enumNames;
                    int idx = -1;
                    for (int i = 0; i < names.Length; i++)
                        if (string.Equals(names[i], s, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                    if (idx < 0) return false;
                    if (p.enumValueIndex == idx) return false;
                    before = p.enumValueIndex >= 0 && p.enumValueIndex < names.Length ? names[p.enumValueIndex] : "?";
                    after = names[idx];
                    p.enumValueIndex = idx;
                    return true;
                }
                case SerializedPropertyType.String:
                {
                    string nv = (raw ?? "").Replace("\r\n", "\n").Trim();
                    if (p.stringValue == nv) return false;
                    before = Short(p.stringValue); after = Short(nv);
                    p.stringValue = nv;
                    return true;
                }
            }
            return false;
        }

        static bool TryNumber(string s, out double v)
        {
            s = s.Replace(",", "").Replace(" ", "");
            if (s.EndsWith("%") && double.TryParse(s.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { v /= 100d; return true; }
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        static string Fmt(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        static string Short(string s) => s == null ? "" : s.Length <= 24 ? $"\"{s}\"" : $"\"{s.Substring(0, 24)}…\"";
        static string Cell(string[] row, int c) => c >= 0 && c < row.Length ? row[c] : "";

        /// <summary>열 제목에서 맨 마지막 [ ] 안의 필드 이름. 없으면 null (참고용 열).</summary>
        static string[] HeaderFields(string[] header)
        {
            var result = new string[header.Length];
            for (int i = 0; i < header.Length; i++)
            {
                string h = header[i] ?? "";
                int close = h.LastIndexOf(']');
                int open = close > 0 ? h.LastIndexOf('[', close) : -1;
                if (open >= 0 && close > open + 1) result[i] = h.Substring(open + 1, close - open - 1).Trim();
            }
            return result;
        }

        /// <summary>RFC 4180 CSV — 따옴표 안의 쉼표 · 줄바꿈 · "" 처리.</summary>
        static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (ch == '\n' || ch == '\r')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Clear();
                    rows.Add(row.ToArray()); row.Clear();
                }
                else cell.Append(ch);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row.ToArray()); }
            return rows;
        }

        static string ExtractId(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            input = input.Trim();
            const string marker = "/spreadsheets/d/";
            int i = input.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return input;
            string rest = input.Substring(i + marker.Length);
            int end = rest.IndexOfAny(new[] { '/', '?', '#' });
            return end < 0 ? rest : rest.Substring(0, end);
        }

        // ── 내려받기 ──────────────────────────────────────────────
        sealed class Fetch
        {
            readonly Dictionary<string, UnityWebRequest> _requests = new Dictionary<string, UnityWebRequest>();
            public readonly Dictionary<string, string> Csv = new Dictionary<string, string>();
            public string Error;
            bool _collected;

            public Fetch(string sheetId)
            {
                long bust = DateTime.UtcNow.Ticks;
                foreach (var tab in Tabs)
                {
                    string url = $"https://docs.google.com/spreadsheets/d/{sheetId}/gviz/tq?tqx=out:csv&headers=1" +
                                 $"&sheet={Uri.EscapeDataString(tab)}&_={bust}";
                    var req = UnityWebRequest.Get(url);
                    req.timeout = TimeoutSeconds;
                    req.SetRequestHeader("Cache-Control", "no-cache");
                    req.SendWebRequest();
                    _requests[tab] = req;
                }
            }

            public bool IsDone
            {
                get
                {
                    foreach (var r in _requests.Values) if (!r.isDone) return false;
                    if (!_collected) Collect();
                    return true;
                }
            }

            void Collect()
            {
                _collected = true;
                foreach (var kv in _requests)
                {
                    var req = kv.Value;
                    try
                    {
                        if (req.result != UnityWebRequest.Result.Success)
                        {
                            Error = req.responseCode == 401 || req.responseCode == 403 || req.responseCode == 404
                                ? $"시트에 접근할 수 없습니다 (HTTP {req.responseCode}). 공유를 '링크가 있는 모든 사용자 — 뷰어'로 바꿔 주세요."
                                : $"'{kv.Key}' 탭: {req.error}";
                            continue;
                        }
                        string text = req.downloadHandler.text ?? "";
                        string type = req.GetResponseHeader("Content-Type") ?? "";
                        if (type.Contains("html") || text.TrimStart().StartsWith("<"))
                        {
                            Error = "로그인 화면이 돌아왔습니다. 시트 공유를 '링크가 있는 모든 사용자 — 뷰어'로 바꿔 주세요.";
                            continue;
                        }
                        // 탭 이름이 틀리면 gviz는 오류 대신 첫 탭을 돌려준다 — 머리글로 확인
                        string first = text.Length > 300 ? text.Substring(0, 300) : text;
                        bool looksRight = kv.Key == "플레이어" ? first.Contains("[key]") : first.Contains("[id]");
                        if (!looksRight) { Error = $"'{kv.Key}' 탭을 찾지 못했습니다. 탭 이름을 바꾸지 말아 주세요."; continue; }
                        Csv[kv.Key] = text;
                    }
                    finally { req.Dispose(); }
                }
            }
        }

        // ── 주소 입력 창 ──────────────────────────────────────────
        sealed class SheetAddressWindow : EditorWindow
        {
            string _value;

            public static void Open()
            {
                var w = CreateInstance<SheetAddressWindow>();
                w.titleContent = new GUIContent("밸런스 시트 주소");
                w._value = $"https://docs.google.com/spreadsheets/d/{SheetId}/edit";
                w.position = new Rect(200, 200, 560, 110);
                w.ShowUtility();
            }

            void OnGUI()
            {
                EditorGUILayout.LabelField("구글 시트 주소(또는 ID)를 붙여 넣으세요.");
                _value = EditorGUILayout.TextField(_value);
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("기본값으로"))
                        _value = $"https://docs.google.com/spreadsheets/d/{DefaultSheetId}/edit";
                    if (GUILayout.Button("저장하고 불러오기"))
                    {
                        SheetId = _value;
                        Debug.Log($"[밸런스 시트] 주소 저장: {SheetId}");
                        Close();
                        PullNow();
                    }
                }
            }
        }
    }
}
