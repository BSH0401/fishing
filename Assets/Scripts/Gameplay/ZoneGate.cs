using System;
using FishGame.Utils;
using TMPro;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 존과 존 사이 통로를 막는 창살.
    ///
    /// 판 중에 먹어서 커진 "현재 크기"가 필요 크기 이상이 되면 열린다.
    /// 스킬트리로 올린 기본 크기가 아니라 현재 크기를 보는 게 핵심이다 —
    /// 그래야 한 판의 목표가 "이번엔 어디까지 내려가느냐"가 된다.
    ///
    /// 한 번 열리면 그 판 동안은 계속 열려 있다. 위아래로 자유롭게 오갈 수 있다.
    /// 다음 판이 시작되면 다시 잠긴 상태로 돌아온다.
    /// </summary>
    public class ZoneGate : MonoBehaviour
    {
        public int ZoneIndex { get; private set; }
        public float RequiredSize { get; private set; }
        public bool IsOpen { get; private set; }
        public string FromZoneName { get; private set; }
        public string ToZoneName { get; private set; }

        /// <summary>플레이어 크기가 필요치의 이 비율을 넘으면 "곧 열림" 안내를 띄운다.</summary>
        const float NearThreshold = 0.75f;

        public event Action<ZoneGate> OnOpened;

        BoxCollider2D _blocker;
        Transform _barsRoot;
        SpriteRenderer[] _bars;
        SpriteRenderer _glow;
        // TMP_Text(추상 기반 타입)에는 sortingOrder가 없다.
        // 월드 공간에 그리는 TextMeshPro여야 정렬 순서를 지정할 수 있다.
        TextMeshPro _label;
        Vector3 _barScale;

        float _halfWidth;
        float _openAnim;
        bool _wasNear;

        // ══════════════════════════════════════════════════════════
        //  생성
        // ══════════════════════════════════════════════════════════
        public static ZoneGate Create(Transform parent, Vector3 position, int zoneIndex,
                                      float requiredSize, float halfWidth,
                                      string fromName, string toName, TMP_FontAsset font)
        {
            var go = new GameObject($"ZoneGate_{zoneIndex}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            var gate = go.AddComponent<ZoneGate>();
            gate.Setup(zoneIndex, requiredSize, halfWidth, fromName, toName, font);
            return gate;
        }

        public void Setup(int zoneIndex, float requiredSize, float halfWidth,
                          string fromName, string toName, TMP_FontAsset font = null)
        {
            ZoneIndex = zoneIndex;
            RequiredSize = Mathf.Max(0.01f, requiredSize);
            _halfWidth = Mathf.Max(0.5f, halfWidth);
            FromZoneName = fromName;
            ToZoneName = toName;
            IsOpen = false;
            _openAnim = 0f;

            BuildVisuals(font);
            BuildCollider();
        }

        void BuildCollider()
        {
            if (_blocker == null) _blocker = gameObject.AddComponent<BoxCollider2D>();
            // 통로 입구를 가로로 완전히 막는다. 두께는 플레이어가 뚫지 못할 만큼.
            _blocker.size = new Vector2(_halfWidth * 2f, Mathf.Max(0.8f, _halfWidth * 0.22f));
            _blocker.offset = Vector2.zero;
            _blocker.isTrigger = false;
            _blocker.enabled = true;

            if (GetComponent<Rigidbody2D>() == null)
                gameObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;

            int wall = LayerMask.NameToLayer("Wall");
            if (wall >= 0) gameObject.layer = wall;
        }

        void BuildVisuals(TMP_FontAsset font)
        {
            if (_barsRoot != null) Destroy(_barsRoot.gameObject);

            var rootGo = new GameObject("Bars");
            _barsRoot = rootGo.transform;
            _barsRoot.SetParent(transform, false);

            // 창살 — 통로 폭에 맞춰 개수를 정한다
            float barThickness = Mathf.Max(0.3f, _halfWidth * 0.11f);
            int count = Mathf.Clamp(Mathf.RoundToInt(_halfWidth * 1.2f), 4, 14);
            float barHeight = Mathf.Max(1.2f, _halfWidth * 0.5f);
            _barScale = new Vector3(barThickness, barHeight, 1f);

            _bars = new SpriteRenderer[count];
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : (float)i / (count - 1);
                float x = Mathf.Lerp(-_halfWidth + barThickness, _halfWidth - barThickness, t);

                var sr = Prims.NewSprite(_barsRoot, $"Bar{i}", Prims.White,
                                         new Color(0.14f, 0.15f, 0.18f, 1f), -60);
                sr.transform.localPosition = new Vector3(x, 0f, 0f);
                sr.transform.localScale = _barScale;
                _bars[i] = sr;
            }

            // 잠김 안내 — 필요 크기를 숫자로 보여준다
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = new Vector3(0f, barHeight * 0.9f, 0f);

            _label = labelGo.AddComponent<TextMeshPro>();
            if (font != null) _label.font = font;
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = Mathf.Max(3f, _halfWidth * 0.9f);
            _label.color = new Color(1f, 0.85f, 0.45f, 0.95f);
            _label.sortingOrder = -55;
            _label.text = LockedText();

            var rt = _label.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(_halfWidth * 4f, _halfWidth * 1.4f);

            // 열렸을 때 아래로 빨려드는 느낌을 줄 발광
            _glow = Prims.NewSprite(transform, "Glow", Prims.SoftDot,
                                    new Color(0.6f, 0.9f, 1f, 0f), -70);
            _glow.transform.localScale = Vector3.one * (_halfWidth * 3f);
        }

        string LockedText() => $"크기 {RequiredSize:0.#} 필요";

        // ══════════════════════════════════════════════════════════
        //  판정
        // ══════════════════════════════════════════════════════════
        void Update()
        {
            if (IsOpen) { AnimateOpen(); return; }

            var run = RunManager.Instance;
            if (run == null || !run.IsRunning || run.IsPaused) return;

            float size = run.CurrentPlayerSize;
            if (size >= RequiredSize) { Open(); return; }

            // 근접 안내 — 조금만 더 먹으면 열린다는 신호
            bool near = size >= RequiredSize * NearThreshold;
            if (near != _wasNear)
            {
                _wasNear = near;
                if (_label != null)
                    _label.color = near
                        ? new Color(1f, 0.95f, 0.55f, 1f)
                        : new Color(1f, 0.85f, 0.45f, 0.95f);
            }

            if (_label != null && near)
            {
                float pulse = 0.85f + Mathf.PingPong(Time.time * 1.6f, 0.3f);
                _label.transform.localScale = Vector3.one * pulse;
                _label.text = $"크기 {size:0.#} / {RequiredSize:0.#}";
            }
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            if (_blocker != null) _blocker.enabled = false;
            if (_label != null) _label.text = $"{ToZoneName} →";

            // 지금 있는 구역의 통로일 때만 화면을 흔든다 (개발자 모드 "통로 모두 열기" 등에서 여러 번 흔들리지 않게)
            var run = RunManager.Instance;
            if (run == null || run.CurrentZoneIndex == ZoneIndex) Juice.Hit(0.04f, 0.35f);
            OnOpened?.Invoke(this);
        }

        /// <summary>새 판이 시작될 때 다시 잠근다.</summary>
        public void Relock()
        {
            IsOpen = false;
            _openAnim = 0f;
            _wasNear = false;
            if (_blocker != null) _blocker.enabled = true;
            if (_label != null)
            {
                _label.text = LockedText();
                _label.color = new Color(1f, 0.85f, 0.45f, 0.95f);
                _label.transform.localScale = Vector3.one;
            }
            if (_bars != null)
            {
                foreach (var b in _bars)
                {
                    if (b == null) continue;
                    b.gameObject.SetActive(true);
                    b.transform.localScale = _barScale;             // 열림 연출로 찌그러진 걸 되돌린다
                    b.color = new Color(0.14f, 0.15f, 0.18f, 1f);
                }
            }
            if (_glow != null)
            {
                var c = _glow.color;
                _glow.color = new Color(c.r, c.g, c.b, 0f);
            }
        }

        /// <summary>창살이 양옆으로 물러나며 열리는 연출.</summary>
        void AnimateOpen()
        {
            if (_openAnim >= 1f) return;
            _openAnim = Mathf.Min(1f, _openAnim + Time.unscaledDeltaTime * 2.4f);
            float k = Mathf.SmoothStep(0f, 1f, _openAnim);

            if (_bars != null)
            {
                for (int i = 0; i < _bars.Length; i++)
                {
                    var b = _bars[i];
                    if (b == null) continue;
                    var s = b.transform.localScale;
                    b.transform.localScale = new Vector3(s.x, Mathf.Lerp(s.y, 0.02f, k * 0.35f), 1f);
                    var c = b.color;
                    b.color = new Color(c.r, c.g, c.b, 1f - k);
                    if (_openAnim >= 1f) b.gameObject.SetActive(false);
                }
            }

            if (_glow != null)
            {
                float a = Mathf.Sin(k * Mathf.PI) * 0.5f;
                var c = _glow.color;
                _glow.color = new Color(c.r, c.g, c.b, a);
            }
        }
    }
}
