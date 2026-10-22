using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FishGame.Core;
using FishGame.Data;
using UnityEditor;
using UnityEngine;

namespace FishGame.EditorTools
{
    /// <summary>
    /// 밸런스 엑셀 → 게임 데이터(에셋) 동기화. (R13: 구글 시트 → 프로젝트 안 엑셀로 원본을 옮김)
    ///
    ///   원본    프로젝트 폴더/Balance/물고기게임_밸런스.xlsx  (탭: 플레이어 · 스킬 · 물고기 · 구역)
    ///   보기용  저장할 때마다 구글 드라이브 FishGame/시트 폴더로 같은 파일을 복사한다 (드라이브 쪽을 고쳐도 반영 안 됨)
    ///
    ///   열 제목의 [필드이름]이 곧 C# 필드 이름이다. [ ]가 없는 열은 참고용이라 무시한다.
    ///     플레이어   [key] 열의 필드에 [value] 열의 값을 GameDatabase에 넣는다 (행 단위 키-값)
    ///     스킬       [id] = SkillNode.id
    ///     물고기     [id] = FishSpecies 에셋 이름
    ///     구역       [id] = ZoneData 에셋 이름
    ///
    /// 엑셀에서 저장하면(파일 수정 시각이 바뀌면) 1초 안에 읽어 바뀐 값만 에셋에 쓰고 저장한다.
    /// 엑셀이 파일을 열고 있어도 읽기 공유로 읽는다. xlsx는 zip + XML이라 외부 라이브러리 없이 직접 푼다.
    /// </summary>
    [InitializeOnLoad]
    public static class BalanceSheetSync
    {
        public const string ExcelRelativePath = "Balance/물고기게임_밸런스.xlsx";
        const string DefaultDriveFolder = "E:/내 드라이브/FishGame/시트";
        const string PrefAuto = "FishGame.BalanceExcel.Auto";
        const string PrefDrive = "FishGame.BalanceExcel.DriveFolder";
        static string PrefApplied => "FishGame.BalanceExcel.Applied." + Application.dataPath;
        const double PollSeconds = 1d;

        static readonly string[] Tabs = { "플레이어", "스킬", "물고기", "구역" };
        const string Menu = "FishGame/7. 밸런스 엑셀/";
        const string AutoMenu = Menu + "자동 반영 (엑셀 저장하면 바로)";

