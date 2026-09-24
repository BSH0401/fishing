using System.Collections;
using System.Collections.Generic;
using FishGame.Core;
using FishGame.Data;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>
    /// 타이틀 화면 — 게임을 켜면 가장 먼저 나온다.
    ///
    ///   이어하기   세이브가 있으면. 스킬트리(메인) 화면으로 간다
    ///   새 게임    세이브가 있으면 한 번 더 묻고 진행도를 지운 뒤 시작 (설정은 남는다)
    ///   설정       SettingsPanel
    ///   종료
    ///
    /// 화면은 전부 실행 중에 만든다: 수중 실험실 배경(UnderwaterBackdrop) 위로
    /// 게임 속 물고기들이 깊이별로 헤엄치고, 멀리 리바이어던 그림자가 지나간다.
    /// 씬에는 캔버스 하나와 이 컴포넌트만 있으면 된다.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        [Header("문구")]
        [SerializeField] string gameTitle = "실험체 #7";
        [SerializeField] string tagLine = "SUBJECT No.7  ·  ESCAPE PROTOCOL";
        [SerializeField] string subTitle = "어항에서 바다까지 — 먹고, 자라고, 탈출하라";

        [Header("배경 물고기")]
        [SerializeField] int fishCount = 16;

        GameManager _game;
        RectTransform _root;
        RectTransform _titleRt;
        Image _fade;
        bool _busy;
        GameObject _confirm;
        Button _firstButton;

        class Swimmer
        {
            public RectTransform Rt; public Image Img;
            public float Speed, Bob, Phase, BaseY, Depth; public int Dir;
        }
        readonly List<Swimmer> _swimmers = new List<Swimmer>();

        // ══════════════════════════════════════════════════════════
        void Start()
        {
            _game = GameManager.Instance;
            if (_game == null)
            {
                // 타이틀 씬만 단독으로 틀었을 때 — GameManager가 Resources에서 DB를 찾아 스스로 뜬다
                _game = new GameObject("GameManager").AddComponent<GameManager>();
            }

            UIKit.EnsureEventSystem();
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = UIKit.OverlayCanvas("Title Canvas", 0);
            _root = (RectTransform)canvas.transform;

            BuildBackground();
            BuildMenu();
            BuildFooter();

            _fade = UIKit.Image(_root, "Fade", null, Color.black, raycast: true);
            UIKit.Stretch(_fade.rectTransform);
            StartCoroutine(FadeTo(0f, 0.8f));

            var bank = AudioManager.Instance.Bank;
            AudioManager.PlayMusic(bank != null ? bank.menuMusic : null);
        }

        // ── 배경 ────────────────────────────────────────────────
        void BuildBackground()
        {
            var bg = UIKit.Rect(_root, "Backdrop");
            UIKit.Stretch(bg);
            bg.gameObject.AddComponent<RectMask2D>();
            var backdrop = bg.gameObject.AddComponent<UnderwaterBackdrop>();
            backdrop.Build();

            // 물고기 층은 빛줄기·물방울 위, 비네트 아래
            var fishLayer = UIKit.Rect(bg, "Fish");
            UIKit.Stretch(fishLayer);
            fishLayer.SetSiblingIndex(Mathf.Max(0, bg.childCount - 2));

            var species = new List<FishSpecies>();
            FishSpecies boss = null;
            var db = _game != null ? _game.Database : null;
            if (db != null && db.allFish != null)
                foreach (var f in db.allFish)
                {
                    if (f == null || f.sprite == null) continue;
                    if (f.isBoss) boss = f; else species.Add(f);
                }

            // 멀리 지나가는 리바이어던 그림자 — 제일 뒤
            if (boss != null) AddSwimmer(fishLayer, boss, depth: 0.05f, pixelSize: 760f, speed: 18f);

            if (species.Count == 0) return;
            for (int i = 0; i < fishCount; i++)
            {
                var sp = species[Random.Range(0, species.Count)];
                float depth = Random.value;                                   // 0 멀리 → 1 가까이
                float sizePx = Mathf.Lerp(34f, 120f, depth) * Mathf.Lerp(0.8f, 1.6f, Mathf.InverseLerp(0.3f, 55f, sp.size));
                AddSwimmer(fishLayer, sp, depth, sizePx, Mathf.Lerp(24f, 90f, depth) * Random.Range(0.8f, 1.2f));
            }
            // 가까운 것이 앞에 오게
            _swimmers.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            foreach (var s in _swimmers) s.Rt.SetAsLastSibling();
        }

        void AddSwimmer(RectTransform layer, FishSpecies sp, float depth, float pixelSize, float speed)
        {
            var img = UIKit.Image(layer, $"Swim_{sp.name}", sp.sprite, Color.white);
            img.preserveAspect = true;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.sizeDelta = new Vector2(pixelSize, pixelSize);

            // 멀수록 물색에 묻혀 어둡고 흐리다
            var water = new Color(0.10f, 0.30f, 0.34f);
            var c = Color.Lerp(water, sp.tint, Mathf.Lerp(0.15f, 0.9f, depth));
            c.a = sp.isBoss ? 0.22f : Mathf.Lerp(0.28f, 0.95f, depth);
            img.color = c;

            var s = new Swimmer
            {
                Rt = r, Img = img, Speed = speed, Depth = depth,
                Dir = Random.value < 0.5f ? 1 : -1,
                Bob = Mathf.Lerp(4f, 14f, depth), Phase = Random.value * 10f,
                BaseY = sp.isBoss ? 0.36f : Random.Range(0.08f, 0.92f),
            };
            // 오른쪽을 보는 그림이 기본 — 왼쪽으로 가면 뒤집는다
            bool facesLeft = sp.spriteFacesLeft;
            float flip = (s.Dir < 0) != facesLeft ? -1f : 1f;
            r.localScale = new Vector3(flip, 1f, 1f);
            r.anchoredPosition = new Vector2(Random.Range(0f, 1920f), 0f);
            _swimmers.Add(s);
        }

        // ── 메뉴 ────────────────────────────────────────────────
        void BuildMenu()
        {
            var col = UIKit.Rect(_root, "Menu");
            UIKit.Place(col, new Vector2(0f, 0.5f), new Vector2(150f, 40f), new Vector2(900f, 820f), new Vector2(0f, 0.5f));

            var tag = UIKit.Text(col, "Tag", tagLine, 22f, UIKit.Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            tag.characterSpacing = 8f;
            UIKit.Place(tag.rectTransform, new Vector2(0f, 1f), new Vector2(4f, 0f), new Vector2(900f, 34f), new Vector2(0f, 1f));

            var title = UIKit.Text(col, "Title", gameTitle, 150f, Color.white, TextAlignmentOptions.Left, FontStyles.Bold);
            _titleRt = UIKit.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -40f), new Vector2(900f, 180f), new Vector2(0f, 1f));

            var sub = UIKit.Text(col, "SubTitle", subTitle, 30f, UIKit.InkDim, TextAlignmentOptions.Left);
            UIKit.Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(6f, -226f), new Vector2(900f, 44f), new Vector2(0f, 1f));

            // 버튼
            var p = _game != null ? _game.Progress : null;
            bool hasSave = p != null && (p.totalRuns > 0 || p.totalNodesPurchased > 0 || p.currency > 0d);
            float y = -330f;
            const float gap = 84f;
            var buttons = new List<Button>();

            if (hasSave)
            {
                var cont = MenuButton(col, "Continue", "이어하기", y, primary: true);
                cont.onClick.AddListener(Continue);
                buttons.Add(cont);
                var info = UIKit.Text(col, "SaveInfo", SaveSummary(p), 20f, UIKit.InkDim);
                // 고른 버튼이 5% 커져도 닿지 않게 한 칸 띄우고, 버튼 높이 가운데에 맞춘다
                UIKit.Place(info.rectTransform, new Vector2(0f, 1f), new Vector2(396f, y - 33f), new Vector2(520f, 30f), new Vector2(0f, 0.5f));
                y -= gap;
            }

            var start = MenuButton(col, "NewGame", hasSave ? "새 게임" : "게임 시작", y, primary: !hasSave);
            start.onClick.AddListener(() => { if (hasSave) AskNewGame(); else Continue(); });
            buttons.Add(start);
            y -= gap;

            var settings = MenuButton(col, "Settings", "설정", y);
            settings.onClick.AddListener(() => SettingsPanel.Open(() => Select(_firstButton)));
            buttons.Add(settings);
            y -= gap;

            var quit = MenuButton(col, "Quit", "종료", y);
            quit.onClick.AddListener(Quit);
            buttons.Add(quit);

            // 위아래 방향키로 메뉴를 오간다
            for (int i = 0; i < buttons.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnUp = buttons[(i - 1 + buttons.Count) % buttons.Count];
                nav.selectOnDown = buttons[(i + 1) % buttons.Count];
                buttons[i].navigation = nav;
            }
            _firstButton = buttons[0];
            Select(_firstButton);
        }

        Button MenuButton(RectTransform col, string name, string label, float y, bool primary = false)
        {
            var b = UIKit.Button(col, name, label, new Vector2(360f, 66f), primary, fontSize: 28f);
            // 피벗을 가운데에 — 고르면 커지는 효과가 제자리에서 고르게 퍼진다 (왼쪽 위 기준이면 오른쪽 아래로 밀려 보인다)
            UIKit.Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(180f, y - 33f), new Vector2(360f, 66f), new Vector2(0.5f, 0.5f));
            return b;
        }

        string SaveSummary(PlayerProgress p)
        {
            var db = _game.Database;
            string zone = db != null && db.GetZone(p.DeepestZoneReached) != null ? db.GetZone(p.DeepestZoneReached).displayName : "어항";
            int slots = db != null ? db.PurchasableSlotCount : 0;
            string cleared = p.clearedMaps != null && p.clearedMaps.Count > 0 ? "  ·  <color=#FFD666>보스 처치</color>" : "";
            return $"찍은 칸 {p.totalNodesPurchased}{(slots > 0 ? $"/{slots}" : "")}  ·  {zone}까지  ·  {p.totalRuns}판{cleared}";
        }

        void BuildFooter()
        {
            var ver = UIKit.Text(_root, "Version", $"v{Application.version}", 18f, new Color(0.5f, 0.66f, 0.68f), TextAlignmentOptions.Right);
            UIKit.Place(ver.rectTransform, new Vector2(1f, 0f), new Vector2(-40f, 30f), new Vector2(300f, 28f), new Vector2(1f, 0f));
            var hint = UIKit.Text(_root, "Hint", "↑ ↓ 로 고르고 Enter  ·  마우스로 클릭", 18f, new Color(0.5f, 0.66f, 0.68f), TextAlignmentOptions.Left);
            UIKit.Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(150f, 30f), new Vector2(700f, 28f), new Vector2(0f, 0f));
        }

        // ── 새 게임 확인 ─────────────────────────────────────────
        void AskNewGame()
        {
            if (_confirm != null) return;
            var dim = UIKit.Image(_root, "ConfirmDim", null, new Color(0f, 0.02f, 0.04f, 0.72f), raycast: true);
            UIKit.Stretch(dim.rectTransform);
            _confirm = dim.gameObject;

            var box = UIKit.Image(dim.rectTransform, "Box", LabArt.Panel, LabStyle.Fill, raycast: true);
            LabStyle.Panel(box, corners: true, fill: new Color(0.03f, 0.10f, 0.13f, 0.97f));
            var b = UIKit.Place(box.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 320f));

            var t = UIKit.Text(b, "Title", "새 게임을 시작할까요?", 34f, UIKit.Ink, TextAlignmentOptions.Center, FontStyles.Bold);
            UIKit.Place(t.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(640f, 48f));
            var body = UIKit.Text(b, "Body", "지금까지의 진행도(재화 · 스킬트리 · 도감 · 구역)가 모두 사라집니다.\n설정은 그대로 남습니다.",
                                  21f, UIKit.InkDim, TextAlignmentOptions.Center);
            body.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(640f, 80f));

            var cancel = UIKit.Button(b, "Cancel", "취소", new Vector2(240f, 58f));
            UIKit.Place((RectTransform)cancel.transform, new Vector2(0.5f, 0f), new Vector2(-135f, 36f), new Vector2(240f, 58f));
            cancel.onClick.AddListener(CloseConfirm);

            var ok = UIKit.Button(b, "Confirm", "새로 시작", new Vector2(240f, 58f), primary: true);
            UIKit.Place((RectTransform)ok.transform, new Vector2(0.5f, 0f), new Vector2(135f, 36f), new Vector2(240f, 58f));
            ok.onClick.AddListener(() =>
            {
                _game.ResetProgress();
                Continue();
            });
            // 두 버튼끼리만 오가게 — 자동 탐색이면 어둡게 가린 뒤쪽 메뉴 버튼까지 골라진다
            cancel.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = ok, selectOnRight = ok };
            ok.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = cancel, selectOnRight = cancel };
            Select(cancel);   // 실수로 Enter를 눌러도 지워지지 않게 '취소'부터
        }

        void CloseConfirm()
        {
            if (_confirm != null) Destroy(_confirm);
            _confirm = null;
            Select(_firstButton);
        }

        // ── 이동 ────────────────────────────────────────────────
        void Continue()
        {
            if (_busy) return;
            _busy = true;
            StartCoroutine(FadeThen(() => _game.GoToMainMenu()));
        }

        void Quit()
        {
            if (_busy) return;
            _busy = true;
            StartCoroutine(FadeThen(() =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }));
        }

        IEnumerator FadeThen(System.Action then)
        {
            yield return FadeTo(1f, 0.35f);
            then();
        }

        IEnumerator FadeTo(float target, float seconds)
        {
            if (_fade == null) yield break;
            _fade.transform.SetAsLastSibling();   // 나중에 만든 확인 창보다도 위를 덮게
            _fade.raycastTarget = true;
            float start = _fade.color.a, t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                var c = _fade.color;
                c.a = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / seconds));
                _fade.color = c;
                yield return null;
            }
            var end = _fade.color; end.a = target; _fade.color = end;
            _fade.raycastTarget = target > 0.01f;
        }

        static void Select(Selectable s)
        {
            if (s != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(s.gameObject);
        }

        // ══════════════════════════════════════════════════════════
        void Update()
        {
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            if (_titleRt != null) _titleRt.anchoredPosition = new Vector2(0f, -40f + Mathf.Sin(t * 0.9f) * 6f);

            var size = _root != null ? _root.rect.size : new Vector2(1920f, 1080f);
            foreach (var s in _swimmers)
            {
                var p = s.Rt.anchoredPosition;
                p.x += s.Dir * s.Speed * dt;
                p.y = s.BaseY * size.y + Mathf.Sin(t * 1.1f + s.Phase) * s.Bob;
                float margin = s.Rt.sizeDelta.x;
                if (s.Dir > 0 && p.x > size.x + margin) { p.x = -margin; s.BaseY = s.Depth < 0.1f ? s.BaseY : Random.Range(0.08f, 0.92f); }
                if (s.Dir < 0 && p.x < -margin) { p.x = size.x + margin; s.BaseY = s.Depth < 0.1f ? s.BaseY : Random.Range(0.08f, 0.92f); }
                s.Rt.anchoredPosition = p;
            }

            // 확인 창에서 ESC = 취소
            if (_confirm != null && UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
                CloseConfirm();
        }
    }
}
