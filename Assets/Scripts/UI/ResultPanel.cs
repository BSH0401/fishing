using FishGame.Core;
using FishGame.Gameplay;
using FishGame.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>
    /// 결과 화면. 기획서의 세 번째 레벨(플레이 → 결과 → 메인).
    /// Gameplay 씬 안의 오버레이 패널로 동작한다.
    /// </summary>
    public class ResultPanel : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text reasonText;
        [SerializeField] TMP_Text currencyText;
        [SerializeField] TMP_Text penaltyText;
        [SerializeField] TMP_Text statsText;
        [SerializeField] GameObject unlockBanner;
        [SerializeField] TMP_Text unlockText;
        [SerializeField] Button mainMenuButton;
        [SerializeField] Button retryButton;

        void Awake()
        {
            // 이 컴포넌트는 항상 활성인 오브젝트(Canvas 등)에 있어야 한다.
            // 자기 자신을 root로 두고 끄면 Start()가 실행되지 않아 OnRunEnded 구독이 누락되고,
            // 판이 끝나도 결과창이 뜨지 않는다(화면이 멈춘 것처럼 보인다).
            if (root == gameObject)
                Debug.LogError("[ResultPanel] root가 자기 자신입니다. 컴포넌트를 Canvas 같은 " +
                               "항상 켜져 있는 오브젝트로 옮기고, root에는 결과 패널을 넣으세요.");

            if (root != null) root.SetActive(false);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(OnMainMenu);
            if (retryButton != null) retryButton.onClick.AddListener(OnRetry);
        }

        void Start()
        {
            var run = RunManager.Instance;
            if (run != null) run.OnRunEnded += HandleRunEnded;
        }

        void OnDestroy()
        {
            var run = RunManager.Instance;
            if (run != null) run.OnRunEnded -= HandleRunEnded;
        }

        void HandleRunEnded(RunEndReason reason)
        {
            // RunManager.EndRun이 GameManager.FinishRun을 먼저 호출하므로
            // 이 시점의 LastResult는 이번 판의 정산 결과다.
            var gm = GameManager.Instance;
            RunResult result = gm != null ? gm.LastResult : default;

            Show(result, reason);
        }

        public void Show(RunResult result, RunEndReason reason)
        {
            if (root != null) root.SetActive(true);

            if (titleText != null)
            {
                titleText.text = reason switch
                {
                    RunEndReason.BossDefeated => "보스 격파!",
                    RunEndReason.Eaten        => "잡아먹혔다",
                    RunEndReason.TimeOut      => "탈진",
                    _                         => "플레이 종료",
                };
            }

            if (reasonText != null)
            {
                reasonText.text = reason switch
                {
                    RunEndReason.BossDefeated => "심해를 지키던 것을 삼켰다. 탈출이다.",
                    RunEndReason.Eaten        => "더 큰 물고기에게 먹혔다. 재화 일부를 잃었다.",
                    RunEndReason.TimeOut      => "버틸 시간이 다했다. 실험실로 돌아간다.",
                    _                         => "",
                };
            }

            if (currencyText != null)
                currencyText.text = NumberFormatter.Format(result.CurrencyEarned);

            if (penaltyText != null)
            {
                double lost = result.CurrencyRaw - result.CurrencyEarned;
                bool hasLoss = lost > 0.5d;
                penaltyText.gameObject.SetActive(hasLoss);
                if (hasLoss) penaltyText.text = $"사망 손실 -{NumberFormatter.Format(lost)}";
            }

            if (statsText != null)
            {
                var gm = GameManager.Instance;
                var startZone = gm?.Database?.GetZone(result.StartZone);
                var deepZone = gm?.Database?.GetZone(result.DeepestZone);

                string route = startZone != null && deepZone != null
                    ? (result.StartZone == result.DeepestZone
                        ? deepZone.displayName
                        : $"{startZone.displayName} → {deepZone.displayName}")
                    : "-";

                statsText.text =
                    $"도달 구역  {route}\n" +
                    $"잡아먹은 물고기  {result.FishEaten}마리\n" +
                    $"생존 시간  {NumberFormatter.FormatTime(result.SurvivedSeconds)}" +
                    (result.HiddenItemsFound > 0
                        ? $"\n히든 아이템  +{result.HiddenItemsFound}개  <size=80%>(재화 +{result.HiddenItemsFound * (gm?.Database != null ? gm.Database.hiddenItemCurrencyBonus : 0.05f) * 100f:0.#}% 영구)</size>"
                        : "");
            }

            if (unlockBanner != null)
            {
                unlockBanner.SetActive(result.NewZoneReached);
                if (result.NewZoneReached && unlockText != null)
                {
                    var gm = GameManager.Instance;
                    var reached = gm?.Database?.GetZone(result.DeepestZone);
                    bool autoSelected = gm != null && gm.SelectedStartZone == result.DeepestZone;
                    string hint = autoSelected
                        ? "다음 판은 여기서 시작합니다 (메뉴에서 바꿀 수 있음)"
                        : "조금 더 커지면 메뉴에서 이 구역을 시작 구역으로 고를 수 있습니다";
                    unlockText.text = reached != null
                        ? $"새 구역 도달: {reached.displayName}\n<size=75%>{hint}</size>"
                        : "새 구역 도달!";
                }
            }
        }

        void OnMainMenu() => RunManager.Instance?.ReturnToMainMenu();
        void OnRetry() => RunManager.Instance?.RestartRun();
    }
}
