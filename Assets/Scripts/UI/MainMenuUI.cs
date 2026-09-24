using FishGame.Core;
using FishGame.Utils;
using TMPro;
using UnityEngine;
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
