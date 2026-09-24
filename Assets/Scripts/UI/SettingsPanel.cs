using System;
using System.Collections.Generic;
using FishGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>
    /// 게임 설정 창 — 타이틀 · 메인(스킬트리) · 일시정지 어디서나 SettingsPanel.Open()으로 연다.
    /// 씬에 미리 놓을 필요 없이 실행 중에 만들어진다 (맨 위 캔버스, 수중 실험실 스타일).
    ///
    ///   사운드  전체 · 배경음 · 효과음 볼륨, 음소거
    ///   화면    화면 모드 · 해상도 · 수직 동기화 · 프레임 제한
    ///   조작    키보드 / 마우스 따라가기 + 키 안내
    ///
    /// 바꾸는 즉시 적용되고, 닫을 때 settings.json에 저장한다. ESC로 닫힌다.
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        static SettingsPanel _open;
        static int _closedFrame = -10;

        /// <summary>설정 창이 떠 있는가 (일시정지 메뉴가 ESC를 양보한다).</summary>
        public static bool IsOpen => _open != null;

        /// <summary>이번 프레임에 ESC로 닫혔는가 — 같은 ESC가 일시정지까지 풀지 않게.</summary>
        public static bool ClosedThisFrame => Time.frameCount - _closedFrame <= 1;

        public static SettingsPanel Open(Action onClosed = null)
        {
            if (_open != null) return _open;
            UIKit.EnsureEventSystem();
            var canvas = UIKit.OverlayCanvas("SettingsPanel", 900);
            var panel = canvas.gameObject.AddComponent<SettingsPanel>();
            panel._onClosed = onClosed;
            panel._previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            panel.LockBehind();
            panel.Build();
            _open = panel;
            return panel;
        }

        Action _onClosed;
        readonly List<Selectable> _locked = new List<Selectable>();

        /// <summary>
        /// 뒤 화면(타이틀 메뉴·일시정지 패널)의 버튼을 잠근다. 어두운 막이 마우스는 막지만
        /// 방향키 탐색은 화면 전체의 버튼을 찾아가므로, 설정 창 밖으로 선택이 새지 않게 한다.
        /// </summary>
        void LockBehind()
        {
            foreach (var s in Selectable.allSelectablesArray)
            {
                if (s == null || !s.interactable) continue;
                s.interactable = false;
                _locked.Add(s);
            }
        }

        void UnlockBehind()
        {
            foreach (var s in _locked) if (s != null) s.interactable = true;
            _locked.Clear();
        }
        GameObject _previousSelection;
        InputAction _escape;

        readonly List<Button> _tabs = new List<Button>();
        readonly List<GameObject> _pages = new List<GameObject>();
        int _tab;

        // 화면 탭
        LabSelector _resolution, _frameLimit, _screenMode;
        LabSwitch _vsync;
        List<Vector2Int> _resList;

        // 조작 탭
        TMP_Text _keyGuide;

        const float RowH = 76f;

        // ══════════════════════════════════════════════════════════
        void Build()
        {
            var root = (RectTransform)transform;

            // 뒤를 어둡게 + 뒤쪽 클릭 막기
            var dim = UIKit.Image(root, "Dim", null, new Color(0f, 0.02f, 0.04f, 0.72f), raycast: true);
            UIKit.Stretch(dim.rectTransform);

            var win = UIKit.Image(root, "Window", LabArt.Panel, LabStyle.Fill, raycast: true);
            LabStyle.Panel(win, corners: true, fill: new Color(0.03f, 0.10f, 0.13f, 0.97f));
            var w = UIKit.Place(win.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 700f));

            UIKit.Place(UIKit.Text(w, "Title", "설정", 40f, UIKit.Ink, TextAlignmentOptions.Left, FontStyles.Bold).rectTransform,
                        new Vector2(0f, 1f), new Vector2(44f, -30f), new Vector2(400f, 56f));

            var close = UIKit.Button(w, "Close", "×", new Vector2(56f, 56f), fontSize: 34f);
            UIKit.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(-28f, -28f), new Vector2(56f, 56f));
            close.onClick.AddListener(Close);

            // 탭
            string[] tabNames = { "사운드", "화면", "조작" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                int idx = i;
                var tab = UIKit.Button(w, $"Tab{i}", tabNames[i], new Vector2(190f, 54f));
                UIKit.Place((RectTransform)tab.transform, new Vector2(0f, 1f), new Vector2(44f + i * 204f + 95f, -135f), new Vector2(190f, 54f),
                            new Vector2(0.5f, 0.5f));   // 가운데 피벗 — 커져도 옆 탭을 덮지 않게
                tab.onClick.AddListener(() => ShowTab(idx));
                _tabs.Add(tab);
            }

            var line = UIKit.Image(w, "Divider", null, new Color(0.36f, 0.86f, 0.86f, 0.25f));
            UIKit.Place(line.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -182f), new Vector2(892f, 2f));

            _pages.Add(BuildSoundPage(w));
            _pages.Add(BuildDisplayPage(w));
            _pages.Add(BuildControlPage(w));

            // 아래 버튼
            var reset = UIKit.Button(w, "Defaults", "기본값으로", new Vector2(220f, 56f));
            UIKit.Place((RectTransform)reset.transform, new Vector2(0f, 0f), new Vector2(44f, 36f), new Vector2(220f, 56f));
            reset.onClick.AddListener(() => { GameSettings.ResetToDefaults(); RefreshAll(); });

            var done = UIKit.Button(w, "Done", "닫기", new Vector2(220f, 56f), primary: true);
            UIKit.Place((RectTransform)done.transform, new Vector2(1f, 0f), new Vector2(-44f, 36f), new Vector2(220f, 56f));
            done.onClick.AddListener(Close);

            _escape = new InputAction("SettingsClose", InputActionType.Button);
            _escape.AddBinding("<Keyboard>/escape");
            _escape.AddBinding("<Gamepad>/buttonEast");
            _escape.AddBinding("<Gamepad>/start");
            _escape.Enable();

            ShowTab(0);
        }

        RectTransform NewPage(RectTransform win, string name)
        {
            var page = UIKit.Rect(win, name);
            page.anchorMin = Vector2.zero; page.anchorMax = Vector2.one;
            page.offsetMin = new Vector2(44f, 110f);
            page.offsetMax = new Vector2(-44f, -200f);
            return page;
        }

        /// <summary>왼쪽 이름 + 오른쪽 조작 부품 한 줄. 부품을 놓을 자리(오른쪽 칸)를 돌려준다.</summary>
        RectTransform Row(RectTransform page, int index, string label, string hint = null)
        {
            var row = UIKit.Place(UIKit.Rect(page, $"Row_{label}"), new Vector2(0f, 1f), new Vector2(0f, -index * RowH),
                                  new Vector2(892f, RowH), new Vector2(0f, 1f));
            var t = UIKit.Text(row, "Label", label, 24f, UIKit.Ink);
            UIKit.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, hint != null ? 10f : 0f), new Vector2(360f, 36f));
            if (hint != null)
            {
                var h = UIKit.Text(row, "Hint", hint, 15f, UIKit.InkDim);
                UIKit.Place(h.rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, -18f), new Vector2(380f, 24f));
            }
            var slot = UIKit.Rect(row, "Control");
            UIKit.Place(slot, new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(480f, RowH - 14f), new Vector2(1f, 0.5f));
            return slot;
        }

        // ── 사운드 ──────────────────────────────────────────────
        GameObject BuildSoundPage(RectTransform win)
        {
            var page = NewPage(win, "Page_Sound");
            // 기본값으로 되돌리면 Data 객체가 새로 만들어지므로 항상 GameSettings.Data를 거쳐 읽고 쓴다
            VolumeRow(page, 0, "전체 볼륨", () => GameSettings.Data.masterVolume, v => GameSettings.Data.masterVolume = v);
            VolumeRow(page, 1, "배경음", () => GameSettings.Data.musicVolume, v => GameSettings.Data.musicVolume = v);
            VolumeRow(page, 2, "효과음", () => GameSettings.Data.sfxVolume, v => GameSettings.Data.sfxVolume = v, preview: true);

            var slot = Row(page, 3, "음소거", "모든 소리를 끈다");
            var sw = UIKit.Switch(slot, "Mute");
            UIKit.Place((RectTransform)sw.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(96f, 44f));
            sw.Set(GameSettings.Data.mute, instant: true);
            sw.Changed += on => { GameSettings.Data.mute = on; GameSettings.ApplyAudio(); };
            _refreshers.Add(() => sw.Set(GameSettings.Data.mute, instant: true));
            return page.gameObject;
        }

        readonly List<Action> _refreshers = new List<Action>();

        void VolumeRow(RectTransform page, int index, string label, Func<float> get, Action<float> set, bool preview = false)
        {
            var slot = Row(page, index, label);
            var slider = UIKit.Slider(slot, "Slider", new Vector2(380f, 40f));
            UIKit.Place((RectTransform)slider.transform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(380f, 40f));
            var pct = UIKit.Text(slot, "Value", "", 22f, UIKit.Accent, TextAlignmentOptions.Right);
            UIKit.Place(pct.rectTransform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(84f, 36f));

            slider.SetValueWithoutNotify(get());
            pct.text = $"{Mathf.RoundToInt(get() * 100f)}%";
            float lastPreview = 0f;
            slider.onValueChanged.AddListener(v =>
            {
                set(v);
                pct.text = $"{Mathf.RoundToInt(v * 100f)}%";
                GameSettings.ApplyAudio();
                // 효과음 볼륨은 들어 봐야 안다 — 너무 자주 울리지 않게 0.15초 간격으로 들려준다
                if (preview && Time.unscaledTime - lastPreview > 0.15f)
                {
                    lastPreview = Time.unscaledTime;
                    var bank = AudioManager.Instance.Bank;
                    var clip = bank != null ? (bank.buttonClick != null ? bank.buttonClick : bank.eatSmall) : null;
                    if (clip != null) AudioManager.Play(clip);
                }
            });
            _refreshers.Add(() =>
            {
                slider.SetValueWithoutNotify(get());
                pct.text = $"{Mathf.RoundToInt(get() * 100f)}%";
            });
        }

        // ── 화면 ────────────────────────────────────────────────
        GameObject BuildDisplayPage(RectTransform win)
        {
            var page = NewPage(win, "Page_Display");
            var d = GameSettings.Data;

            _screenMode = UIKit.Selector(Row(page, 0, "화면 모드"), "ScreenMode", new Vector2(480f, 56f));
            FitRight(_screenMode.transform);
            _screenMode.SetOptions(GameSettings.ScreenModeNames, d.screenMode);
            _screenMode.Changed += i => { GameSettings.Data.screenMode = i; GameSettings.ApplyDisplay(changeResolution: false); };

#if UNITY_EDITOR
            string resHint = "에디터에서는 바뀌지 않음 (빌드에서 적용)";
#else
            string resHint = null;
#endif
            _resolution = UIKit.Selector(Row(page, 1, "해상도", resHint), "Resolution", new Vector2(480f, 56f));
            FitRight(_resolution.transform);
            _resolution.Changed += i =>
            {
                var r = _resList[i];
                GameSettings.Data.width = r.x;
                GameSettings.Data.height = r.y;
                GameSettings.ApplyDisplay(changeResolution: true);
            };

            var vslot = Row(page, 2, "수직 동기화", "화면 찢어짐을 막는다");
            _vsync = UIKit.Switch(vslot, "VSync");
            UIKit.Place((RectTransform)_vsync.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(96f, 44f));
            _vsync.Changed += on => { GameSettings.Data.vSync = on; GameSettings.ApplyDisplay(changeResolution: false); RefreshFrameLimit(); };

            _frameLimit = UIKit.Selector(Row(page, 3, "프레임 제한"), "FrameLimit", new Vector2(480f, 56f));
            FitRight(_frameLimit.transform);
            _frameLimit.Changed += i => { GameSettings.Data.frameLimit = GameSettings.FrameLimits[i]; GameSettings.ApplyDisplay(changeResolution: false); };

            _refreshers.Add(RefreshDisplay);
            RefreshDisplay();
            return page.gameObject;
        }

        static void FitRight(Transform t) =>
            UIKit.Place((RectTransform)t, new Vector2(1f, 0.5f), Vector2.zero, ((RectTransform)t).sizeDelta);

        void RefreshDisplay()
        {
            var d = GameSettings.Data;
            _screenMode.SetOptions(GameSettings.ScreenModeNames, d.screenMode);

            _resList = GameSettings.AvailableResolutions();
            var cur = GameSettings.CurrentResolution;
            var names = new string[_resList.Count];
            int sel = 0;
            for (int i = 0; i < _resList.Count; i++)
            {
                names[i] = $"{_resList[i].x} × {_resList[i].y}";
                if (_resList[i] == cur) sel = i;
            }
            _resolution.SetOptions(names, sel);

            _vsync.Set(d.vSync, instant: true);
            RefreshFrameLimit();
        }

        void RefreshFrameLimit()
        {
            var d = GameSettings.Data;
            var names = new string[GameSettings.FrameLimits.Length];
            for (int i = 0; i < names.Length; i++)
                names[i] = GameSettings.FrameLimits[i] > 0 ? $"{GameSettings.FrameLimits[i]} FPS" : "제한 없음";
            _frameLimit.SetOptions(names, Mathf.Max(0, Array.IndexOf(GameSettings.FrameLimits, d.frameLimit)));
            _frameLimit.SetInteractable(!d.vSync, d.vSync ? "수직 동기화 사용 중" : null);
        }

        // ── 조작 ────────────────────────────────────────────────
        GameObject BuildControlPage(RectTransform win)
        {
            var page = NewPage(win, "Page_Controls");
            var sel = UIKit.Selector(Row(page, 0, "조작 방식", "판 중에도 바로 바뀐다"), "Scheme", new Vector2(480f, 56f));
            FitRight(sel.transform);
            sel.SetOptions(GameSettings.ControlSchemeNames, GameSettings.Data.controlScheme);
            sel.Changed += i => { GameSettings.Data.controlScheme = i; GameSettings.ApplyControls(); RefreshKeyGuide(); };
            _refreshers.Add(() => { sel.SetOptions(GameSettings.ControlSchemeNames, GameSettings.Data.controlScheme); RefreshKeyGuide(); });

            var box = UIKit.Image(page, "KeyGuide", LabArt.Panel, UIKit.Track);
            LabStyle.Panel(box, corners: false, fill: new Color(0.02f, 0.07f, 0.09f, 0.9f));
            UIKit.Place(box.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -RowH - 10f), new Vector2(892f, 290f), new Vector2(0f, 1f));

            _keyGuide = UIKit.Text(box.rectTransform, "Text", "", 21f, UIKit.Ink);
            _keyGuide.textWrappingMode = TextWrappingModes.Normal;
            _keyGuide.lineSpacing = 18f;
            UIKit.Stretch(_keyGuide.rectTransform, 24f);
            _keyGuide.alignment = TextAlignmentOptions.TopLeft;
            RefreshKeyGuide();
            return page.gameObject;
        }

        void RefreshKeyGuide()
        {
            if (_keyGuide == null) return;
            bool mouse = GameSettings.Data.controlScheme == 1;
            string k(string s) => $"<color=#73F2E6>{s}</color>";
            string row(string action, string keys, string pad) => $"{action}<pos=26%>{keys}<pos=74%><color=#9EC2C6>{pad}</color>\n";

            _keyGuide.text =
                $"<color=#9EC2C6><size=17>동작<pos=26%>키보드 · 마우스<pos=74%>게임패드</size></color>\n" +
                row("이동", mouse ? $"{k("마우스 위치")} 쪽으로 헤엄" : $"{k("W A S D")} · {k("방향키")}", "왼쪽 스틱") +
                row("부스터 (대쉬)", $"{k("마우스 오른쪽")} · {k("Space")}", "A") +
                row("청소기 (누르고 있기)", $"{k("마우스 왼쪽")} · {k("E")}", "X") +
                row("일시정지", k("ESC"), "Start") +
                row("스킬트리 확대 · 이동", $"{k("휠")} · {k("드래그")}", "—");
        }

        // ══════════════════════════════════════════════════════════
        void ShowTab(int index)
        {
            _tab = index;
            for (int i = 0; i < _pages.Count; i++) _pages[i].SetActive(i == index);
            for (int i = 0; i < _tabs.Count; i++) LabStyle.Button(_tabs[i], primary: i == index);

            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_tabs[index].gameObject);
        }

        void RefreshAll()
        {
            foreach (var r in _refreshers) r();
        }

        void Update()
        {
            if (_escape != null && _escape.WasPressedThisFrame()) Close();
        }

        public void Close()
        {
            if (_open != this) return;
            GameSettings.Save();
            _open = null;
            _closedFrame = Time.frameCount;
            UnlockBehind();
            if (EventSystem.current != null && _previousSelection != null)
                EventSystem.current.SetSelectedGameObject(_previousSelection);
            _onClosed?.Invoke();
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            _escape?.Dispose();
            if (_open == this) { _open = null; GameSettings.Save(); }
            UnlockBehind();
        }
    }
}
