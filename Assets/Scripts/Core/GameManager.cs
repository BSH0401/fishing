using System;
using FishGame.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishGame.Core
{
    public enum GameState { Boot, MainMenu, Playing, Result }

    /// <summary>플레이 종료 사유.</summary>
    public enum RunEndReason { TimeOut, Eaten, BossDefeated, Quit }

    /// <summary>한 판의 결과 요약.</summary>
    public struct RunResult
    {
        public RunEndReason Reason;
        /// <summary>이번 판을 시작한 구역</summary>
        public int StartZone;
        /// <summary>이번 판에 도달한 가장 깊은 구역</summary>
        public int DeepestZone;
        /// <summary>지금까지 가 본 적 없는 구역에 처음 내려갔는가</summary>
        public bool NewZoneReached;
        public double CurrencyEarned;   // 페널티 적용 후 실제 획득량
        public double CurrencyRaw;      // 페널티 전 원본
        public int FishEaten;
        /// <summary>이번 판에 새로 먹은 히든 아이템 수</summary>
        public int HiddenItemsFound;
        public float SurvivedSeconds;
        public bool BossKilled;
    }

    /// <summary>
    /// 씬을 넘어 살아남는 게임 루트. 상태머신 + 진행도 소유자.
    /// Bootstrap 씬 또는 MainMenu 씬에 1개만 배치한다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>Resources 폴더에서 찾을 때 쓰는 이름 (확장자 없음).</summary>
        public const string DatabaseResourceName = "GameDatabase";

        [Header("데이터")]
        [Tooltip("비워 두면 Resources/GameDatabase 를 자동으로 불러온다.")]
        [SerializeField] GameDatabase database;

        [Header("씬 이름")]
        [Tooltip("타이틀 씬 이름. 빌드 설정에 없으면 '타이틀로'는 메인 화면으로 대신 간다.")]
        [SerializeField] string titleScene = "Title";
        [SerializeField] string mainMenuScene = "MainMenu";
        [SerializeField] string gameplayScene = "Gameplay";

        [Header("디버그")]
        [SerializeField] bool wipeSaveOnStart = false;

        public GameDatabase Database => database;
        public PlayerProgress Progress { get; private set; }
        public PlayerStats Stats { get; private set; }
        public GameState State { get; private set; } = GameState.Boot;

        /// <summary>이번 판을 시작할 구역. 맵은 하나지만 도달해 본 구역부터 시작할 수 있다.</summary>
        public int SelectedStartZone { get; private set; }
        public ZoneData SelectedZone => database != null ? database.GetZone(SelectedStartZone) : null;

        /// <summary>가장 최근 판의 결과 (결과 화면에서 읽음)</summary>
        public RunResult LastResult { get; private set; }

        public event Action<GameState> OnStateChanged;
        public event Action OnProgressChanged;
        public event Action<RunResult> OnRunFinished;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            // 인스펙터 참조가 비어 있으면 Resources에서 자동으로 찾는다.
            // (씬 재생성 / 머지 / 에셋 재임포트로 참조가 끊겨도 게임이 돌아가도록)
            if (database == null)
            {
                database = Resources.Load<GameDatabase>(DatabaseResourceName);
                if (database != null)
                    Debug.LogWarning("[GameManager] 인스펙터의 Database가 비어 있어 " +
                                     $"Resources/{DatabaseResourceName} 에서 자동으로 불러왔습니다.");
            }

            if (database == null)
                Debug.LogError(
                    "[GameManager] GameDatabase를 찾을 수 없습니다.\n" +
                    "① MainMenu 씬의 GameManager 인스펙터 Database 슬롯에 GameDatabase.asset을 넣거나\n" +
                    "② [FishGame ▸ 1. 콘텐츠 에셋 생성]을 실행해 " +
                    $"Assets/_Project/Resources/{DatabaseResourceName}.asset 을 만드세요.");

            if (wipeSaveOnStart) SaveSystem.DeleteSave();

            Progress = SaveSystem.Load();
            PrepareSkillTree();
            SelectedStartZone = Progress.SelectedStartZone;
            RecalculateStats();
            AvoidDeadlyStartZone();
        }

        /// <summary>
        /// 이 구역에서 시작하면 바로 잡아먹힐 만큼 지금 몸이 작은가.
        /// (FinishRun의 자동 선택·balance_sim.py의 pick_start_zone과 같은 기준: 구역 평균 크기의 42%)
        /// </summary>
        public bool IsZoneDangerous(int index)
        {
            var z = database != null ? database.GetZone(index) : null;
            if (z == null || Stats == null || index <= 0) return false;
            return Stats.Size < z.AverageSpawnSize * 0.42f;
        }

        /// <summary>
        /// 불러온 세이브의 시작 구역이 지금 몸으로는 즉사하는 곳이면 (스킬 초기화·환불 뒤 등)
        /// 버틸 수 있는 가장 깊은 구역으로 한 번 옮긴다. 메뉴에서 직접 다시 고르는 건 막지 않는다.
        /// </summary>
        void AvoidDeadlyStartZone()
        {
            if (database == null || !IsZoneDangerous(SelectedStartZone)) return;
            int zone = SelectedStartZone;
            while (zone > 0 && (IsZoneDangerous(zone) || !ZoneFitsCurrentSize(zone))) zone--;
            if (!ZoneFitsCurrentSize(zone)) zone = ShallowestEnterableZone();
            // 몸이 커서 위 구역에 못 들어가 더 깊은 곳으로 간 경우 — EnsureStartZoneFits와 같이 그 구역을 연다
            // (안 그러면 세이브가 도달 구역으로 잘라 읽고, 메뉴에서도 선택 표시가 사라진다)
            if (zone > Progress.DeepestZoneReached) Progress.DeepestZoneReached = zone;
            SelectedStartZone = zone;
            Progress.SelectedStartZone = zone;
            SaveNow();
        }

        /// <summary>
        /// 비용 곡선을 DB 값으로 맞추고, 예전 세이브를 옮기고, 찍은 칸에서 레벨을 다시 계산한다.
        /// </summary>
        void PrepareSkillTree()
        {
            if (database == null) return;
            SkillCostCurve.Configure(database.skillCostGrowth, database.skillCostGrowthLate,
                                     database.skillCostSoftcap);

            if (database.skillTree == null || database.skillTree.Count == 0)
            {
                Debug.LogError("[GameManager] 스킬트리 칸이 없습니다. [FishGame ▸ 1. 콘텐츠 에셋 생성]을 다시 실행하세요. " +
                               "(세이브의 스킬은 건드리지 않았습니다)");
                return;
            }

            double refund = SkillTreeManager.MigrateLegacySave(Progress);
            SkillTreeManager.SyncLevels(database, Progress);
            if (refund > 0d || Progress.loadedVersion < PlayerProgress.CurrentVersion) SaveNow();
            if (refund > 0d) PendingNotice =
                $"스킬트리가 새 도면으로 바뀌었습니다.\n예전에 찍은 스킬을 초기화하고 재화 {FishGame.Utils.NumberFormatter.Format(refund)}을 돌려드렸어요.";
        }

        /// <summary>메인 화면이 한 번 띄워 줄 안내 문구 (없으면 null). 읽으면 비운다.</summary>
        public string PendingNotice { get; private set; }
        public string ConsumeNotice() { var n = PendingNotice; PendingNotice = null; return n; }

        void Start()
        {
            if (State == GameState.Boot)
                SetState(SceneManager.GetActiveScene().name == gameplayScene
                    ? GameState.Playing : GameState.MainMenu);
        }

        void OnApplicationPause(bool paused) { if (paused) SaveNow(); }
        void OnApplicationQuit() => SaveNow();

        // ── 상태 ────────────────────────────────────────────────
        void SetState(GameState next)
        {
            if (State == next) return;
            State = next;
            OnStateChanged?.Invoke(State);
        }

        // ── 진행도 ──────────────────────────────────────────────
        public void RecalculateStats()
        {
            if (database != null) Stats = PlayerStats.Build(database, Progress);
        }

        public void SaveNow() => SaveSystem.Save(Progress);

        public void NotifyProgressChanged()
        {
            RecalculateStats();
            OnProgressChanged?.Invoke();
            SaveNow();
        }

        // ── 시작 구역 선택 / 플레이 시작 ────────────────────────
        public bool CanSelectZone(int index) =>
            database != null && index >= 0 && index < database.ZoneCount &&
            index <= Progress.DeepestZoneReached && ZoneFitsCurrentSize(index);

        /// <summary>
        /// 지금 기본 크기로 이 구역에서 시작해도 출구로 빠져나갈 수 있는가.
        /// 스킬을 많이 찍으면 어항 배수구보다 몸이 커져서, 어항에서 시작하면 영영 못 내려간다.
        /// </summary>
        public bool ZoneFitsCurrentSize(int index)
        {
            var z = database != null ? database.GetZone(index) : null;
            if (z == null || Stats == null) return true;
            return Stats.Size <= z.MaxEnterableSize;
        }

        /// <summary>선택 가능한 가장 얕은 구역. 몸이 커져서 위쪽 구역이 막혔을 때 쓴다.</summary>
        public int ShallowestEnterableZone()
        {
            if (database == null) return 0;
            for (int i = 0; i < database.ZoneCount; i++)
                if (ZoneFitsCurrentSize(i)) return i;
            return database.DeepestZoneIndex;
        }

        public void SelectStartZone(int index)
        {
            if (!CanSelectZone(index)) return;
            SelectedStartZone = index;
            Progress.SelectedStartZone = index;
            SaveNow();
        }

        public void StartRun()
        {
            RecalculateStats();
            EnsureStartZoneFits();
            SetState(GameState.Playing);
            SceneManager.LoadScene(gameplayScene);
        }

        /// <summary>
        /// 스킬을 찍어 몸이 커지면 골라 둔 시작 구역의 출구보다 커질 수 있다.
        /// 그대로 시작하면 그 구역에 갇히므로, 들어갈 수 있는 가장 얕은 구역으로 옮긴다.
        /// 아직 가 본 적 없는 구역이어도 연다 — 몸이 그 구역보다 커졌다는 건 이미 넘어섰다는 뜻이다.
        /// </summary>
        void EnsureStartZoneFits()
        {
            if (database == null || ZoneFitsCurrentSize(SelectedStartZone)) return;

            int zone = SelectedStartZone;
            while (zone < database.DeepestZoneIndex && !ZoneFitsCurrentSize(zone)) zone++;

            if (zone > Progress.DeepestZoneReached) Progress.DeepestZoneReached = zone;
            SelectedStartZone = zone;
            Progress.SelectedStartZone = zone;
            SaveNow();
        }

        /// <summary>타이틀 화면으로. 타이틀 씬이 빌드에 없으면 메인 화면으로 간다.</summary>
        public void GoToTitle()
        {
            SaveNow();
            if (!Application.CanStreamedLevelBeLoaded(titleScene)) { GoToMainMenu(); return; }
            SetState(GameState.MainMenu);
            SceneManager.LoadScene(titleScene);
        }

        public void GoToMainMenu()
        {
            SetState(GameState.MainMenu);
            SceneManager.LoadScene(mainMenuScene);
        }

        // ── 판 종료 처리 ────────────────────────────────────────
        /// <summary>RunManager가 판이 끝났을 때 호출한다. 재화 정산과 해금을 여기서 처리.</summary>
        public void FinishRun(RunEndReason reason, double rawCurrency, int fishEaten,
                              float survivedSeconds, bool bossKilled,
                              int deepestZoneThisRun, int hiddenItemsFound)
        {
            int runStartZone = SelectedStartZone;   // 아래에서 다음 판 시작 구역으로 바뀌기 전에 이번 판 값을 잡아 둔다
            double penalty = reason == RunEndReason.Eaten && database != null ? database.deathCurrencyPenalty : 0d;

            double earned = Math.Max(0d, rawCurrency * (1d - penalty));

            // 새 구역 도달은 재화와 달리 죽어도 남는다 — 내려가 본 사실 자체가 진행이다
            bool newZone = false;
            if (database != null && database.recordDeepestZone &&
                deepestZoneThisRun > Progress.DeepestZoneReached)
            {
                Progress.DeepestZoneReached = deepestZoneThisRun;
                newZone = true;

                // 새 구역에 처음 닿으면 다음 판은 거기서 시작하게 골라 둔다 (메뉴에서 바꿀 수 있다).
                // 안 그러면 모르고 계속 어항에서 시작해 진행이 크게 느려진다.
                // 단, 기본 크기가 그 구역 물고기 평균의 42%도 안 되면 시작하자마자 잡아먹히니 그대로 둔다
                // (balance_sim.py의 pick_start_zone과 같은 기준).
                var reachedZone = database.GetZone(deepestZoneThisRun);
                bool strongEnough = reachedZone == null || Stats == null ||
                                    Stats.Size >= reachedZone.AverageSpawnSize * 0.42f;
                if (ZoneFitsCurrentSize(deepestZoneThisRun) && strongEnough)
                {
                    SelectedStartZone = deepestZoneThisRun;
                    Progress.SelectedStartZone = deepestZoneThisRun;
                }
            }

            if (bossKilled) Progress.MarkZoneCleared(deepestZoneThisRun);

            Progress.currency            += earned;
            Progress.totalCurrencyEarned += earned;
            Progress.totalFishEaten      += fishEaten;
            Progress.totalPlaySeconds    += survivedSeconds;
            Progress.totalRuns++;
            if (survivedSeconds > Progress.bestSurvivalSeconds) Progress.bestSurvivalSeconds = survivedSeconds;
            if (earned > Progress.bestRunCurrency) Progress.bestRunCurrency = earned;

            LastResult = new RunResult
            {
                Reason           = reason,
                StartZone        = runStartZone,
                DeepestZone      = deepestZoneThisRun,
                NewZoneReached   = newZone,
                CurrencyEarned   = earned,
                CurrencyRaw      = rawCurrency,
                FishEaten        = fishEaten,
                HiddenItemsFound = hiddenItemsFound,
                SurvivedSeconds  = survivedSeconds,
                BossKilled       = bossKilled,
            };

            // 이번 판에 주운 히든 아이템(+재화%)·새 구역이 다음 판 스탯과 UI에 바로 반영되게
            RecalculateStats();

            SaveNow();
            SetState(GameState.Result);
            OnRunFinished?.Invoke(LastResult);
            OnProgressChanged?.Invoke();
        }

        /// <summary>세이브를 지우고 메모리상의 진행도까지 초기화한다.</summary>
        public void ResetProgress()
        {
            SaveSystem.DeleteSave();
            Progress = new PlayerProgress();
            SelectedStartZone = 0;
            RecalculateStats();
            OnProgressChanged?.Invoke();
        }

        // ── 디버그 ──────────────────────────────────────────────
        [ContextMenu("Debug/재화 +1000")]
        void DebugAddCurrency() { Progress.currency += 1000d; NotifyProgressChanged(); }

        [ContextMenu("Debug/세이브 삭제 후 초기화")]
        void DebugWipe() => ResetProgress();
    }
}
