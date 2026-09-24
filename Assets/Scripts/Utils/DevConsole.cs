#if UNITY_EDITOR || DEVELOPMENT_BUILD
using FishGame.Core;
using FishGame.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame.Utils
{
    /// <summary>
    /// 개발자 모드. F1로 열고 닫는다.
    ///
    /// 에디터와 Development Build에서만 컴파일된다 — 정식 빌드에는 이 파일 자체가 없다.
    /// 씬에 배치할 필요 없이 게임이 시작되면 스스로 생겨나고, 씬을 넘어 살아남는다.
    ///
    /// IMGUI(OnGUI)로 그린다. 캔버스·프리팹이 없으니 씬 생성기를 건드리지 않고,
    /// 게임 UI가 깨져 있어도 이 창은 뜬다 — 디버그 도구가 디버그 대상에 기대면 안 된다.
    /// </summary>
    public class DevConsole : MonoBehaviour
    {
        static DevConsole _instance;

        bool _open;
        Rect _window = new Rect(0, 0, 380, 620);
        Vector2 _scroll;
        bool _placed;

        float _timeScale = 1f;
        bool _confirmWipe;
        string _toast;
        float _toastUntil;

        GUIStyle _title, _label, _small, _button, _toggle, _box;
        Font _font;

        // ══════════════════════════════════════════════════════════
        //  자동 생성
        // ══════════════════════════════════════════════════════════
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_instance != null) return;
            var go = new GameObject("[DevConsole]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DevConsole>();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            DevFlags.PointerOverPanel = false;
        }

        // ══════════════════════════════════════════════════════════
        //  루프
        // ══════════════════════════════════════════════════════════
        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame)
            {
                _open = !_open;
                _confirmWipe = false;
            }

            if (!_open) DevFlags.PointerOverPanel = false;

            ApplyTimeScale();
        }

        /// <summary>
        /// 게임 속도를 매 프레임 다시 건다.
        /// 일시정지·히트스톱은 timeScale을 0으로, 끝나면 1로 되돌리는데,
        /// 그 코드들을 전부 고치는 대신 여기서 "0이 아닐 때만" 덮어쓴다.
        /// </summary>
        void ApplyTimeScale()
        {
            var run = RunManager.Instance;
            if (run == null || !run.IsRunning || run.IsPaused) return;
            if (Time.timeScale <= 0f) return;                       // 히트스톱 중
            if (!Mathf.Approximately(Time.timeScale, _timeScale)) Time.timeScale = _timeScale;
        }

        // ══════════════════════════════════════════════════════════
        //  그리기
        // ══════════════════════════════════════════════════════════
        void OnGUI()
        {
            EnsureStyles();

            if (!_open)
            {
                GUI.Label(new Rect(8, Screen.height - 22, 200, 20), "DEV · F1", _small);
                return;
            }

            if (!_placed)
            {
                _window.x = Screen.width - _window.width - 16;
                _window.y = 16;
                _placed = true;
            }

            _window = GUI.Window(0x46495348, _window, DrawWindow, "개발자 모드", _box);

            // 창 위의 클릭이 게임 조작으로 새지 않게 — IMGUI 좌표는 위가 0이다
            Vector2 mouse = Event.current.mousePosition;
            DevFlags.PointerOverPanel = _window.Contains(mouse);

            if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime < _toastUntil)
            {
                var r = new Rect(Screen.width * 0.5f - 200, 24, 400, 32);
                GUI.Box(r, _toast, _box);
            }
        }

        void DrawWindow(int id)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);

            DrawProgressSection();
            GUILayout.Space(10);
            DrawRunSection();

            GUILayout.EndScrollView();

            GUILayout.Label("F1 로 닫기 · 창 제목을 끌어서 옮기기", _small);
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        // ══════════════════════════════════════════════════════════
        //  진행도 치트
        // ══════════════════════════════════════════════════════════
        void DrawProgressSection()
        {
            GUILayout.Label("진행도", _title);

            var gm = GameManager.Instance;
            if (gm == null || gm.Progress == null || gm.Database == null)
            {
                GUILayout.Label("GameManager가 없습니다. MainMenu 씬에서 시작하세요.", _label);
                return;
            }

            var p = gm.Progress;
            GUILayout.Label($"재화 {NumberFormatter.Format(p.currency)}   칸 {p.totalNodesPurchased}개" +
                            $"도달 {p.DeepestZoneReached + 1}구역   히든 {p.HiddenItemCount}개", _small);

            GUILayout.BeginHorizontal();
            if (Btn("+1천"))  DevCheats.AddCurrency(1e3);
            if (Btn("+100만")) DevCheats.AddCurrency(1e6);
            if (Btn("+10억")) DevCheats.AddCurrency(1e9);
            if (Btn("×10"))  DevCheats.MultiplyCurrency(10);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (Btn("스킬 전부 최대")) Toast(DevCheats.MaxAllSkills());
            if (Btn("스킬 초기화"))    Toast(DevCheats.ResetSkills());
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (Btn("구역 전부 해금"))  Toast(DevCheats.UnlockAllZones());
            if (Btn("구역 잠금"))      Toast(DevCheats.LockZones());
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (Btn("히든 전부 획득")) Toast(DevCheats.CollectAllHidden());
            if (Btn("히든 초기화"))    Toast(DevCheats.ResetHidden());
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (Btn("도감 전부 채우기")) Toast(DevCheats.FillCodex());
            if (Btn("도감 초기화"))      Toast(DevCheats.ResetCodex());
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            if (!_confirmWipe)
            {
                if (Btn("세이브 초기화…")) _confirmWipe = true;
            }
            else
            {
                GUILayout.BeginHorizontal();
                var prev = GUI.color;
                GUI.color = new Color(1f, 0.55f, 0.5f);
                if (Btn("정말 초기화")) { gm.ResetProgress(); _confirmWipe = false; Toast("세이브를 초기화했습니다"); }
                GUI.color = prev;
                if (Btn("취소")) _confirmWipe = false;
                GUILayout.EndHorizontal();
            }

            if (RunManager.Instance != null && RunManager.Instance.IsRunning)
                GUILayout.Label("※ 스킬·도감·히든 변경은 다음 판부터 반영됩니다", _small);
        }

        // ══════════════════════════════════════════════════════════
        //  판 중 치트
        // ══════════════════════════════════════════════════════════
        void DrawRunSection()
        {
            GUILayout.Label("이번 판", _title);

            var run = RunManager.Instance;
            if (run == null || !run.IsRunning || run.Player == null)
            {
                GUILayout.Label("플레이 중일 때만 쓸 수 있습니다.", _label);
                DrawTimeScale();
                return;
            }

            var player = run.Player;
            var zone = run.CurrentZone;
            GUILayout.Label($"{(zone != null ? zone.displayName : "?")}  ·  크기 {player.Size:0.00}  ·  " +
                            $"남은 시간 {run.TimeRemaining:0.0}s", _small);

            DevFlags.GodMode      = GUILayout.Toggle(DevFlags.GodMode, " 무적 (먹혀도 안 죽음)", _toggle);
            DevFlags.InfiniteTime = GUILayout.Toggle(DevFlags.InfiniteTime, " 시간 무한", _toggle);

            GUILayout.BeginHorizontal();
            if (Btn("시간 가득")) run.DevRefillTime();
            if (Btn("판 끝내기")) run.EndRun(RunEndReason.TimeOut);
            GUILayout.EndHorizontal();

            // ── 크기 ──
            GUILayout.Space(4);
            GUILayout.Label("크기", _label);
            GUILayout.BeginHorizontal();
            if (Btn("÷1.5")) SetSize(player.Size / 1.5f);
            if (Btn("×1.5")) SetSize(player.Size * 1.5f);
            if (Btn("×3"))   SetSize(player.Size * 3f);
            GUILayout.EndHorizontal();

            float need = run.NextGateRequiredSize;
            if (need > 0f && Btn($"다음 통로 크기로 ({need:0.#})")) SetSize(need * 1.02f);
            if (Btn("보스를 먹을 크기로 (43)")) SetSize(43.5f);

            // ── 이동 ──
            GUILayout.Space(4);
            GUILayout.Label("구역 이동 (위쪽 통로는 자동으로 열림)", _label);
            GUILayout.BeginHorizontal();
            var layout = run.Layout;
            if (layout != null)
            {
                for (int i = 0; i < layout.ZoneCount; i++)
                {
                    var z = layout.GetZone(i);
                    if (Btn(z != null ? z.displayName : $"{i + 1}")) run.DevTeleportToZone(i);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (Btn("통로 전부 열기")) run.DevOpenAllGates();
            if (Btn(run.BossReleased ? "보스 풀려남" : "보스 소환")) run.DevReleaseBossNow();
            GUILayout.EndHorizontal();

            DrawTimeScale();
        }

        void DrawTimeScale()
        {
            GUILayout.Space(4);
            GUILayout.Label($"게임 속도 ×{_timeScale:0.##}", _label);
            GUILayout.BeginHorizontal();
            foreach (float s in new[] { 0.25f, 0.5f, 1f, 2f, 4f })
            {
                var prev = GUI.color;
                if (Mathf.Approximately(_timeScale, s)) GUI.color = new Color(0.55f, 1f, 0.7f);
                if (Btn($"×{s:0.##}")) _timeScale = s;
                GUI.color = prev;
            }
            GUILayout.EndHorizontal();
        }

        void SetSize(float size)
        {
            var run = RunManager.Instance;
            if (run == null || run.Player == null || run.Player.Body == null) return;
            // 치트로도 절대 상한은 넘기지 않는다 — 실제 게임에서 나올 수 없는 상태를 QA하면 헷갈린다
            float cap = run.Database != null ? run.Database.maxPlayerSize : 999f;
            run.Player.Body.Size = Mathf.Clamp(size, 0.1f, cap);
        }

        // ══════════════════════════════════════════════════════════
        //  도움
        // ══════════════════════════════════════════════════════════
        bool Btn(string text) => GUILayout.Button(text, _button, GUILayout.MinHeight(28));

        void Toast(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            _toast = msg;
            _toastUntil = Time.unscaledTime + 2.2f;
        }

        void EnsureStyles()
        {
            if (_title != null) return;

            // 기본 IMGUI 폰트에는 한글이 없을 수 있어 OS 폰트를 쓴다.
            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans KR", "NanumGothic", "Apple SD Gothic Neo", "Arial" }, 15);

            _box = new GUIStyle(GUI.skin.window) { font = _font, fontSize = 15 };
            _box.normal.textColor = Color.white;

            _title = new GUIStyle(GUI.skin.label) { font = _font, fontSize = 16, fontStyle = FontStyle.Bold };
            _title.normal.textColor = new Color(0.55f, 0.9f, 1f);

            _label = new GUIStyle(GUI.skin.label) { font = _font, fontSize = 14, wordWrap = true };
            _label.normal.textColor = new Color(0.92f, 0.94f, 0.96f);

            _small = new GUIStyle(_label) { fontSize = 12 };
            _small.normal.textColor = new Color(0.7f, 0.76f, 0.82f);

            _button = new GUIStyle(GUI.skin.button) { font = _font, fontSize = 14 };
            _toggle = new GUIStyle(GUI.skin.toggle) { font = _font, fontSize = 14 };
            _toggle.normal.textColor = _toggle.onNormal.textColor = new Color(0.92f, 0.94f, 0.96f);
        }
    }
}
#endif
