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
        [SerializeField] TMP_Text mapNameText;
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
            _run.OnBossGateOpened += HandleBossGateOpened;

            var s = _run.Stats;
            SetActive(boosterSlot, s.HasBooster);
            SetActive(vacuumSlot,  s.HasVacuum);
            SetActive(baitSlot,    s.HasGoldenBait);
            SetActive(voltSlot,    s.HasVolt);
            SetActive(missileSlot, s.HasMissile);
            SetActive(armorGroup,  s.HasScaleArmor);

            if (mapNameText != null && _run.Map != null) mapNameText.text = _run.Map.displayName;
            if (bossBanner != null) bossBanner.SetActive(false);

            UpdateBossHint();
        }

        void OnDestroy()
        {
            FishGame.Core.AudioManager.StopLoop();
            if (_run == null) return;
            _run.OnFishEaten -= HandleFishEaten;
            _run.OnBossGateOpened -= HandleBossGateOpened;
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

            UpdateDanger();
            UpdateBossHint();
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

        void UpdateBossHint()
        {
            if (bossHintText == null || _run?.Map == null) return;

            if (_run.Map.boss == null)
            {
                bossHintText.gameObject.SetActive(false);
                return;
            }

            bossHintText.gameObject.SetActive(true);
            bossHintText.text = _run.BossGateOpen
                ? "보스 등장! 길목으로 이동하세요"
                : $"보스 도전 조건: 크기 {_run.Map.bossGateMinSize:0.0} (현재 {_run.GateSize:0.0})";
        }

        void HandleFishEaten(float timeGain, double currency)
        {
            if (floatingText == null || _player == null) return;
            floatingText.Spawn(_player.transform.position,
                $"+{NumberFormatter.Format(currency)}  +{timeGain:0.0}s");
        }

        void HandleBossGateOpened()
        {
            FishGame.Gameplay.Juice.Shake(0.5f);
            var bank = FishGame.Core.AudioManager.Instance.Bank;
            if (bank?.bossAppear != null) FishGame.Core.AudioManager.Play(bank.bossAppear);
            if (bossBanner != null) StartCoroutine(ShowBannerRoutine());
        }

        System.Collections.IEnumerator ShowBannerRoutine()
        {
            bossBanner.SetActive(true);
            yield return new WaitForSeconds(2.5f);
            bossBanner.SetActive(false);
        }
    }
}
