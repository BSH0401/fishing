using FishGame.Core;
using FishGame.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>
    /// 메인 화면. 스킬트리 + 맵 선택 + 게임 시작.
    /// 기획서의 플레이 루프 1단계.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("버튼")]
        [SerializeField] Button playButton;
        [SerializeField] Button quitButton;
        [SerializeField] Button resetSaveButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button titleButton;

        [Header("표시")]
        [SerializeField] TMP_Text currencyText;
        [SerializeField] TMP_Text statsText;
        [SerializeField] TMP_Text loadoutText;

        [Header("확인 팝업")]
        [SerializeField] GameObject resetConfirmPanel;
        [SerializeField] Button resetConfirmYes;
        [SerializeField] Button resetConfirmNo;

        GameManager _game;

        void Start()
        {
            _game = GameManager.Instance;
            if (_game == null)
            {
                Debug.LogError("[MainMenuUI] GameManager가 없습니다.");
                return;
            }

            if (playButton != null) playButton.onClick.AddListener(() => _game.StartRun());
            if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
            if (settingsButton != null)
            {
                LabStyle.Button(settingsButton);
                // 누르면 바로 설정 창이 아니라 작은 메뉴(설정 · 타이틀)를 펼친다
                settingsButton.onClick.AddListener(ToggleSettingsMenu);
            }
            if (titleButton != null)
            {
                LabStyle.Button(titleButton);
                titleButton.onClick.AddListener(() => _game.GoToTitle());
            }

            ApplyLabStyle();

            var bank = AudioManager.Instance.Bank;
            AudioManager.PlayMusic(bank != null ? bank.menuMusic : null);

            if (resetConfirmPanel != null) resetConfirmPanel.SetActive(false);
            if (resetSaveButton != null)
                resetSaveButton.onClick.AddListener(() => resetConfirmPanel?.SetActive(true));
            if (resetConfirmNo != null)
                resetConfirmNo.onClick.AddListener(() => resetConfirmPanel?.SetActive(false));
            if (resetConfirmYes != null)
                resetConfirmYes.onClick.AddListener(ResetSave);

            _game.OnProgressChanged += Refresh;
            Refresh();
        }

        /// <summary>
        /// 씬 생성기가 같은 재화 글자를 SkillTreeUI에도 물린다. SkillTreeUI가 "다음 칸 가격"까지 붙여 쓰는데
        /// 여기서 매번 숫자만으로 덮어써 그 정보가 보이지 않았다 — 트리가 맡고 있으면 손대지 않는다.
        /// </summary>
        bool CurrencyOwnedByTree()
        {
            if (currencyText == null) return false;
            var tree = FindAnyObjectByType<SkillTreeUI>();
            return tree != null && tree.CurrencyLabel == currencyText;
        }

        /// <summary>
        /// 씬 생성기가 만든 기본 회색 판·버튼(오른쪽 패널, 탈출 시작, 세이브 초기화, 도감, 확인 창)을
        /// 스킬트리·설정 창과 같은 수중 실험실 스타일로 맞춘다.
        /// </summary>
        void ApplyLabStyle()
        {
            if (playButton != null)
            {
                LabStyle.Button(playButton, primary: true);
                var side = playButton.transform.parent != null ? playButton.transform.parent.GetComponent<Image>() : null;
                if (side != null && side.transform != transform) LabStyle.Panel(side);
            }

            if (resetSaveButton != null)
            {
                // 되돌릴 수 없는 버튼이라 붉은 기운을 남긴다
                var img = resetSaveButton.targetGraphic as Image ?? resetSaveButton.GetComponent<Image>();
                LabStyle.Panel(img, corners: false, fill: new Color(0.20f, 0.07f, 0.09f, 0.92f),
                               border: new Color(1f, 0.45f, 0.42f, 0.55f));
                var t = resetSaveButton.GetComponentInChildren<TMP_Text>();
                if (t != null) t.color = new Color(1f, 0.72f, 0.68f);
            }

            if (resetConfirmPanel != null)
            {
                LabStyle.Panel(resetConfirmPanel.GetComponent<Image>(), fill: new Color(0.03f, 0.10f, 0.13f, 0.97f));
                LabStyle.Button(resetConfirmNo);
                LabStyle.Button(resetConfirmYes);
                var yesLabel = resetConfirmYes != null ? resetConfirmYes.GetComponentInChildren<TMP_Text>() : null;
                if (yesLabel != null) yesLabel.color = new Color(1f, 0.62f, 0.58f);
            }
        }

        // ══════════════════════════════════════════════════════════
        //  설정 버튼 메뉴 — 설정 · 타이틀
        // ══════════════════════════════════════════════════════════
        GameObject _menuBlocker;

        void ToggleSettingsMenu()
        {
            if (_menuBlocker != null) { CloseSettingsMenu(); return; }

            var root = (RectTransform)transform;
            // 메뉴 밖 아무 곳이나 누르면 닫힌다
            var blocker = UIKit.Image(root, "SettingsMenuBlocker", null, new Color(0f, 0f, 0f, 0.001f), raycast: true);
            UIKit.Stretch(blocker.rectTransform);
            var close = blocker.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(CloseSettingsMenu);
            _menuBlocker = blocker.gameObject;

            var panel = UIKit.Image(blocker.transform, "Menu", LabArt.Panel, LabStyle.Fill, raycast: true);
            LabStyle.Panel(panel, corners: true, fill: new Color(0.03f, 0.10f, 0.13f, 0.97f));
            var pr = panel.rectTransform;
            // 설정 버튼 바로 아래, 오른쪽 끝을 맞춘다
            var btnRt = settingsButton != null ? (RectTransform)settingsButton.transform : null;
            Vector2 anchorPos = new Vector2(-36f, -86f);
            if (btnRt != null)
                anchorPos = new Vector2(btnRt.anchoredPosition.x, btnRt.anchoredPosition.y - btnRt.sizeDelta.y - 10f);
            UIKit.Place(pr, new Vector2(1f, 1f), anchorPos, new Vector2(220f, 140f), new Vector2(1f, 1f));

            var open = UIKit.Button(pr, "Settings", "설정", new Vector2(188f, 52f), fontSize: 22f);
            UIKit.Place((RectTransform)open.transform, new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(188f, 52f), new Vector2(0.5f, 0.5f));
            open.onClick.AddListener(() => { CloseSettingsMenu(); SettingsPanel.Open(); });

            var title = UIKit.Button(pr, "Title", "타이틀로", new Vector2(188f, 52f), fontSize: 22f);
            UIKit.Place((RectTransform)title.transform, new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(188f, 52f), new Vector2(0.5f, 0.5f));
            title.onClick.AddListener(() => { CloseSettingsMenu(); _game.GoToTitle(); });

            blocker.transform.SetAsLastSibling();
        }

        void CloseSettingsMenu()
        {
            if (_menuBlocker != null) Destroy(_menuBlocker);
            _menuBlocker = null;
        }

        // ── ESC = 설정 창 ────────────────────────────────────────
        InputAction _escape;

        void OnEnable()
        {
            _escape = new InputAction("OpenSettings", InputActionType.Button);
            _escape.AddBinding("<Keyboard>/escape");
            _escape.AddBinding("<Gamepad>/start");
            _escape.Enable();
        }

        void OnDisable()
        {
            _escape?.Dispose();
            _escape = null;
        }

        void Update()
        {
            if (!UIKit.EscapePressed(_escape)) return;
            // 설정 창이 떠 있으면 그 ESC는 설정 창이 닫는 데 쓴다 (닫자마자 다시 열리지 않게)
            if (SettingsPanel.IsOpen || SettingsPanel.ClosedThisFrame) return;
            // 도감이 열려 있으면 먼저 도감을 닫는다
            var codex = GetComponent<CodexUI>();
            if (codex != null && codex.IsOpen) { codex.Close(); return; }
            CloseSettingsMenu();
            SettingsPanel.Open();
        }

        void OnDestroy()
        {
            if (_game != null) _game.OnProgressChanged -= Refresh;
        }

        void Refresh()
        {
            var p = _game.Progress;
            var s = _game.Stats;

            if (currencyText != null && !CurrencyOwnedByTree())
                currencyText.text = NumberFormatter.Format(p.currency);

            if (statsText != null)
            {
                statsText.text =
                    $"총 플레이  {p.totalRuns}회   찍은 칸  {p.totalNodesPurchased}개\n" +
                    $"총 포식  {p.totalFishEaten}마리   도감  {p.codex.Count}종\n" +
                    $"최고 생존  {NumberFormatter.FormatTime(p.bestSurvivalSeconds)}";
            }

            if (loadoutText != null && s != null)
            {
                string actives = "";
                if (s.HasBooster)    actives += "부스터 ";
                if (s.HasVacuum)     actives += "청소기 ";
                if (s.HasScaleArmor) actives += $"비늘×{s.ArmorStacks} ";
                if (s.HasGoldenBait) actives += $"미끼×{s.BaitCount} ";
                if (s.HasVolt)       actives += "볼트 ";
                if (s.HasMissile)    actives += $"미사일×{s.MissileCount} ";
                if (string.IsNullOrEmpty(actives)) actives = "없음";

                loadoutText.text =
                    $"크기  {s.Size:0.00}\n" +
                    $"제한시간  {s.MaxSurvivalTime:0}초\n" +
                    $"이동속도  {s.MoveSpeed:0.0}\n" +
                    $"액티브  {actives}";
            }
        }

        void ResetSave()
        {
            _game.ResetProgress();
            if (resetConfirmPanel != null) resetConfirmPanel.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
