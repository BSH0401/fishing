using System.Collections.Generic;
using FishGame.UI;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 인게임 물속 분위기 — 타이틀 · 스킬트리 배경(UnderwaterBackdrop)을 월드 카메라용으로 옮긴 것.
    ///
    ///   빛줄기   위에서 비스듬히 내려오는 옅은 빛. 깊이 내려갈수록 약해진다
    ///   물방울   아래에서 올라오는 거품 + 천천히 가라앉는 부유물
    ///   비네트   화면 가장자리를 어둡게 — 메인 화면과 같은 깊은 물 느낌
    ///
    /// 카메라 자식으로 붙어 화면 크기(줌)에 맞춰 늘어난다. 물 메시(-200)와 격자 위,
    /// 벽 윤곽선 · 물고기 아래에 그린다. 비네트만 물고기 위.
    /// 입자는 카메라가 움직이면 반대로 살짝 밀려 원근감이 생긴다.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorldAmbience : MonoBehaviour
    {
        [SerializeField] int rayCount = 6;
        [SerializeField] int bubbleCount = 16;
        [SerializeField] int snowCount = 28;
        [SerializeField] Color rayColor = new Color(0.55f, 0.95f, 0.92f, 1f);
        [SerializeField] Color bubbleColor = new Color(0.75f, 0.95f, 1f, 0.45f);
        [SerializeField] Color snowColor = new Color(0.8f, 0.95f, 0.9f, 0.16f);
        [SerializeField] float vignetteAlpha = 0.5f;
        [Tooltip("카메라가 움직일 때 입자가 따라 밀리는 정도 (0 = 화면에 붙음, 1 = 월드에 고정)")]
        [Range(0f, 1f)] [SerializeField] float particleParallax = 0.35f;

        const int RayOrder = -190, MoteOrder = -185, VignetteOrder = 300;
        // 구역별 빛 세기 (어항 → 바다). 구역이 더 있으면 마지막 값을 쓴다
        static readonly float[] ZoneLight = { 1f, 0.72f, 0.6f, 0.4f };

        class Ray { public SpriteRenderer Sr; public float BaseX, Phase, Speed, Alpha, Tilt; }
        class Mote { public SpriteRenderer Sr; public Vector2 Pos; public float Size, Speed, Wobble, Phase, BaseAlpha; public bool Rising; }

        readonly List<Ray> _rays = new List<Ray>();
        readonly List<Mote> _motes = new List<Mote>();
        Camera _cam;
        SpriteRenderer _vignette;
        Vector3 _lastCamPos;
        float _light = 1f;
        bool _built;

        /// <summary>카메라에 분위기 층을 붙인다 (이미 있으면 그대로).</summary>
        public static WorldAmbience Ensure(Camera cam)
        {
            if (cam == null) return null;
            var a = cam.GetComponentInChildren<WorldAmbience>();
            if (a != null) return a;
            var go = new GameObject("WorldAmbience");
            go.transform.SetParent(cam.transform, false);
            return go.AddComponent<WorldAmbience>();
        }

        void Awake()
        {
            _cam = GetComponentInParent<Camera>();
            Build();
        }

        void Build()
        {
            if (_built || _cam == null) return;
            _built = true;
            // 카메라 앞(z=+10)이 월드 z=0 근처다
            transform.localPosition = new Vector3(0f, 0f, 10f);
            transform.localRotation = Quaternion.identity;

            for (int i = 0; i < rayCount; i++)
            {
                _rays.Add(new Ray
                {
                    Sr = NewSprite($"Ray{i}", LabArt.Ray, rayColor, RayOrder),
                    BaseX = (i + 0.5f) / rayCount + Random.Range(-0.07f, 0.07f),
                    Phase = Random.value * 10f,
                    Speed = Random.Range(0.12f, 0.25f),
                    Alpha = Random.Range(0.05f, 0.1f),
                    Tilt = Random.Range(12f, 22f),
                });
            }

            for (int i = 0; i < bubbleCount; i++) _motes.Add(NewMote(true));
            for (int i = 0; i < snowCount; i++) _motes.Add(NewMote(false));

            _vignette = NewSprite("Vignette", LabArt.Vignette, new Color(0f, 0.02f, 0.04f, vignetteAlpha), VignetteOrder);

            Vector2 view = ViewSize();
            foreach (var m in _motes) Respawn(m, view, anywhere: true);
            _lastCamPos = _cam.transform.position;
        }

        Mote NewMote(bool bubble)
        {
            return new Mote
            {
                Sr = NewSprite(bubble ? "Bubble" : "Snow", bubble ? LabArt.Bubble : LabArt.SoftDot,
                               bubble ? bubbleColor : snowColor, MoteOrder),
                Rising = bubble,
                // 크기 · 속도는 화면 높이 비율 — 줌이 바뀌어도 화면에서 같은 크기로 보인다
                Size = bubble ? Random.Range(0.007f, 0.018f) : Random.Range(0.003f, 0.007f),
                Speed = bubble ? Random.Range(0.025f, 0.06f) : Random.Range(0.004f, 0.011f),
                Wobble = bubble ? Random.Range(0.004f, 0.01f) : Random.Range(0.006f, 0.015f),
                Phase = Random.value * 10f,
                BaseAlpha = (bubble ? bubbleColor.a : snowColor.a) * Random.Range(0.6f, 1.2f),
            };
        }

        SpriteRenderer NewSprite(string name, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        Vector2 ViewSize()
        {
            float h = _cam.orthographicSize * 2f;
            return new Vector2(h * _cam.aspect, h);
        }

        /// <summary>화면 기준 좌표(0..1)로 다시 뿌린다.</summary>
        void Respawn(Mote m, Vector2 view, bool anywhere)
        {
            float x = Random.value;
            float y = anywhere ? Random.value : (m.Rising ? -0.03f : 1.03f);
            m.Pos = new Vector2(x, y);
            m.Phase = Random.value * 10f;
        }

        static void FitSprite(SpriteRenderer sr, float width, float height)
        {
            var b = sr.sprite != null ? sr.sprite.bounds.size : Vector3.one;
            sr.transform.localScale = new Vector3(width / Mathf.Max(1e-4f, b.x), height / Mathf.Max(1e-4f, b.y), 1f);
        }

        void LateUpdate()
        {
            if (!_built || _cam == null) return;
            float t = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            Vector2 view = ViewSize();
            if (view.y < 0.01f) return;

            UpdateLight(dt);

            // ── 빛줄기: 화면 위쪽 가장자리에서 비스듬히 ──
            foreach (var r in _rays)
            {
                float sway = Mathf.Sin(t * r.Speed + r.Phase);
                float x = (r.BaseX + sway * 0.04f - 0.5f) * view.x;
                r.Sr.transform.localPosition = new Vector3(x, view.y * 0.5f + view.y * 0.03f, 0f);
                r.Sr.transform.localRotation = Quaternion.Euler(0f, 0f, r.Tilt + sway * 3f);
                float w = view.x * 0.11f * (1f + 0.3f * Mathf.Sin(t * r.Speed * 1.7f + r.Phase));
                FitSprite(r.Sr, w, view.y * 1.35f);
                var c = r.Sr.color;
                c.a = r.Alpha * _light * (0.65f + 0.35f * Mathf.Sin(t * r.Speed * 2.3f + r.Phase * 2f));
                r.Sr.color = c;
            }

            // ── 물방울 · 부유물: 카메라가 움직이면 반대로 조금 밀린다 ──
            Vector3 camPos = _cam.transform.position;
            Vector2 camDelta = (Vector2)(camPos - _lastCamPos);
            _lastCamPos = camPos;
            // 순간이동(판 시작 · 구역 이동)이면 원근 효과를 건너뛴다
            if (camDelta.sqrMagnitude > view.y * view.y) camDelta = Vector2.zero;
            Vector2 shift = new Vector2(camDelta.x / view.x, camDelta.y / view.y) * particleParallax;

            foreach (var m in _motes)
            {
                m.Pos.y += (m.Rising ? m.Speed : -m.Speed) * dt;
                m.Pos.x += Mathf.Sin(t * 1.3f + m.Phase) * m.Wobble * dt;
                m.Pos -= shift;

                if (m.Rising ? m.Pos.y > 1.04f : m.Pos.y < -0.04f) Respawn(m, view, anywhere: false);
                // 옆으로 밀려 나가면 반대편에서 다시 들어온다
                if (m.Pos.x < -0.05f) m.Pos.x += 1.1f;
                else if (m.Pos.x > 1.05f) m.Pos.x -= 1.1f;
                if (!m.Rising && m.Pos.y > 1.05f) m.Pos.y -= 1.1f;
                if (m.Rising && m.Pos.y < -0.05f) m.Pos.y += 1.1f;

                m.Sr.transform.localPosition = new Vector3((m.Pos.x - 0.5f) * view.x, (m.Pos.y - 0.5f) * view.y, 0f);
                float s = m.Size * view.y;
                FitSprite(m.Sr, s, s);

                var c = m.Sr.color;
                // 화면 위쪽(수면 쪽)일수록 조금 더 밝게, 깊을수록 전체가 흐리게
                c.a = m.BaseAlpha * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(m.Pos.y)) * Mathf.Lerp(0.55f, 1f, _light);
                m.Sr.color = c;
            }

            // ── 비네트: 화면 전체 ──
            _vignette.transform.localPosition = Vector3.zero;
            FitSprite(_vignette, view.x * 1.02f, view.y * 1.02f);
        }

        /// <summary>카메라가 있는 깊이에 따라 빛 세기를 부드럽게 바꾼다.</summary>
        void UpdateLight(float dt)
        {
            float target = 1f;
            var run = RunManager.Instance;
            var layout = run != null ? run.Layout : null;
            if (layout != null && layout.ZoneCount > 0)
            {
                float y = _cam.transform.position.y;
                int zi = Mathf.Clamp(layout.ZoneIndexAt(y), 0, layout.ZoneCount - 1);
                float zoneLight = ZoneLight[Mathf.Min(zi, ZoneLight.Length - 1)];
                target = zoneLight * Mathf.Lerp(1f, 0.8f, layout.DepthFactorInZone(y));
            }
            _light = Mathf.Lerp(_light, target, 1f - Mathf.Exp(-2f * dt));
        }
    }
}
