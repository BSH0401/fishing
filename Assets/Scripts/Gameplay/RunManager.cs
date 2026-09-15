using System;
using FishGame.Core;
using FishGame.Data;
using FishGame.Player;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 한 판(run)의 진행을 관리한다. Gameplay 씬에 1개.
    /// 제한시간, 획득 재화, 도감, 보스 게이트, 종료 처리를 담당.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class RunManager : MonoBehaviour
    {
        public static RunManager Instance { get; private set; }

        [Header("씬 참조")]
        [SerializeField] PlayerFish player;
        [SerializeField] FishSpawner spawner;
        [SerializeField] SpriteRenderer backgroundRenderer;
        [SerializeField] Transform bossGateVisual;

        [Header("에디터 단독 실행용 (GameManager 없이 테스트)")]
        [SerializeField] GameDatabase fallbackDatabase;
        [SerializeField] int fallbackMapIndex = 0;

        // ── 상태 ────────────────────────────────────────────────
        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public float TimeRemaining { get; private set; }
        public float MaxTime { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public double CurrencyEarned { get; private set; }
        public int FishEaten { get; private set; }
        public bool BossKilled { get; private set; }

        public MapData Map { get; private set; }
        public Rect MapBounds => Map != null ? Map.WorldBounds : new Rect(-20, -11, 40, 22);
        public PlayerStats Stats { get; private set; }
        public GameDatabase Database { get; private set; }
        public PlayerFish Player => player;

        public bool BossGateOpen { get; private set; }

        public event Action<float, double> OnFishEaten;   // (회복 시간, 획득 재화)
        public event Action<RunEndReason> OnRunEnded;
        public event Action<bool> OnPauseChanged;
        public event Action OnBossGateOpened;

        PlayerProgress _progress;

        void Awake()
        {
            Instance = this;

            var gm = GameManager.Instance;
            if (gm != null && gm.Database != null)
            {
                Database = gm.Database;
                Map = gm.SelectedMap;
                Stats = gm.Stats ?? PlayerStats.Build(Database, gm.Progress);
                _progress = gm.Progress;
            }
            else
            {
                Debug.LogWarning("[RunManager] GameManager가 없어 fallback 데이터로 실행합니다. (에디터 단독 테스트 모드)");

                var fallback = fallbackDatabase != null
                    ? fallbackDatabase
                    : Resources.Load<GameDatabase>(GameManager.DatabaseResourceName);

                Database = fallback;
                Map = fallback != null ? fallback.GetMap(fallbackMapIndex) : null;
                _progress = new PlayerProgress();
                Stats = fallback != null ? PlayerStats.Build(fallback, _progress) : new PlayerStats();
            }

            if (Database == null || Map == null)
                Debug.LogError("[RunManager] Database 또는 Map이 없습니다. GameDatabase와 맵 인덱스를 확인하세요.");
        }

        void Start() => BeginRun();

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Bait.DespawnAll();
        }

        public void BeginRun()
        {
            if (Database == null || Map == null) return;

            MaxTime = Stats.MaxSurvivalTime;
            TimeRemaining = MaxTime;
            ElapsedSeconds = 0f;
            CurrencyEarned = 0d;
            FishEaten = 0;
            BossKilled = false;
            BossGateOpen = false;
            IsPaused = false;

            Bait.DespawnAll();

            if (backgroundRenderer != null)
            {
                if (Map.background != null) backgroundRenderer.sprite = Map.background;
                backgroundRenderer.color = Map.waterColor;
                FitBackgroundToBounds();
            }

            if (bossGateVisual != null)
            {
                bool hasBoss = Map.boss != null;
                bossGateVisual.gameObject.SetActive(hasBoss);
                if (hasBoss) bossGateVisual.position = Map.bossGatePosition;
            }

            if (player != null)
            {
                player.transform.position = Vector3.zero;
                player.Initialize(Stats, Database, this);
            }

            if (spawner != null) spawner.BeginSpawning(Map, this);

            IsRunning = true;
        }

        void FitBackgroundToBounds()
        {
            var sr = backgroundRenderer;
            if (sr == null || sr.sprite == null) return;
            Vector2 spriteSize = sr.sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f) return;
            sr.transform.localScale = new Vector3(
                Map.boundsSize.x / spriteSize.x,
                Map.boundsSize.y / spriteSize.y, 1f);
        }

        /// <summary>현재 초당 제한시간 소모 배수. 오래 버틸수록 커진다.</summary>
        public float CurrentDrainMultiplier
        {
            get
            {
                if (Database == null) return 1f;
                float m = 1f + (ElapsedSeconds / 60f) * Database.timeDrainAccelerationPer60s;
                return Mathf.Min(Database.maxTimeDrainMultiplier, m);
            }
        }

        /// <summary>
        /// 화면에 보이는 대략적인 반경. 스포너가 "화면 밖 가장자리"를 계산할 때 쓴다.
        /// 맵이 커질수록 카메라도 넓어지므로, 이걸 기준으로 스폰해야 밀도가 일정하다.
        /// </summary>
        public float ViewRadius
        {
            get
            {
                var cam = Camera.main;
                if (cam == null || !cam.orthographic)
                    return Mathf.Max(8f, (Stats != null ? Stats.Size : 1f) * 10f);
                float halfH = cam.orthographicSize;
                float halfW = halfH * cam.aspect;
                return Mathf.Sqrt(halfW * halfW + halfH * halfH);
            }
        }

        /// <summary>보스 게이트 판정에 쓰이는 크기.</summary>
        public float GateSize =>
            Database != null && Database.bossGateUsesBaseSize
                ? (Stats != null ? Stats.Size : 0f)
                : (player != null ? player.Size : 0f);

        void Update()
        {
            if (!IsRunning || IsPaused) return;

            float dt = Time.deltaTime;
            ElapsedSeconds += dt;
            TimeRemaining -= dt * CurrentDrainMultiplier;

            if (!BossGateOpen && Map.boss != null && player != null &&
                GateSize >= Map.bossGateMinSize)
            {
                BossGateOpen = true;
                spawner?.SpawnBoss(Map.boss, Map.bossGatePosition);
                OnBossGateOpened?.Invoke();
            }

            if (TimeRemaining <= 0f)
            {
                TimeRemaining = 0f;
                EndRun(RunEndReason.TimeOut);
            }
        }

        // ── 포식 보고 ───────────────────────────────────────────
        /// <summary>
        /// 물고기를 먹었을 때 PlayerFish가 호출하는 단일 진입점.
        /// 시간·재화 계산과 도감 누적을 여기서 한다.
        /// </summary>
        public void ReportFishEaten(FishSpecies species, bool wasBoss)
        {
            if (!IsRunning) return;

            float baseTime = species != null ? species.timeReward : 1f;
            double baseMoney = species != null ? species.currencyReward : 1d;

            float timeGain = baseTime * Stats.TimeGainMultiplier;

            // 도감 누적 — 보너스 자체는 다음 판의 PlayerStats에 반영된다
            if (species != null && _progress != null) _progress.AddCodexCount(species.CodexKey);

            double gained = baseMoney * Stats.CurrencyMultiplier * Map.currencyMultiplier;

            TimeRemaining = Mathf.Min(MaxTime, TimeRemaining + timeGain);
            CurrencyEarned += gained;
            FishEaten++;

            OnFishEaten?.Invoke(timeGain, gained);

            if (wasBoss)
            {
                BossKilled = true;
                EndRun(RunEndReason.BossDefeated);
            }
        }

        // ── 일시정지 / 종료 ─────────────────────────────────────
        public void SetPaused(bool paused)
        {
            if (!IsRunning || IsPaused == paused) return;
            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            OnPauseChanged?.Invoke(paused);
        }

        public void EndRun(RunEndReason reason)
        {
            if (!IsRunning) return;
            IsRunning = false;
            Time.timeScale = 1f;

            spawner?.StopSpawning();
            Bait.DespawnAll();

            // 순서 중요: 결과 UI가 GameManager.LastResult를 읽으므로 정산을 먼저 끝낸다.
            GameManager.Instance?.FinishRun(reason, CurrencyEarned, FishEaten, ElapsedSeconds, BossKilled);
            OnRunEnded?.Invoke(reason);
        }

        public void ReturnToMainMenu()
        {
            Time.timeScale = 1f;
            if (GameManager.Instance != null) GameManager.Instance.GoToMainMenu();
            else Debug.LogWarning("[RunManager] GameManager가 없어 씬 이동을 건너뜁니다.");
        }

        public void RestartRun()
        {
            Time.timeScale = 1f;
            if (GameManager.Instance != null) GameManager.Instance.StartRun();
            else BeginRun();
        }
    }
}
