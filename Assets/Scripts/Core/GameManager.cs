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
        public int MapIndex;
        public double CurrencyEarned;   // 페널티 적용 후 실제 획득량
        public double CurrencyRaw;      // 페널티 전 원본
        public int FishEaten;
        public float SurvivedSeconds;
        public bool BossKilled;
        public bool NewMapUnlocked;
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

        /// <summary>이번에 진입할 / 진입한 맵</summary>
        public int SelectedMapIndex { get; private set; }
        public MapData SelectedMap => database != null ? database.GetMap(SelectedMapIndex) : null;

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
            SelectedMapIndex = Mathf.Clamp(Progress.lastSelectedMap, 0, Progress.highestUnlockedMap);
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

        // ── 맵 선택 / 플레이 시작 ───────────────────────────────
        public bool CanSelectMap(int index) =>
            index >= 0 && index < database.MapCount && index <= Progress.highestUnlockedMap;

        public void SelectMap(int index)
        {
            if (!CanSelectMap(index)) return;
            SelectedMapIndex = index;
            Progress.lastSelectedMap = index;
            SaveNow();
        }

        public void StartRun()
        {
            RecalculateStats();
            SetState(GameState.Playing);
            SceneManager.LoadScene(gameplayScene);
        }

        public void GoToMainMenu()
        {
            SetState(GameState.MainMenu);
            SceneManager.LoadScene(mainMenuScene);
        }

        // ── 판 종료 처리 ────────────────────────────────────────
        /// <summary>RunManager가 판이 끝났을 때 호출한다. 재화 정산과 해금을 여기서 처리.</summary>
        public void FinishRun(RunEndReason reason, double rawCurrency, int fishEaten,
                              float survivedSeconds, bool bossKilled)
        {
            double penalty = reason == RunEndReason.Eaten ? database.deathCurrencyPenalty : 0d;

            double earned = Math.Max(0d, rawCurrency * (1d - penalty));

            bool newUnlock = false;
            if (bossKilled && !Progress.IsMapCleared(SelectedMapIndex))
            {
                Progress.MarkMapCleared(SelectedMapIndex);
                newUnlock = SelectedMapIndex + 1 < database.MapCount;
            }

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
                MapIndex         = SelectedMapIndex,
                CurrencyEarned   = earned,
                CurrencyRaw      = rawCurrency,
                FishEaten        = fishEaten,
                SurvivedSeconds  = survivedSeconds,
                BossKilled       = bossKilled,
                NewMapUnlocked   = newUnlock,
            };

            SaveNow();
            SetState(GameState.Result);
            OnRunFinished?.Invoke(LastResult);
        }

        /// <summary>세이브를 지우고 메모리상의 진행도까지 초기화한다.</summary>
        public void ResetProgress()
        {
            SaveSystem.DeleteSave();
            Progress = new PlayerProgress();
            SelectedMapIndex = 0;
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
