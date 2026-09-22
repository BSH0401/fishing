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
            SelectedStartZone = Progress.SelectedStartZone;
            RecalculateStats();
        }

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
            double penalty = reason == RunEndReason.Eaten && database != null ? database.deathCurrencyPenalty : 0d;

            double earned = Math.Max(0d, rawCurrency * (1d - penalty));

            // 새 구역 도달은 재화와 달리 죽어도 남는다 — 내려가 본 사실 자체가 진행이다
            bool newZone = false;
            if (database != null && database.recordDeepestZone &&
                deepestZoneThisRun > Progress.DeepestZoneReached)
            {
                Progress.DeepestZoneReached = deepestZoneThisRun;
                newZone = true;
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
                StartZone        = SelectedStartZone,
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
