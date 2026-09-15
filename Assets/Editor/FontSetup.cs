#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace FishGame.EditorTools
{
    /// <summary>
    /// 프로젝트 전체 TMP 텍스트에 쓸 한글 폰트를 한 곳에서 관리한다.
    ///
    /// 폰트를 구하는 순서:
    ///   1) PreferredFontPath 의 TMP_FontAsset
    ///   2) 프로젝트 안 아무 데나 있는 "NotoSans / Nanum / Pretendard ..." TMP_FontAsset
    ///   3) TTF/OTF 원본만 있으면 → 동적(Dynamic) TMP_FontAsset 을 직접 만들어 준다
    ///      (Font Asset Creator 를 손으로 돌릴 필요가 없다)
    ///
    /// 적용은 3중으로 건다. 하나라도 빠지면 한글이 네모로 나온다.
    ///   · TMP 기본 폰트(Default Font Asset)  → 앞으로 만들어지는 모든 텍스트
    ///   · TMP 전역 폴백(Fallback Font Assets) → 이미 다른 폰트를 쓰는 텍스트
    ///   · 모든 씬·프리팹의 TMP_Text.font 직접 교체
    /// </summary>
    public static class FontSetup
    {
        /// <summary>기본 한글 폰트 경로. 없으면 원본 TTF로 여기에 만들어 준다.</summary>
        public const string PreferredFontPath = "Assets/Fonts/NotoSansKR-Regular SDF.asset";

        const string FontFolder = "Assets/Fonts";
        const string SceneFolder = "Assets/_Project/Scenes";

        /// <summary>TMP_FontAsset 을 이름으로 찾을 때 쓰는 키워드 (우선순위 순).</summary>
        static readonly string[] NameKeywords =
        {
            "NotoSansKR", "NotoSans", "Noto", "Pretendard", "Nanum", "SpoqaHanSans", "Malgun",
        };

        static TMP_FontAsset _cached;

        // ══════════════════════════════════════════════════════════
        //  폰트 찾기 / 만들기
        // ══════════════════════════════════════════════════════════
        public static TMP_FontAsset Font
        {
            get
            {
                if (_cached != null) return _cached;

                _cached = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PreferredFontPath);
                if (_cached != null) return _cached;

                _cached = FindExistingFontAsset();
                if (_cached != null) return _cached;

                // 원본 TTF/OTF 만 있는 경우 — 여기서 직접 만들어 준다.
                _cached = CreateFromSourceFont();
                return _cached;
            }
        }

        public static void ClearCache() => _cached = null;

        /// <summary>
        /// 이름에 한글 폰트 키워드가 들어간 TMP_FontAsset 을 찾는다.
        /// FindAssets 의 검색어 매칭은 공백·하이픈 때문에 잘 빗나가서,
        /// 전부 긁어온 뒤 이름으로 직접 거른다.
        /// </summary>
        static TMP_FontAsset FindExistingFontAsset()
        {
            var guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            foreach (var keyword in NameKeywords)
            {
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                    if (font == null) continue;

                    Debug.Log($"[FishGame] 한글 TMP 폰트를 찾았습니다: {path}");
                    return font;
                }
            }
            return null;
        }

        /// <summary>이름에 한글 폰트 키워드가 들어간 TTF/OTF 원본을 찾는다.</summary>
        static Font FindSourceFont()
        {
            var guids = AssetDatabase.FindAssets("t:Font");
            foreach (var keyword in NameKeywords)
            {
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                    if (font != null) return font;
                }
            }
            return null;
        }

        /// <summary>
        /// TTF/OTF 로부터 동적(Dynamic) TMP_FontAsset 을 만든다.
        ///
        /// 동적 모드를 쓰는 이유: 한글은 글자 수가 많아 미리 구워두면 아틀라스가 거대해진다.
        /// 동적 모드는 화면에 실제로 나온 글자만 그때그때 아틀라스에 채워 넣는다.
        /// </summary>
        static TMP_FontAsset CreateFromSourceFont()
        {
            var source = FindSourceFont();
            if (source == null) return null;

            EnsureFolder(FontFolder);

            // 인자: 원본, 샘플링 크기, 패딩, 렌더 모드, 아틀라스 W, H, 채우기 모드, 멀티 아틀라스
            var font = TMP_FontAsset.CreateFontAsset(
                source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);

            if (font == null)
            {
                Debug.LogError($"[FishGame] '{source.name}' 로 TMP 폰트를 만들지 못했습니다. " +
                               "Font Asset Creator 로 직접 만들어 주세요.");
                return null;
            }

            font.name = Path.GetFileNameWithoutExtension(PreferredFontPath);
            AssetDatabase.CreateAsset(font, PreferredFontPath);

            // 아틀라스 텍스처 / 머티리얼은 폰트 에셋의 서브에셋으로 같이 넣어야
            // 파일 하나로 관리되고, 다른 씬에서 참조가 끊기지 않는다.
            if (font.atlasTextures != null && font.atlasTextures.Length > 0 && font.atlasTextures[0] != null)
            {
                font.atlasTextures[0].name = font.name + " Atlas";
                AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
            }
            if (font.material != null)
            {
                font.material.name = font.name + " Material";
                AssetDatabase.AddObjectToAsset(font.material, font);
            }

            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(PreferredFontPath, ImportAssetOptions.ForceUpdate);

            Debug.Log($"[FishGame] '{AssetDatabase.GetAssetPath(source)}' 로 " +
                      $"한글 TMP 폰트를 새로 만들었습니다 → {PreferredFontPath}");

            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PreferredFontPath) ?? font;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>폰트가 없으면 경고를 남기고 false.</summary>
        public static bool WarnIfMissing()
        {
            if (Font != null) return true;
            Debug.LogWarning(
                "[FishGame] 한글 TMP 폰트를 찾지 못했습니다.\n" +
                $"기대 경로: {PreferredFontPath}\n" +
                "한글 TTF(예: NotoSansKR-Regular.ttf)를 프로젝트 어딘가에 넣어두면 " +
                "메뉴 실행 시 폰트 에셋을 자동으로 만들어 줍니다.");
            return false;
        }

        /// <summary>TMP_Text 하나에 폰트를 적용. 폰트가 없으면 아무것도 하지 않는다.</summary>
        public static void Apply(TMP_Text text)
        {
            if (text == null) return;
            var font = Font;
            if (font != null) text.font = font;
        }

        // ══════════════════════════════════════════════════════════
        //  일괄 적용
        // ══════════════════════════════════════════════════════════
        [MenuItem("FishGame/5. 모든 텍스트에 한글 폰트 적용", false, 5)]
        public static void ApplyToEverythingMenu() => ApplyToEverything(true);

        /// <summary>씬·프리팹·TMP 설정 전부에 한글 폰트를 건다.</summary>
        public static void ApplyToEverything(bool showDialog)
        {
            ClearCache();
            if (!WarnIfMissing())
            {
                if (showDialog)
                    EditorUtility.DisplayDialog("FishGame",
                        "한글 TMP 폰트를 찾지 못했습니다.\n\n" +
                        "한글 TTF 파일(NotoSansKR-Regular.ttf 등)을 프로젝트 안에 넣고\n" +
                        "다시 실행하면 폰트 에셋을 자동으로 만들어 줍니다.", "확인");
                return;
            }

            bool defaultSet  = TrySetTmpDefaultFont();
            bool fallbackSet = TryAddGlobalFallback();

            int prefabCount = ApplyToPrefabs();
            int sceneTexts = 0, sceneCount = 0;
            ApplyToAllScenes(ref sceneTexts, ref sceneCount);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string msg =
                $"폰트: {Font.name}\n\n" +
                $"· 씬 {sceneCount}개의 텍스트 {sceneTexts}개\n" +
                $"· 프리팹의 텍스트 {prefabCount}개\n" +
                $"· TMP 기본 폰트 {(defaultSet ? "변경됨" : "변경 실패")}\n" +
                $"· TMP 전역 폴백 {(fallbackSet ? "등록됨" : "등록 실패")}";

            Debug.Log("[FishGame] 폰트 적용 완료 — " + msg.Replace("\n", " "));
            if (showDialog) EditorUtility.DisplayDialog("FishGame 폰트 적용", msg, "확인");
        }

        /// <summary>
        /// 프로젝트의 모든 씬을 하나씩 열어 TMP_Text 의 폰트를 바꾸고 저장한다.
        /// 예전 버전은 "열려 있는 씬"만 훑어서, 닫힌 씬은 그대로 영어 폰트로 남았다.
        /// </summary>
        static void ApplyToAllScenes(ref int textCount, ref int sceneCount)
        {
            // 작업 중인 씬을 날리지 않도록, 실제로 수정된 씬이 있을 때만 물어본다.
            bool anyDirty = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) { anyDirty = true; break; }
            if (anyDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string resume = SceneManager.GetActiveScene().path;

            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { SceneFolder }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));

            // 씬 폴더가 비어 있으면 최소한 지금 열린 씬이라도 처리한다.
            if (paths.Count == 0)
            {
                textCount += ApplyToOpenScenes();
                sceneCount += 1;
                return;
            }

            foreach (var path in paths)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int changed = ApplyToOpenScenes();
                if (changed > 0) EditorSceneManager.SaveScene(scene);
                textCount += changed;
                sceneCount++;
            }

            if (!string.IsNullOrEmpty(resume) && paths.Contains(resume))
                EditorSceneManager.OpenScene(resume, OpenSceneMode.Single);
        }

        static int ApplyToOpenScenes()
        {
            var font = Font;
            if (font == null) return 0;

            int count = 0;
            foreach (var text in Object.FindObjectsByType<TMP_Text>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (text.font == font) continue;
                Undo.RecordObject(text, "Apply Korean Font");
                text.font = font;
                EditorUtility.SetDirty(text);
                count++;
            }
            if (count > 0)
                EditorSceneManager.MarkAllScenesDirty();
            return count;
        }

        static int ApplyToPrefabs()
        {
            var font = Font;
            if (font == null) return 0;

            int count = 0;
            var folders = new List<string>();
            if (AssetDatabase.IsValidFolder(PrefabGenerator.PrefabFolder))
                folders.Add(PrefabGenerator.PrefabFolder);
            if (folders.Count == 0) return 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", folders.ToArray()))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;

                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.font == font) continue;
                    text.font = font;
                    changed = true;
                    count++;
                }

                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            return count;
        }

        // ══════════════════════════════════════════════════════════
        //  TMP 설정
        // ══════════════════════════════════════════════════════════
        /// <summary>
        /// TMP의 Default Font Asset을 이 폰트로 바꾼다.
        /// 앞으로 새로 만드는 모든 TMP 텍스트가 한글 폰트로 시작한다.
        /// </summary>
        static bool TrySetTmpDefaultFont()
        {
            var settings = TMP_Settings.instance;
            if (settings == null || Font == null) return false;

            var so = new SerializedObject(settings);
            var prop = so.FindProperty("m_defaultFontAsset");
            if (prop == null) return false;

            prop.objectReferenceValue = Font;

            // TMP는 Resources 기준 상대 경로도 함께 들고 있다. 있으면 같이 맞춰준다.
            var pathProp = so.FindProperty("m_defaultFontAssetPath");
            if (pathProp != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(Font);
                int idx = assetPath.IndexOf("/Resources/", System.StringComparison.Ordinal);
                if (idx >= 0)
                {
                    string rel = assetPath.Substring(idx + "/Resources/".Length);
                    rel = Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? "";
                    pathProp.stringValue = string.IsNullOrEmpty(rel) ? "" : rel + "/";
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);
            return true;
        }

        /// <summary>
        /// TMP 전역 폴백 목록에 한글 폰트를 넣는다.
        /// 어떤 이유로든 영어 폰트가 남아 있는 텍스트도 한글만은 이 폰트로 그려진다 — 안전망이다.
        /// </summary>
        static bool TryAddGlobalFallback()
        {
            var settings = TMP_Settings.instance;
            if (settings == null || Font == null) return false;

            var so = new SerializedObject(settings);
            var list = so.FindProperty("m_fallbackFontAssets");
            if (list == null || !list.isArray) return false;

            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == Font) return true;

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = Font;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);
            return true;
        }
    }
}
#endif
