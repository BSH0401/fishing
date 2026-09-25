#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FishGame.EditorTools
{
    /// <summary>
    /// 0~3단계를 순서대로 한 번에 실행한다.
    /// 메뉴: FishGame ▸ ★ 전체 셋업 (원클릭)
    /// </summary>
    public static class SetupWizard
    {
        [MenuItem("FishGame/★ 전체 셋업 (원클릭)", false, -10)]
        public static void RunAll()
        {
            FontSetup.ClearCache();

            if (TMP_Settings.defaultFontAsset == null && FontSetup.Font == null)
            {
                bool go = EditorUtility.DisplayDialog(
                    "TMP 폰트 없음",
                    "TextMeshPro 폰트를 하나도 찾지 못했습니다.\n\n" +
                    "Window ▸ TextMeshPro ▸ Import TMP Essential Resources 를 먼저 실행하세요.\n\n" +
                    "그래도 계속할까요? UI 텍스트가 비어 보일 수 있습니다.",
                    "계속", "취소");
                if (!go) return;
            }
            else if (FontSetup.Font == null)
            {
                bool go = EditorUtility.DisplayDialog(
                    "한글 폰트 없음",
                    $"한글 TMP 폰트를 찾지 못했습니다.\n기대 경로: {FontSetup.PreferredFontPath}\n\n" +
                    "이대로 진행하면 한글이 □ 로 보입니다. 계속할까요?",
                    "계속", "취소");
                if (!go) return;
            }

            EditorUtility.DisplayProgressBar("FishGame 셋업", "레이어 · 물리 설정", 0.1f);
            ProjectSetupUtility.SetupProject();

            EditorUtility.DisplayProgressBar("FishGame 셋업", "콘텐츠 에셋 생성", 0.35f);
            ContentGenerator.Generate();

            EditorUtility.DisplayProgressBar("FishGame 셋업", "프리팹 생성", 0.65f);
            PrefabGenerator.GenerateAll();

            EditorUtility.DisplayProgressBar("FishGame 셋업", "씬 생성", 0.85f);
            SceneGenerator.GenerateScenes();

            EditorUtility.DisplayProgressBar("FishGame 셋업", "한글 폰트 적용", 0.95f);
            FontSetup.ApplyToEverything(false);
            ArtApplier.Apply();   // 직접 그린 그림 — 프리팹을 새로 만들었으니 다시 꽂는다
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

            EditorUtility.ClearProgressBar();

            EditorUtility.DisplayDialog("FishGame",
                "셋업이 끝났습니다.\n\n" +
                "MainMenu 씬이 열려 있습니다. Play 버튼을 누르면 바로 플레이할 수 있습니다.\n\n" +
                $"폰트: {(FontSetup.Font != null ? FontSetup.Font.name : "기본 (한글 미지원)")}\n\n" +
                "조작: WASD 이동 / Space 대쉬 / Shift 스퍼트 / E 흡입 / ESC 일시정지",
                "확인");
        }

        /// <summary>
        /// 씬을 다시 만들지 않고 GameDatabase 참조만 다시 연결한다.
        /// 씬을 직접 수정했다가 참조가 끊겼을 때 쓴다.
        /// </summary>
        [MenuItem("FishGame/4. 열린 씬의 Database 참조 복구", false, 4)]
        public static void RepairDatabaseReference()
        {
            var db = AssetDatabase.LoadAssetAtPath<FishGame.Data.GameDatabase>(
                         ContentGenerator.DatabasePath)
                     ?? AssetDatabase.LoadAssetAtPath<FishGame.Data.GameDatabase>(
                         "Assets/_Project/ScriptableObjects/GameDatabase.asset");

            if (db == null)
            {
                EditorUtility.DisplayDialog("FishGame",
                    "GameDatabase 에셋이 없습니다.\n먼저 [FishGame ▸ 1. 콘텐츠 에셋 생성]을 실행하세요.", "확인");
                return;
            }

            int fixedCount = 0;

            foreach (var gm in Object.FindObjectsByType<FishGame.Core.GameManager>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(gm);
                var prop = so.FindProperty("database");
                if (prop != null && prop.objectReferenceValue == null)
                {
                    prop.objectReferenceValue = db;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(gm);
                    fixedCount++;
                }
            }

            foreach (var run in Object.FindObjectsByType<FishGame.Gameplay.RunManager>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(run);
                var prop = so.FindProperty("fallbackDatabase");
                if (prop != null && prop.objectReferenceValue == null)
                {
                    prop.objectReferenceValue = db;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(run);
                    fixedCount++;
                }
            }

            if (fixedCount > 0)
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();

            EditorUtility.DisplayDialog("FishGame",
                fixedCount > 0
                    ? $"{fixedCount}개의 참조를 복구했습니다.\nCtrl+S 로 씬을 저장하세요."
                    : "비어 있는 참조가 없습니다. 이미 정상입니다.",
                "확인");
        }

        [MenuItem("FishGame/세이브 파일 삭제", false, 100)]
        public static void DeleteSave()
        {
            FishGame.Core.SaveSystem.DeleteSave();
            Debug.Log("[FishGame] 세이브를 삭제했습니다: " + FishGame.Core.SaveSystem.SavePath);
        }

        [MenuItem("FishGame/세이브 폴더 열기", false, 101)]
        public static void RevealSave()
        {
            EditorUtility.RevealInFinder(Application.persistentDataPath);
        }
    }
}
#endif
