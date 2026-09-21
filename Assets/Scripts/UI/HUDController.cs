using FishGame.Gameplay;
using FishGame.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>플레이 중 HUD.</summary>
    public class HUDController : MonoBehaviour
    {
        [Header("상단 정보")]
        [SerializeField] TMP_Text timeText;
        [SerializeField] TMP_Text currencyText;
        [SerializeField] TMP_Text sizeText;
        [Tooltip("현재 구역 이름")]
        [SerializeField] TMP_Text mapNameText;
        [Tooltip("수면에서 얼마나 내려왔는지")]
        [SerializeField] TMP_Text depthText;
        [Tooltip("통합 맵 전체 대비 깊이 게이지")]
        [SerializeField] Image depthBar;
        [SerializeField] Image timeBar;
        [SerializeField] Gradient timeBarGradient;
        [Tooltip("제한시간 소모 가속 표시 (x1.0 → x2.4 ...)")]
        [SerializeField] TMP_Text drainText;
        [Tooltip("이번 판에서 얼마나 자랐는지 보여주는 바")]
        [SerializeField] Image growthBar;

        [Header("액티브 스킬 슬롯")]
        [SerializeField] GameObject boosterSlot;
        [SerializeField] Image boosterFill;
        [SerializeField] GameObject vacuumSlot;
        [SerializeField] Image vacuumFill;
        [SerializeField] GameObject baitSlot;
        [SerializeField] Image baitFill;
        [SerializeField] GameObject voltSlot;
        [SerializeField] Image voltFill;
        [SerializeField] GameObject missileSlot;
        [SerializeField] Image missileFill;

        [Header("비늘 경화")]
        [SerializeField] GameObject armorGroup;
        [SerializeField] TMP_Text armorText;

        [Header("보스 안내")]
        [SerializeField] GameObject bossBanner;
        [SerializeField] TMP_Text bossHintText;

        [Header("위급 연출")]
        [Tooltip("제한시간이 얼마 안 남으면 붉게 맥동하는 화면 가장자리")]
        [SerializeField] Image dangerVignette;
        [Tooltip("이 시간 이하로 남으면 위급 연출 시작 (초)")]
        [SerializeField] float dangerThreshold = 6f;
        [SerializeField] float dangerMaxAlpha = 0.42f;

        [Header("획득 팝업")]
        [SerializeField] FloatingTextSpawner floatingText;

        RunManager _run;
        FishGame.Player.PlayerFish _player;

        void Start()
        {
            _run = RunManager.Instance;
            if (_run == null) { enabled = false; return; }

            _player = _run.Player;

            _run.OnFishEaten += HandleFishEaten;
            _run.OnBossReleased += HandleBossReleased;
            _run.OnZoneChanged += HandleZoneChanged;
            _run.OnGateOpened += HandleGateOpened;
            _run.OnHiddenItemFound += HandleHiddenItemFound;

            var s = _run.Stats;
            SetActive(boosterSlot, s.HasBooster);
            SetActive(vacuumSlot,  s.HasVacuum);
            SetActive(baitSlot,    s.HasGoldenBait);
            SetActive(voltSlot,    s.HasVolt);
            SetActive(missileSlot, s.HasMissile);
            SetActive(armorGroup,  s.HasScaleArmor);

            if (bossBanner != null) bossBanner.SetActive(false);

            HandleZoneChanged(_run.CurrentZoneIndex);
        }

        void OnDestroy()
        {
            FishGame.Core.AudioManager.StopLoop();
            if (_run == null) return;
            _run.OnFishEaten -= HandleFishEaten;
            _run.OnBossReleased -= HandleBossReleased;
            _run.OnZoneChanged -= HandleZoneChanged;
            _run.OnGateOpened -= HandleGateOpened;
            _run.OnHiddenItemFound -= HandleHiddenItemFound;
        }

        void Update()
        {
            if (_run == null) return;

            if (timeText != null)
                timeText.text = $"제한시간 {NumberFormatter.FormatCountdown(_run.TimeRemaining)}";

            if (currencyText != null)
                currencyText.text = $"재화 {NumberFormatter.Format(_run.CurrencyEarned)}";

            if (_player != null)
            {
                if (sizeText != null)
                    sizeText.text = _player.MaxRunSize > _player.RunStartSize + 0.01f
                        ? $"크기 {_player.Size:0.00} <size=70%>/ {_player.MaxRunSize:0.0}</size>"
                        : $"크기 {_player.Size:0.00}";

                if (growthBar != null) growthBar.fillAmount = _player.GrowthProgress01;
            }

            if (drainText != null)
            {
                float m = _run.CurrentDrainMultiplier;
                drainText.gameObject.SetActive(m > 1.05f);
                drainText.text = $"소모 x{m:0.0}";
            }

            if (timeBar != null && _run.MaxTime > 0f)
            {
                float t = Mathf.Clamp01(_run.TimeRemaining / _run.MaxTime);
                timeBar.fillAmount = t;
                if (timeBarGradient != null) timeBar.color = timeBarGradient.Evaluate(t);
            }

            if (_player != null)
            {
                // fillAmount는 "남은 쿨타임" 어둡게 덮는 방식
                SetFill(boosterFill, _player.BoosterCooldownNormalized);
                SetFill(vacuumFill,  _player.IsVacuuming ? 0f : 1f);
                SetFill(baitFill,    _player.BaitCooldownNormalized);
                SetFill(voltFill,    _player.VoltCooldownNormalized);
                SetFill(missileFill, _player.MissileCooldownNormalized);

                if (armorText != null && _player.ArmorMax > 0)
                    armorText.text = $"비늘 {_player.ArmorRemaining}/{_player.ArmorMax}";
            }

            if (depthText != null && _run.Layout != null && _player != null)
            {
                // 깊이와 함께 "지금 여기서 먹으면 몇 배인지"를 같이 보여준다.
                // 아래로 내려갈 이유를 숫자로 드러내는 게 이 게임의 핵심이라서.
                float y = _player.transform.position.y;
                float depth = _run.WorldTopY - y;
                float mult = _run.Layout.DepthValueDisplay(y);
                depthText.text = $"깊이 {depth:0}m  ×{mult:0.0}";
            }
            if (depthBar != null) depthBar.fillAmount = _run.Depth01;

            UpdateDanger();
            UpdateGateHint();
        }

        /// <summary>제한시간이 임박하면 화면 가장자리를 붉게 맥동시키고 심박음을 올린다.</summary>
        void UpdateDanger()
        {
            if (!_run.IsRunning || _run.TimeRemaining > dangerThreshold)
            {
                if (dangerVignette != null && dangerVignette.color.a > 0f)
                    SetVignetteAlpha(0f);
                FishGame.Core.AudioManager.StopLoop();
                return;
            }

            // 남은 시간이 적을수록 진하고 빠르게 뛴다
            float urgency = 1f - Mathf.Clamp01(_run.TimeRemaining / Mathf.Max(0.01f, dangerThreshold));
            float pulseHz = Mathf.Lerp(1.6f, 4.2f, urgency);
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * pulseHz * Mathf.PI * 2f);

            SetVignetteAlpha(dangerMaxAlpha * urgency * pulse);

            var bank = FishGame.Core.AudioManager.Instance.Bank;
            if (bank?.heartbeat != null)
                FishGame.Core.AudioManager.SetLoop(bank.heartbeat, 0.35f + 0.45f * urgency,
                                                   Mathf.Lerp(0.9f, 1.35f, urgency));
        }

        void SetVignetteAlpha(float a)
        {
            if (dangerVignette == null) return;
            var c = dangerVignette.color;
            c.a = a;
            dangerVignette.color = c;
            if (dangerVignette.gameObject.activeSelf != (a > 0.001f))
                dangerVignette.gameObject.SetActive(a > 0.001f);
        }

        static void SetActive(GameObject go, bool value) { if (go != null) go.SetActive(value); }
        static void SetFill(Image img, float ready01) { if (img != null) img.fillAmount = 1f - ready01; }

        /// <summary>
        /// 아래 통로까지 얼마나 남았는지. 한 판의 목표가 '더 깊이'이므로
        /// 이 한 줄이 HUD에서 제일 중요하다.
        /// </summary>
        void UpdateGateHint()
        {
            if (bossHintText == null || _run == null || _run.Layout == null || !_run.IsRunning) return;

            // 최하단(바다)에서는 보스 구조물 안내로 바뀐다
            if (!_run.Layout.HasGate(_run.CurrentZoneIndex))
            {
                var zone = _run.CurrentZone;
                bool hasBoss = zone != null && zone.boss != null;
                bossHintText.gameObject.SetActive(hasBoss);
                if (hasBoss)
                    bossHintText.text = _run.BossReleased
                        ? "보스가 풀려났다!"
                        : "고리 구조물에 부스터로 돌진 → 보스 등장";
                return;
            }

            bossHintText.gameObject.SetActive(true);

            float need = _run.NextGateRequiredSize;
            float now = _run.CurrentPlayerSize;

            if (_run.NextGateIsOpen)
            {
                var next = _run.Layout.GetZone(_run.CurrentZoneIndex + 1);
                bossHintText.text = $"통로 개방 — 아래로 내려가면 {(next != null ? next.displayName : "다음 구역")}";
            }
            else
            {
                bossHintText.text = $"아래 통로: 크기 {now:0.0} / {need:0.0}";
            }
        }

        void HandleFishEaten(float timeGain, double currency)
        {
            if (floatingText == null || _player == null) return;
            floatingText.Spawn(_player.transform.position,
                $"+{NumberFormatter.Format(currency)}  +{timeGain:0.0}s");
        }

        void HandleBossReleased()
        {
            FishGame.Gameplay.Juice.Shake(0.5f);
            var bank = FishGame.Core.AudioManager.Instance.Bank;
            if (bank?.bossAppear != null) FishGame.Core.AudioManager.Play(bank.bossAppear);
            ShowBanner("보스가 풀려났다");
            UpdateGateHint();
        }

        void HandleZoneChanged(int zoneIndex)
        {
            var zone = _run != null ? _run.Layout?.GetZone(zoneIndex) : null;
            if (mapNameText != null && zone != null) mapNameText.text = zone.displayName;
            UpdateGateHint();
        }

        void HandleGateOpened(FishGame.Gameplay.ZoneGate gate)
        {
            if (gate == null) return;
            if (gate.ZoneIndex != _run.CurrentZoneIndex) return;
            ShowBanner($"통로 개방 — {gate.ToZoneName}");
            UpdateGateHint();
        }

        void HandleHiddenItemFound(int countThisRun)
        {
            if (floatingText != null && _player != null)
                floatingText.Spawn(_player.transform.position, "히든 아이템!  재화 +5% 영구");
            ShowBanner("히든 아이템 발견 — 재화 획득 +5% (영구)");
        }

        void ShowBanner(string message)
        {
            if (bossBanner == null) return;
            var label = bossBanner.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = message;
            StopCoroutine(nameof(ShowBannerRoutine));
            StartCoroutine(nameof(ShowBannerRoutine));
        }

        System.Collections.IEnumerator ShowBannerRoutine()
        {
            bossBanner.SetActive(true);
            yield return new WaitForSeconds(2.5f);
            bossBanner.SetActive(false);
        }
    }
}
