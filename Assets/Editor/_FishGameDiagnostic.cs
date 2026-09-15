#if UNITY_EDITOR
using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 셋업이 안 될 때 원인을 가려내는 진단 스크립트.
/// 네임스페이스도 없고 외부 의존도 없어서, 에디터 어셈블리만 살아 있으면 무조건 컴파일된다.
///
/// 메뉴: Tools ▸ FishGame 진단
/// 이 메뉴조차 안 보이면 → Assembly-CSharp-Editor 자체가 컴파일되지 않는 상태
/// 이 메뉴는 보이는데 FishGame 메뉴가 없으면 → FishGame 에디터 스크립트 쪽 문제
/// </summary>
public static class FishGameDiagnostic
{
    [MenuItem("Tools/FishGame 진단", false, 1)]
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("════════ FishGame 진단 ════════");
        sb.AppendLine($"Unity 버전 : {Application.unityVersion}");
        sb.AppendLine();

        // 1) 런타임 타입이 컴파일됐는가
        sb.AppendLine("── 런타임 스크립트 (Assembly-CSharp) ──");
        CheckType(sb, "FishGame.Data.GameDatabase");
        CheckType(sb, "FishGame.Data.FishSpecies");
        CheckType(sb, "FishGame.Data.SkillNode");
        CheckType(sb, "FishGame.Core.GameManager");
        CheckType(sb, "FishGame.Gameplay.RunManager");
        CheckType(sb, "FishGame.Gameplay.FishSpawner");
        CheckType(sb, "FishGame.Player.PlayerFish");
        CheckType(sb, "FishGame.UI.SkillTreeUI");
        sb.AppendLine();

        // 2) 에디터 타입이 컴파일됐는가
        sb.AppendLine("── 에디터 스크립트 (Assembly-CSharp-Editor) ──");
        CheckType(sb, "FishGame.EditorTools.SetupWizard");
        CheckType(sb, "FishGame.EditorTools.ContentGenerator");
        CheckType(sb, "FishGame.EditorTools.PrefabGenerator");
        CheckType(sb, "FishGame.EditorTools.SceneGenerator");
        CheckType(sb, "FishGame.EditorTools.PlaceholderArt");
        CheckType(sb, "FishGame.EditorTools.ProjectSetupUtility");
        sb.AppendLine();

        // 3) 패키지
        sb.AppendLine("── 패키지 ──");
        CheckType(sb, "UnityEngine.InputSystem.InputAction", "Input System");
        CheckType(sb, "UnityEngine.InputSystem.UI.InputSystemUIInputModule", "Input System UI");
        CheckType(sb, "TMPro.TMP_Text", "TextMeshPro");
        sb.AppendLine();

        // 4) TMP 리소스
        sb.AppendLine("── TMP 리소스 ──");
        var tmpSettings = Type.GetType("TMPro.TMP_Settings, Unity.TextMeshPro")
                          ?? FindType("TMPro.TMP_Settings");
        if (tmpSettings == null)
        {
            sb.AppendLine("  ✗ TMP_Settings 없음");
        }
        else
        {
            var prop = tmpSettings.GetProperty("defaultFontAsset",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var font = prop?.GetValue(null);
            sb.AppendLine(font != null
                ? $"  ✓ 기본 폰트: {font}"
                : "  ✗ 기본 폰트 없음 → Window ▸ TextMeshPro ▸ Import TMP Essential Resources 실행 필요");
        }
        sb.AppendLine();

        // 5) 레이어
        sb.AppendLine("── 레이어 ──");
        sb.AppendLine(LayerMask.NameToLayer("Fish") >= 0
            ? $"  ✓ Fish (슬롯 {LayerMask.NameToLayer("Fish")})" : "  · Fish 없음 (셋업 전이면 정상)");
        sb.AppendLine(LayerMask.NameToLayer("Player") >= 0
            ? $"  ✓ Player (슬롯 {LayerMask.NameToLayer("Player")})" : "  · Player 없음 (셋업 전이면 정상)");
        sb.AppendLine();

        // 6) 생성물
        sb.AppendLine("── 생성된 에셋 ──");
        CheckAsset(sb, "Assets/_Project/ScriptableObjects/GameDatabase.asset");
        CheckAsset(sb, "Assets/_Project/Prefabs/PlayerFish.prefab");
        CheckAsset(sb, "Assets/_Project/Prefabs/AIFish.prefab");
        CheckAsset(sb, "Assets/_Project/Scenes/MainMenu.unity");
        CheckAsset(sb, "Assets/_Project/Scenes/Gameplay.unity");
        sb.AppendLine();

        // 7) FishGame 메뉴 존재 여부
        bool menuExists = Menu.GetEnabled("FishGame/★ 전체 셋업 (원클릭)");
        sb.AppendLine($"── FishGame 메뉴 등록: {(menuExists ? "✓ 있음" : "✗ 없음")}");
        sb.AppendLine("═══════════════════════════════");

        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("FishGame 진단",
            "결과를 Console 창에 출력했습니다.\nConsole의 로그를 클릭해서 전체 내용을 복사해 주세요.", "확인");
    }

    /// <summary>진단이 통과하면 이 버튼으로 바로 셋업을 돌릴 수 있다.</summary>
    [MenuItem("Tools/FishGame 전체 셋업 실행 (백업 경로)", false, 2)]
    public static void RunSetup()
    {
        var wizard = FindType("FishGame.EditorTools.SetupWizard");
        if (wizard == null)
        {
            EditorUtility.DisplayDialog("FishGame",
                "SetupWizard 타입을 찾을 수 없습니다.\n에디터 스크립트가 컴파일되지 않았습니다.\n" +
                "먼저 [Tools ▸ FishGame 진단]을 실행해 주세요.", "확인");
            return;
        }
        wizard.GetMethod("RunAll",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.Invoke(null, null);
    }

    static void CheckType(StringBuilder sb, string fullName, string label = null)
    {
        var t = FindType(fullName);
        sb.AppendLine(t != null
            ? $"  ✓ {label ?? fullName}"
            : $"  ✗ {label ?? fullName}  ← 없음");
    }

    static void CheckAsset(StringBuilder sb, string path)
    {
        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        sb.AppendLine(obj != null ? $"  ✓ {path}" : $"  · {path}  (아직 없음)");
    }

    static Type FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(fullName, false);
            if (t != null) return t;
        }
        return null;
    }
}
#endif