        public static string ExcelPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ExcelRelativePath));

        public static bool AutoSync
        {
            get => EditorPrefs.GetBool(PrefAuto, true);
            set => EditorPrefs.SetBool(PrefAuto, value);
        }

        /// <summary>보기용 복사본을 둘 구글 드라이브 폴더. 비우면 복사하지 않는다.</summary>
        public static string DriveFolder
        {
            get => EditorPrefs.GetString(PrefDrive, DefaultDriveFolder);
            set => EditorPrefs.SetString(PrefDrive, value ?? "");
        }

        static double _nextPoll;
        static DateTime _lastWrite;
        static string _lastError;

        static BalanceSheetSync()
        {
            EditorApplication.update += Tick;
            EditorApplication.focusChanged += focused => { if (focused) _nextPoll = 0; };
            // 마지막으로 반영한 파일 시각 — Unity가 꺼져 있는 동안 엑셀을 고쳤으면 켜자마자 반영된다
            long ticks;
            _lastWrite = long.TryParse(EditorPrefs.GetString(PrefApplied, ""), out ticks) ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
            _nextPoll = EditorApplication.timeSinceStartup + 2d;
        }

        // ── 메뉴 ──────────────────────────────────────────────────
        [MenuItem(Menu + "엑셀 열기", false, 7)]
        static void OpenExcel()
        {
            if (!File.Exists(ExcelPath)) { Debug.LogWarning($"[밸런스 엑셀] 파일이 없습니다: {ExcelPath}"); return; }
            EditorUtility.OpenWithDefaultApp(ExcelPath);
        }

        [MenuItem(Menu + "지금 불러오기", false, 8)]
        static void PullNow() { if (Load(manual: true)) MarkApplied(SafeWriteTime()); }

        [MenuItem(AutoMenu, false, 9)]
        static void ToggleAuto()
        {
            AutoSync = !AutoSync;
            Debug.Log($"[밸런스 엑셀] 자동 반영 {(AutoSync ? "켬" : "끔")}");
        }

        [MenuItem(AutoMenu, true)]
        static bool ToggleAutoValidate()
        {
            UnityEditor.Menu.SetChecked(AutoMenu, AutoSync);
            return true;
        }

        [MenuItem(Menu + "폴더 열기", false, 20)]
        static void RevealExcel() => EditorUtility.RevealInFinder(File.Exists(ExcelPath) ? ExcelPath : Path.GetDirectoryName(ExcelPath));

        [MenuItem(Menu + "구글 드라이브 복사 위치…", false, 21)]
        static void SetDriveFolder()
        {
            string start = Directory.Exists(DriveFolder) ? DriveFolder : "";
            string picked = EditorUtility.OpenFolderPanel("보기용 복사본을 둘 구글 드라이브 폴더 (취소하면 복사 안 함)", start, "");
            DriveFolder = picked;
            Debug.Log(string.IsNullOrEmpty(picked) ? "[밸런스 엑셀] 드라이브 복사를 껐습니다." : $"[밸런스 엑셀] 드라이브 복사 위치: {picked}");
            if (!string.IsNullOrEmpty(picked) && File.Exists(ExcelPath)) CopyToDrive(File.ReadAllBytes(ExcelPath), true);
        }

        // ── 감시 ──────────────────────────────────────────────────
        static DateTime SafeWriteTime()
        {
            try { return File.Exists(ExcelPath) ? File.GetLastWriteTimeUtc(ExcelPath) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        static void Tick()
        {
            if (!AutoSync || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + PollSeconds;

            var t = SafeWriteTime();
            if (t == DateTime.MinValue || t == _lastWrite) return;
            if (Load(manual: false)) MarkApplied(t);   // 저장 중이라 못 읽었으면 다음 틱에 다시
        }

        static void MarkApplied(DateTime t)
        {
            _lastWrite = t;
            EditorPrefs.SetString(PrefApplied, t.Ticks.ToString());
        }

        /// <summary>엑셀을 읽어 반영한다. 파일을 읽지 못했으면 false (다음에 다시 시도).</summary>
        static bool Load(bool manual)
        {
            if (!File.Exists(ExcelPath))
            {
                if (manual) Debug.LogWarning($"[밸런스 엑셀] 파일이 없습니다: {ExcelPath}");
                return true;
            }

            byte[] bytes;
            Dictionary<string, List<string[]>> tabs;
            try
            {
                bytes = ReadShared(ExcelPath);
                tabs = XlsxReader.Read(bytes, Tabs);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
            {
                // 엑셀이 저장하는 도중이면 잠깐 잠기거나 반쯤 쓰인 파일이 보인다
                if (manual) Debug.LogWarning("[밸런스 엑셀] 파일을 읽지 못했습니다 (저장 중?): " + e.Message);
                return false;
            }
            catch (Exception e)
            {
                string msg = "[밸런스 엑셀] 엑셀을 해석하지 못했습니다: " + e.Message;
                if (manual || msg != _lastError) Debug.LogWarning(msg);
                _lastError = msg;
                return true;
            }
            _lastError = null;

            foreach (var tab in Tabs)
                if (!tabs.ContainsKey(tab)) Debug.LogWarning($"[밸런스 엑셀] '{tab}' 탭이 없습니다. 탭 이름을 바꾸지 말아 주세요.");

            var db = FindDatabase();
            if (db == null) { Debug.LogWarning("[밸런스 엑셀] GameDatabase 에셋을 찾지 못했습니다."); return true; }

            int n = Apply(db, tabs, out string report);
            if (n > 0) Debug.Log($"[밸런스 엑셀] 값 {n}개 반영\n{report}");
            else if (manual) Debug.Log("[밸런스 엑셀] 바뀐 값이 없습니다." + (report.Length > 0 ? "\n" + report : ""));

            CopyToDrive(bytes, manual);
            return true;
        }

        static byte[] ReadShared(string path)
        {
            // 엑셀이 열고 있는 파일도 읽을 수 있게 쓰기 공유까지 허용한다
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                return ms.ToArray();
            }
        }

        /// <summary>보기용 복사본을 구글 드라이브 폴더에 둔다 (드라이브 데스크톱 앱이 올려 준다).</summary>
        static void CopyToDrive(byte[] bytes, bool verbose)
        {
            string folder = DriveFolder;
            if (string.IsNullOrWhiteSpace(folder)) return;
            if (!Directory.Exists(folder))
            {
                if (verbose) Debug.LogWarning($"[밸런스 엑셀] 드라이브 폴더가 없어 복사하지 않았습니다: {folder}");
                return;
            }
            string dest = Path.Combine(folder, Path.GetFileName(ExcelRelativePath));
            try
            {
                if (File.Exists(dest) && File.ReadAllBytes(dest).SequenceEqual(bytes)) return;
                File.WriteAllBytes(dest, bytes);
                if (verbose) Debug.Log($"[밸런스 엑셀] 드라이브에 복사: {dest}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[밸런스 엑셀] 드라이브 복사 실패 ({dest}): {e.Message}");
            }
        }

        /// <summary>엑셀 값을 다시 덮어쓴다. 콘텐츠 에셋 생성 직후에 부른다.</summary>
        public static void ReapplyCache(GameDatabase db)
        {
            if (db == null || !File.Exists(ExcelPath)) return;
            try
            {
                var tabs = XlsxReader.Read(ReadShared(ExcelPath), Tabs);
                int n = Apply(db, tabs, out string report);
                Debug.Log($"[밸런스 엑셀] 콘텐츠 생성 후 엑셀 값 {n}개를 다시 적용했습니다." + (n > 0 ? "\n" + report : ""));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[밸런스 엑셀] 콘텐츠 생성 후 엑셀을 읽지 못했습니다: " + e.Message);
            }
        }

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
        static int Apply(GameDatabase db, Dictionary<string, List<string[]>> tabs, out string report)
        {
            var log = new StringBuilder();
            int changed = 0;
            var dirty = new HashSet<UnityEngine.Object>();

            if (tabs.TryGetValue("플레이어", out var player))
                changed += ApplyKeyValue(db, player, log, dirty);

            if (tabs.TryGetValue("스킬", out var skills))
            {
                var map = new Dictionary<string, UnityEngine.Object>();
                foreach (var s in db.skills) if (s != null && !string.IsNullOrEmpty(s.id)) map[s.id] = s;
                changed += ApplyTable("스킬", skills, map, log, dirty);
            }
            if (tabs.TryGetValue("물고기", out var fish))
            {
                var map = new Dictionary<string, UnityEngine.Object>();
                foreach (var s in db.allFish) if (s != null) map[s.name] = s;
                changed += ApplyTable("물고기", fish, map, log, dirty);
            }
            if (tabs.TryGetValue("구역", out var zones))
            {
                var map = new Dictionary<string, UnityEngine.Object>();
                foreach (var z in db.zones) if (z != null) map[z.name] = z;
                changed += ApplyTable("구역", zones, map, log, dirty);
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
        static string Cell(string[] row, int c) => c >= 0 && c < row.Length ? row[c] ?? "" : "";

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

        // ── xlsx 읽기 (zip + XML) ─────────────────────────────────
        static class XlsxReader
        {
            /// <summary>원하는 탭들을 행 목록으로. 셀 값은 엑셀에 저장된 원래 값(숫자는 '.' 소수점 그대로).</summary>
            public static Dictionary<string, List<string[]>> Read(byte[] bytes, string[] wanted)
            {
                var result = new Dictionary<string, List<string[]>>();
                using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
                {
                    var shared = ReadSharedStrings(zip);
                    var wb = Load(zip, "xl/workbook.xml");
                    var rels = Load(zip, "xl/_rels/workbook.xml.rels");
                    if (wb == null || rels == null) throw new InvalidDataException("workbook.xml이 없습니다");

                    var targets = new Dictionary<string, string>();
                    foreach (var r in rels.Descendants().Where(e => e.Name.LocalName == "Relationship"))
                        targets[(string)r.Attribute("Id")] = (string)r.Attribute("Target");

                    foreach (var sheet in wb.Descendants().Where(e => e.Name.LocalName == "sheet"))
                    {
                        string name = ((string)sheet.Attribute("name") ?? "").Trim();
                        if (Array.IndexOf(wanted, name) < 0) continue;
                        var rid = sheet.Attributes().FirstOrDefault(a => a.Name.LocalName == "id");
                        if (rid == null || !targets.TryGetValue(rid.Value, out var target)) continue;
                        string path = target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
                        var doc = Load(zip, path);
                        if (doc != null) result[name] = ReadSheet(doc, shared);
                    }
                }
                return result;
            }

            static XDocument Load(ZipArchive zip, string path)
            {
                var entry = zip.GetEntry(path) ?? zip.Entries.FirstOrDefault(e => e.FullName.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (entry == null) return null;
                using (var s = entry.Open()) return XDocument.Load(s);
            }

            static List<string> ReadSharedStrings(ZipArchive zip)
            {
                var list = new List<string>();
                var doc = Load(zip, "xl/sharedStrings.xml");
                if (doc == null) return list;
                foreach (var si in doc.Root.Elements().Where(e => e.Name.LocalName == "si"))
                    list.Add(TextOf(si));
                return list;
            }

            /// <summary>&lt;t&gt;들을 이어 붙인다. 서식이 섞인 글(rich text)과 윗첨자 발음(rPh)을 구분한다.</summary>
            static string TextOf(XElement e)
            {
                var sb = new StringBuilder();
                foreach (var t in e.Descendants().Where(x => x.Name.LocalName == "t"))
                    if (t.Parent == null || t.Parent.Name.LocalName != "rPh") sb.Append(t.Value);
                return sb.ToString();
            }

            static List<string[]> ReadSheet(XDocument doc, List<string> shared)
            {
                var rows = new List<string[]>();
                foreach (var row in doc.Descendants().Where(e => e.Name.LocalName == "row"))
                {
                    int rowIndex = (int?)row.Attribute("r") ?? rows.Count + 1;
                    var cells = new SortedDictionary<int, string>();
                    int next = 0;
                    foreach (var c in row.Elements().Where(e => e.Name.LocalName == "c"))
                    {
                        string r = (string)c.Attribute("r");
                        int col = r != null ? ColumnIndex(r) : next;
                        next = col + 1;
                        cells[col] = CellValue(c, shared);
                    }
                    while (rows.Count < rowIndex - 1) rows.Add(new string[0]);   // 빈 행 자리
                    int width = cells.Count > 0 ? cells.Keys.Max() + 1 : 0;
                    var arr = new string[width];
                    foreach (var kv in cells) arr[kv.Key] = kv.Value;
                    rows.Add(arr);
                }
                return rows;
            }

            static string CellValue(XElement c, List<string> shared)
            {
                string type = (string)c.Attribute("t");
                var v = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v");
                switch (type)
                {
                    case "s":
                        return v != null && int.TryParse(v.Value, out int i) && i >= 0 && i < shared.Count ? shared[i] : "";
                    case "inlineStr":
                        var isEl = c.Elements().FirstOrDefault(e => e.Name.LocalName == "is");
                        return isEl != null ? TextOf(isEl) : "";
                    case "b":
                        return v != null && v.Value == "1" ? "1" : "0";
                    case "e":
                        return "";
                    default:   // 숫자 · 수식 결과(str)
                        return v?.Value ?? "";
                }
            }

            static int ColumnIndex(string cellRef)
            {
                int col = 0;
                foreach (char ch in cellRef)
                {
                    if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1);
                    else if (ch >= 'a' && ch <= 'z') col = col * 26 + (ch - 'a' + 1);
                    else break;
                }
                return col - 1;
            }
        }
    }
}
