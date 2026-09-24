using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>
    /// 스킬트리 창 뒤의 "수중 실험실" 배경. 스크롤 영역(뷰포트)에 붙이면 실행 중에 알아서 만든다.
    ///
    ///   바다색 그라디언트 → 흔들리는 빛줄기 → 떠오르는 물방울 · 가라앉는 부유물 → (트리) → 가장자리 비네트
    ///
    /// 전부 뷰포트 기준이라 트리를 끌거나 확대해도 제자리에 있다 — 트리만 움직이니 깊이감이 생긴다.
    /// 클릭은 막지 않는다 (raycastTarget 끔).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class UnderwaterBackdrop : MonoBehaviour
    {
        [SerializeField] int rayCount = 5;
        [SerializeField] int bubbleCount = 18;
        [SerializeField] int snowCount = 26;
        [SerializeField] Color rayColor = new Color(0.55f, 0.95f, 0.92f, 1f);
        [SerializeField] Color bubbleColor = new Color(0.75f, 0.95f, 1f, 0.5f);
        [SerializeField] Color snowColor = new Color(0.8f, 0.95f, 0.9f, 0.18f);
        [SerializeField] float vignetteAlpha = 0.6f;

        RectTransform _rt;
        bool _built;

        class Ray { public RectTransform Rt; public Image Img; public float BaseX, Phase, Speed, Alpha, Tilt; }
        class Mote { public RectTransform Rt; public Image Img; public float Speed, Wobble, Phase, BaseAlpha; public bool Rising; }

        readonly List<Ray> _rays = new List<Ray>();
        readonly List<Mote> _motes = new List<Mote>();

        void Start() => Build();

        public void Build()
        {
            if (_built) return;
            _built = true;
            _rt = (RectTransform)transform;

            // 맨 뒤부터 순서대로 끼워 넣는다 (0번 = 가장 뒤)
            int order = 0;
            var backdrop = NewImage("Backdrop", LabArt.Backdrop, Color.white);
            Stretch(backdrop.rectTransform);
            backdrop.rectTransform.SetSiblingIndex(order++);

            var rayRoot = NewRect("Rays");
            Stretch(rayRoot);
            rayRoot.SetSiblingIndex(order++);
            for (int i = 0; i < rayCount; i++)
            {
                var img = NewImage($"Ray{i}", LabArt.Ray, rayColor, rayRoot);
                var r = img.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
                r.pivot = new Vector2(0.5f, 1f);
                _rays.Add(new Ray
                {
                    Rt = r, Img = img,
                    BaseX = (i + 0.5f) / rayCount + Random.Range(-0.08f, 0.08f),
                    Phase = Random.value * 10f,
                    Speed = Random.Range(0.12f, 0.25f),
                    Alpha = Random.Range(0.05f, 0.11f),
                    Tilt = Random.Range(12f, 22f),
                });
            }

            var moteRoot = NewRect("Motes");
            Stretch(moteRoot);
            moteRoot.SetSiblingIndex(order++);
            for (int i = 0; i < bubbleCount; i++) _motes.Add(NewMote(moteRoot, true));
            for (int i = 0; i < snowCount; i++) _motes.Add(NewMote(moteRoot, false));

            // 비네트는 트리 위에 (맨 앞)
            var vignette = NewImage("Vignette", LabArt.Vignette, new Color(0f, 0.02f, 0.04f, vignetteAlpha));
            Stretch(vignette.rectTransform);
            vignette.rectTransform.SetAsLastSibling();

            // 처음부터 화면 곳곳에 흩어 둔다
            var size = _rt.rect.size;
            foreach (var m in _motes) Respawn(m, size, true);
        }

        Mote NewMote(RectTransform parent, bool bubble)
        {
            var img = NewImage(bubble ? "Bubble" : "Snow", bubble ? LabArt.Bubble : LabArt.SoftDot,
                               bubble ? bubbleColor : snowColor, parent);
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            float s = bubble ? Random.Range(6f, 18f) : Random.Range(3f, 7f);
            r.sizeDelta = new Vector2(s, s);
            return new Mote
            {
                Rt = r, Img = img, Rising = bubble,
                Speed = bubble ? Random.Range(22f, 55f) : Random.Range(4f, 10f),
                Wobble = bubble ? Random.Range(4f, 10f) : Random.Range(6f, 16f),
                Phase = Random.value * 10f,
                BaseAlpha = (bubble ? bubbleColor.a : snowColor.a) * Random.Range(0.6f, 1.2f),
            };
        }

        void Respawn(Mote m, Vector2 size, bool anywhere)
        {
            float x = Random.Range(0f, size.x);
            float y = anywhere ? Random.Range(0f, size.y) : (m.Rising ? -20f : size.y + 20f);
            m.Rt.anchoredPosition = new Vector2(x, y);
            m.Phase = Random.value * 10f;
        }

        void Update()
        {
            if (!_built) return;
            float t = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var size = _rt.rect.size;
            if (size.x < 2f) return;

            foreach (var r in _rays)
            {
                float sway = Mathf.Sin(t * r.Speed + r.Phase);
                r.Rt.anchoredPosition = new Vector2((r.BaseX + sway * 0.04f) * size.x, 30f);
                r.Rt.sizeDelta = new Vector2(size.x * 0.11f * (1f + 0.3f * Mathf.Sin(t * r.Speed * 1.7f + r.Phase)),
                                             size.y * 1.35f);
                r.Rt.localRotation = Quaternion.Euler(0f, 0f, r.Tilt + sway * 3f);
                var c = r.Img.color;
                c.a = r.Alpha * (0.65f + 0.35f * Mathf.Sin(t * r.Speed * 2.3f + r.Phase * 2f));
                r.Img.color = c;
            }

            foreach (var m in _motes)
            {
                var p = m.Rt.anchoredPosition;
                p.y += (m.Rising ? m.Speed : -m.Speed) * dt;
                p.x += Mathf.Sin(t * 1.3f + m.Phase) * m.Wobble * dt;
                m.Rt.anchoredPosition = p;
                if (m.Rising ? p.y > size.y + 24f : p.y < -24f) Respawn(m, size, false);

                // 위로 갈수록(수면 쪽) 조금 더 밝게
                var c = m.Img.color;
                c.a = m.BaseAlpha * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(p.y / size.y));
                m.Img.color = c;
            }
        }

        // ── 헬퍼 ────────────────────────────────────────────────
        RectTransform NewRect(string name, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : transform, false);
            return (RectTransform)go.transform;
        }

        Image NewImage(string name, Sprite sprite, Color color, Transform parent = null)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }
    }
}
