using System;
using System.Collections.Generic;
using FishGame.Core;
using FishGame.Data;
using FishGame.Player;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 한 판(run)의 진행을 관리한다. Gameplay 씬에 1개.
    ///
    /// 맵이 넷에서 하나로 합쳐지면서 이 클래스의 역할도 바뀌었다.
    /// 예전: "이 맵에서 보스를 잡아 다음 맵을 연다"
    /// 지금: "위에서 아래로 내려간다. 먹어서 커지면 통로가 열리고, 깊을수록 값이 오른다"
    ///
    /// 한 판의 목표는 '이번엔 어디까지 내려가느냐'다.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class RunManager : MonoBehaviour
    {
        public static RunManager Instance { get; private set; }

        [Header("씬 참조")]
        [SerializeField] PlayerFish player;
        [SerializeField] FishSpawner spawner;
        [SerializeField] WorldBuilder worldBuilder;
        [Tooltip("현재 존의 물색을 카메라 배경에 반영한다. 비워도 동작한다.")]
        [SerializeField] Camera worldCamera;

        [Header("에디터 단독 실행용 (GameManager 없이 테스트)")]
        [SerializeField] GameDatabase fallbackDatabase;
        [SerializeField] int fallbackStartZone = 0;

        // ── 상태 ────────────────────────────────────────────────
        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public float TimeRemaining { get; private set; }
        public float MaxTime { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public double CurrencyEarned { get; private set; }
        public int FishEaten { get; private set; }
        public bool BossKilled { get; private set; }

        public PlayerStats Stats { get; private set; }
        public GameDatabase Database { get; private set; }
        public PlayerFish Player => player;
        public WorldLayout Layout { get; private set; }

        // ── 존 ──────────────────────────────────────────────────
        public int StartZoneIndex { get; private set; }
        public int CurrentZoneIndex { get; private set; }
        public int DeepestZoneThisRun { get; private set; }
        public ZoneData CurrentZone =>
            Layout != null ? Layout.GetZone(CurrentZoneIndex) : null;

        public int HiddenItemsThisRun { get; private set; }
        public bool BossReleased { get; private set; }

        // ── 이벤트 ──────────────────────────────────────────────
        public event Action<float, double> OnFishEaten;      // (회복 시간, 획득 재화)
        public event Action<RunEndReason> OnRunEnded;
        public event Action<bool> OnPauseChanged;
        public event Action<int> OnZoneChanged;              // 새 존 인덱스
        public event Action<ZoneGate> OnGateOpened;
        public event Action<int> OnHiddenItemFound;          // 이번 판 누적 개수
        public event Action OnBossReleased;

        PlayerProgress _progress;
        readonly List<ZoneGate> _gates = new List<ZoneGate>();

        /// <summary>보스가 풀려난 직후 움직이지 않는 시간. 어디서 나왔는지 보고 거리를 잴 틈.</summary>
        const float BossReleaseGraceSeconds = 1.5f;

        // ══════════════════════════════════════════════════════════
        //  초기화
        // ══════════════════════════════════════════════════════════
        void Awake()
        {
            Instance = this;

            var gm = GameManager.Instance;
            if (gm != null && gm.Database != null)
            {
                Database = gm.Database;
                StartZoneIndex = gm.SelectedStartZone;
                Stats = gm.Stats ?? PlayerStats.Build(Database, gm.Progress);
                _progress = gm.Progress;
            }
            else
            {
                Debug.LogWarning("[RunManager] GameManager가 없어 fallback 데이터로 실행합니다. (에디터 단독 테스트 모드)");

                Database = fallbackDatabase != null
                    ? fallbackDatabase
                    : Resources.Load<GameDatabase>(GameManager.DatabaseResourceName);

                StartZoneIndex = fallbackStartZone;
                _progress = new PlayerProgress();
                Stats = Database != null ? PlayerStats.Build(Database, _progress) : new PlayerStats();
            }

            if (Database == null)
                Debug.LogError("[RunManager] GameDatabase가 없습니다. [FishGame ▸ 1. 콘텐츠 에셋 생성]을 실행하세요.");
            else if (Database.ZoneCount == 0)
                Debug.LogError("[RunManager] GameDatabase에 존이 하나도 없습니다.");

            if (worldCamera == null) worldCamera = Camera.main;
        }

        void Start() => BeginRun();

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Bait.DespawnAll();
        }

        // ══════════════════════════════════════════════════════════
        //  판 시작
        // ══════════════════════════════════════════════════════════
        public void BeginRun()
        {
            if (Database == null || Database.ZoneCount == 0) return;

            MaxTime = Stats.MaxSurvivalTime;
            TimeRemaining = MaxTime;
            ElapsedSeconds = 0f;
            CurrencyEarned = 0d;
            FishEaten = 0;
            HiddenItemsThisRun = 0;
            BossKilled = false;
            BossReleased = false;
            IsPaused = false;

            Bait.DespawnAll();

            var bank = AudioManager.Instance.Bank;
            AudioManager.PlayMusic(bank != null ? bank.gameplayMusic : null);

            StartZoneIndex = Mathf.Clamp(StartZoneIndex, 0, Database.ZoneCount - 1);
            CurrentZoneIndex = StartZoneIndex;
            DeepestZoneThisRun = StartZoneIndex;

            BuildWorld();

            if (player != null)
            {
                Vector2 start = Layout.SpawnPointForZone(StartZoneIndex);
                var prb = player.GetComponent<Rigidbody2D>();
                if (prb != null) { prb.position = start; prb.linearVelocity = Vector2.zero; }
                player.transform.position = start;
                player.Initialize(Stats, Database, this);
            }

            // 카메라를 시작 크기에 맞게 즉시 맞춘다. 안 그러면 첫 스폰이 줌아웃 전의
            // 좁은 화면 기준으로 이뤄져서, 깊은 구역에서 시작할 때 물고기가 플레이어 바로 옆에 몰린다.
            var follow = worldCamera != null ? worldCamera.GetComponent<CameraFollow>() : null;
            if (follow != null) follow.SnapToTarget();

            ApplyZoneVisuals(CurrentZoneIndex);

            if (spawner != null) spawner.BeginSpawning(this);

            IsRunning = true;
            OnZoneChanged?.Invoke(CurrentZoneIndex);
        }

        void BuildWorld()
        {
            if (worldBuilder == null)
            {
                Debug.LogError("[RunManager] WorldBuilder가 연결되지 않았습니다. " +
                               "Gameplay 씬을 다시 생성하거나 인스펙터에 World 오브젝트를 넣으세요.");
                Layout = WorldLayout.Build(Database.zones,
                                           Database.depthRichness, Database.globalValueScale);
                return;
            }

            var collected = _progress != null ? _progress.HiddenItemSet : new HashSet<string>();
            Layout = worldBuilder.Build(Database.zones, collected,
                                        Database.depthRichness, Database.globalValueScale);

            _gates.Clear();
            foreach (var gate in worldBuilder.Gates)
            {
                if (gate == null) continue;
                gate.Relock();
                gate.OnOpened += HandleGateOpened;
                _gates.Add(gate);
            }
        }

        void HandleGateOpened(ZoneGate gate)
        {
            if (gate == null) return;
            OnGateOpened?.Invoke(gate);
        }

        // ══════════════════════════════════════════════════════════
        //  월드 질의 — 예전 MapBounds를 대신한다
        // ══════════════════════════════════════════════════════════
        public float WorldTopY => Layout != null ? Layout.TopY : 0f;
        public float WorldBottomY => Layout != null ? Layout.BottomY : -40f;
        public float WorldHalfWidthAt(float y) => Layout != null ? Layout.HalfWidthAt(y) : 20f;
        public float WorldCenterXAt(float y) => Layout != null ? Layout.CenterXAt(y) : 0f;

        public Vector2 ClampToWorld(Vector2 p, float radius) =>
            Layout != null ? Layout.Clamp(p, radius) : p;

        public bool InsideWorld(Vector2 p, float radius) =>
            Layout == null || Layout.Contains(p, radius);

        /// <summary>HUD 깊이 게이지용 0~1.</summary>
        public float Depth01 =>
            Layout != null && player != null ? Layout.GlobalDepth01(player.transform.position.y) : 0f;

        // ══════════════════════════════════════════════════════════
        //  통로 판정에 쓰는 크기
        // ══════════════════════════════════════════════════════════
        /// <summary>
        /// 통로가 열리는지 볼 때 쓰는 크기.
        /// 기본값은 "판 중에 먹어서 커진 현재 크기"다 — 한 판의 목표가 되라고 이렇게 뒀다.
        /// </summary>
        public float CurrentPlayerSize
        {
            get
            {
                if (Database != null && !Database.gateUsesCurrentSize)
                    return Stats != null ? Stats.Size : 0f;
                return player != null ? player.Size : 0f;
            }
        }

        /// <summary>현재 존의 통로를 열기 위해 필요한 크기. 통로가 없으면 0.</summary>
        public float NextGateRequiredSize =>
            Layout != null ? Layout.GateRequiredSize(CurrentZoneIndex) : 0f;

        public bool NextGateIsOpen
        {
            get
            {
                foreach (var g in _gates)
                    if (g != null && g.ZoneIndex == CurrentZoneIndex) return g.IsOpen;
                return false;
            }
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
        /// 존이 커질수록 카메라도 넓어지므로, 이걸 기준으로 스폰해야 밀도가 일정하다.
        /// </summary>
        public float ViewRadius
        {
            get
            {
                var cam = worldCamera != null ? worldCamera : Camera.main;
                if (cam == null || !cam.orthographic)
                    return Mathf.Max(8f, (Stats != null ? Stats.Size : 1f) * 10f);
                float halfH = cam.orthographicSize;
                float halfW = halfH * cam.aspect;
                return Mathf.Sqrt(halfW * halfW + halfH * halfH);
            }
        }

        // ══════════════════════════════════════════════════════════
        //  루프
        // ══════════════════════════════════════════════════════════
        void Update()
        {
            if (!IsRunning || IsPaused) return;

            float dt = Time.deltaTime;
            ElapsedSeconds += dt;
            if (!FishGame.Utils.DevFlags.InfiniteTime)
                TimeRemaining -= dt * CurrentDrainMultiplier;

            TrackZone();

            if (TimeRemaining <= 0f)
            {
                TimeRemaining = 0f;
                EndRun(RunEndReason.TimeOut);
            }
        }

        /// <summary>플레이어가 어느 존에 있는지 추적하고, 바뀌면 연출과 스폰을 갈아 끼운다.</summary>
        void TrackZone()
        {
            if (Layout == null || player == null) return;

            int zone = Layout.ZoneIndexAt(player.transform.position.y);
            if (zone == CurrentZoneIndex) return;

            CurrentZoneIndex = zone;
            if (zone > DeepestZoneThisRun) DeepestZoneThisRun = zone;

            ApplyZoneVisuals(zone);
            OnZoneChanged?.Invoke(zone);
        }

        void ApplyZoneVisuals(int zoneIndex)
        {
            var zone = Layout != null ? Layout.GetZone(zoneIndex) : null;
            if (zone == null) return;

            var cam = worldCamera != null ? worldCamera : Camera.main;
            if (cam != null) cam.backgroundColor = zone.waterColor * 0.45f;
        }

        // ══════════════════════════════════════════════════════════
        //  포식 보고
        // ══════════════════════════════════════════════════════════
        /// <summary>
        /// 물고기를 먹었을 때 PlayerFish가 호출하는 단일 진입점.
        /// 시간·재화 계산과 도감 누적을 여기서 한다.
        /// </summary>
        public void ReportFishEaten(FishSpecies species, bool wasBoss, int preyZoneIndex = -1)
        {
            if (!IsRunning) return;

            float baseTime = species != null ? species.timeReward : 1f;
            double baseMoney = species != null ? species.currencyReward : 1d;

            float timeGain = baseTime * Stats.TimeGainMultiplier;

            // 도감 누적 — 보너스 자체는 다음 판의 PlayerStats에 반영된다
            if (species != null && _progress != null) _progress.AddCodexCount(species.CodexKey);

            // 깊이가 값을 정한다. 존 배율 × 존 안에서의 깊이 보너스.
            float depthMult = Layout != null && player != null
                ? Layout.ValueMultiplierAt(player.transform.position.y, preyZoneIndex)
                : 1f;

            double gained = baseMoney * Stats.CurrencyMultiplier * depthMult;

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

        /// <summary>히든 아이템을 먹었을 때 HiddenItem이 호출.</summary>
        public void ReportHiddenItem(HiddenItem item)
        {
            if (item == null || _progress == null || !IsRunning) return;
            if (!_progress.AddHiddenItem(item.ItemId)) return;

            HiddenItemsThisRun++;
            OnHiddenItemFound?.Invoke(HiddenItemsThisRun);

            // 영구 보너스라 즉시 저장한다 — 이건 죽어도 잃으면 안 된다
            GameManager.Instance?.SaveNow();
        }

        /// <summary>바다의 구조물이 부서졌을 때 BossStructure가 호출.</summary>
        public void ReleaseBoss(BossStructure structure)
        {
            if (!IsRunning || BossReleased || structure == null) return;

            var zone = Layout != null ? Layout.GetZone(structure.ZoneIndex) : null;
            var boss = structure.Boss != null ? structure.Boss : zone?.boss;
            if (boss == null) return;

            BossReleased = true;

            // 구조물은 부스터로 들이받을 때 깨지므로 플레이어는 늘 구조물 한가운데 가까이 있다.
            // 예전엔 보스를 구조물 중심에 바로 만들어서, 다음 물리 프레임에 즉사하거나(작을 때)
            // 그 자리에서 보스를 먹어버렸다(클 때) — 추격전이 아예 없었다.
            // 플레이어 반대편으로 충분히 떨어뜨려 만들고, 잠깐 마비시켜 숨 돌릴 틈을 준다.
            player?.CancelBooster();

            Vector2 center = structure.transform.position;
            Vector2 playerPos = player != null ? (Vector2)player.transform.position : center + Vector2.up;
            Vector2 away = center - playerPos;
            if (away.sqrMagnitude < 0.01f) away = Vector2.down;
            away.Normalize();

            float playerR = player != null ? player.Size * 0.5f : 1f;
            float bossR = boss.size * 0.5f;
            float gap = bossR + playerR + Mathf.Max(bossR, 8f);
            Vector2 spawnPos = Layout != null ? Layout.Clamp(playerPos + away * gap, bossR) : playerPos + away * gap;

            var bossFish = spawner != null ? spawner.SpawnBoss(boss, spawnPos) : null;
            if (bossFish != null && bossFish.Body != null) bossFish.Body.Stun(BossReleaseGraceSeconds);

            OnBossReleased?.Invoke();
        }

        // ══════════════════════════════════════════════════════════
        //  일시정지 / 종료
        // ══════════════════════════════════════════════════════════
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

            // 일시정지 중에 끝났다면(개발자 모드 "판 끝내기" 등) 일시정지 상태를 풀어 준다
            if (IsPaused)
            {
                IsPaused = false;
                OnPauseChanged?.Invoke(false);
            }

            // 먹혔을 때의 히트스톱이 진행 중이면 끊지 않는다 — 끝나면 Juice가 1로 되돌린다
            if (!Juice.IsHitStopping) Time.timeScale = 1f;

            spawner?.StopSpawning();
            Bait.DespawnAll();

            // 순서 중요: 결과 UI가 GameManager.LastResult를 읽으므로 정산을 먼저 끝낸다.
            GameManager.Instance?.FinishRun(reason, CurrencyEarned, FishEaten, ElapsedSeconds,
                                            BossKilled, DeepestZoneThisRun, HiddenItemsThisRun);
            OnRunEnded?.Invoke(reason);
        }

        public void ReturnToMainMenu()
        {
            Time.timeScale = 1f;
            if (GameManager.Instance != null) GameManager.Instance.GoToMainMenu();
            else Debug.LogWarning("[RunManager] GameManager가 없어 씬 이동을 건너뜁니다.");
        }

        // ══════════════════════════════════════════════════════════
        //  개발자 모드용 — DevConsole만 부른다
        // ══════════════════════════════════════════════════════════
        /// <summary>제한시간을 가득 채운다.</summary>
        public void DevRefillTime()
        {
            if (!IsRunning) return;
            TimeRemaining = MaxTime;
        }

        /// <summary>이번 판의 통로를 전부 연다.</summary>
        public void DevOpenAllGates()
        {
            foreach (var g in _gates)
                if (g != null && !g.IsOpen) g.Open();
        }

        /// <summary>해당 구역의 시작 위치로 순간이동. 위쪽 통로도 같이 열어 되돌아갈 길을 남긴다.</summary>
        public void DevTeleportToZone(int zoneIndex)
        {
            if (!IsRunning || Layout == null || player == null) return;
            zoneIndex = Mathf.Clamp(zoneIndex, 0, Layout.ZoneCount - 1);

            foreach (var g in _gates)
                if (g != null && g.ZoneIndex < zoneIndex && !g.IsOpen) g.Open();

            Vector2 pos = Layout.SpawnPointForZone(zoneIndex);
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) { rb.position = pos; rb.linearVelocity = Vector2.zero; }
            player.transform.position = pos;
        }

        /// <summary>보스 구조물 바로 위로 옮긴 뒤 구조물을 깬다.</summary>
        public void DevReleaseBossNow()
        {
            if (!IsRunning || worldBuilder == null || worldBuilder.Boss == null) return;
            var bs = worldBuilder.Boss;

            DevTeleportToZone(bs.ZoneIndex);
            Vector2 near = (Vector2)bs.transform.position + Vector2.up * 60f;
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) rb.position = near;
            player.transform.position = near;

            if (!bs.IsBroken) bs.Break();
        }

        public void RestartRun()
        {
            Time.timeScale = 1f;
            if (GameManager.Instance != null) GameManager.Instance.StartRun();
            else BeginRun();
        }
    }
}
