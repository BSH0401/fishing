using FishGame.Core;
using FishGame.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>ESC 일시정지 메뉴.</summary>
    public class PauseMenu : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] Button resumeButton;
        [SerializeField] Button giveUpButton;
        [SerializeField] Button settingsButton;

        InputAction _pause;

        void Awake()
        {
            // ResultPanel과 같은 이유로 항상 활성인 오브젝트에 붙어 있어야 한다.
            if (root == gameObject)
                Debug.LogError("[PauseMenu] root가 자기 자신입니다. 컴포넌트를 Canvas 같은 " +
                               "항상 켜져 있는 오브젝트로 옮기고, root에는 일시정지 패널을 넣으세요.");

            if (root != null) LabStyle.Panel(root.GetComponent<Image>(), fill: new Color(0.03f, 0.10f, 0.13f, 0.95f));
            LabStyle.Button(resumeButton, primary: true);
            LabStyle.Button(settingsButton);
            LabStyle.Button(giveUpButton);

            if (root != null) root.SetActive(false);
            if (resumeButton != null) resumeButton.onClick.AddListener(() => SetPaused(false));
            if (giveUpButton != null) giveUpButton.onClick.AddListener(GiveUp);
            if (settingsButton != null) settingsButton.onClick.AddListener(() => SettingsPanel.Open());

            _pause = new InputAction("Pause", InputActionType.Button);
            _pause.AddBinding("<Keyboard>/escape");
            _pause.AddBinding("<Gamepad>/start");
        }

        void OnEnable() => _pause.Enable();
        void OnDisable() => _pause.Disable();
        void OnDestroy() => _pause.Dispose();

        void Update()
        {
            var run = RunManager.Instance;
            if (run == null || !run.IsRunning)
            {
                // 일시정지 중에 판이 끝났으면(개발자 모드 등) 패널이 결과 화면 위에 남지 않게
                if (root != null && root.activeSelf) root.SetActive(false);
                return;
            }
            // 다른 곳에서 일시정지가 풀렸으면 패널도 닫는다
            if (root != null && root.activeSelf != run.IsPaused) root.SetActive(run.IsPaused);
            // 설정 창이 떠 있으면 ESC는 설정 창이 먼저 쓴다 (닫는 그 ESC로 일시정지까지 풀리지 않게)
            if (SettingsPanel.IsOpen || SettingsPanel.ClosedThisFrame) return;
            if (_pause.WasPressedThisFrame()) SetPaused(!run.IsPaused);
        }

        void SetPaused(bool paused)
        {
            var run = RunManager.Instance;
            if (run == null) return;
            run.SetPaused(paused);
            if (root != null) root.SetActive(paused);
        }

        void GiveUp()
        {
            var run = RunManager.Instance;
            if (run == null) return;
            run.SetPaused(false);
            if (root != null) root.SetActive(false);
            run.EndRun(RunEndReason.Quit);
        }
    }
}
