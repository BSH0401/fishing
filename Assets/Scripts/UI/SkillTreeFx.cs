using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>
    /// 스킬트리의 움직이는 연출.
    ///
    ///   흐르는 빛   찍은 칸끼리 이어진 선 위를 빛 알갱이가 오간다 (트리가 "살아 있다"는 느낌)
    ///   구매 폭발   찍는 순간 충격파 고리 두 겹 + 도형 조각이 튀고 + 쓴 재화가 떠오른다
    ///   새로 열림   구매로 열린 이웃 칸까지 빛이 달려가서 고리가 번진다
    ///   거절        빨간 고리 하나
    ///
    /// SkillTreeUI가 만들고, 두 층을 넘겨준다:
    ///   lineLayer  선 위 · 칸 아래 (빛 알갱이는 칸 속으로 사라져야 자연스럽다)
    ///   topLayer   칸 위 (폭발·글자)
    /// 모든 오브젝트는 풀에서 재사용한다.
    /// </summary>
    public class SkillTreeFx : MonoBehaviour
    {
        RectTransform _lineLayer, _topLayer;
        TMP_Text _textPrefab;

        // ── 흐르는 빛 ───────────────────────────────────────────
        public struct Segment { public Vector2 A, B; }
        readonly List<Segment> _litSegments = new List<Segment>();

        class Pulse
        {
            public Image Img; public Vector2 A, B; public float T, Speed, Size; public Color Color; public bool Active;
        }
        readonly List<Pulse> _pulses = new List<Pulse>();
        float _spawnTimer;
        const int MaxAmbientPulses = 22;

        // ── 일회성 효과 ─────────────────────────────────────────
        class Burst
        {
            public Image Img; public RectTransform Rt; public float Age, Life, Delay;
            public Vector2 Pos, Vel; public float Drag, Spin, Size0, Size1; public Color Color; public bool Active;
            public bool Ring;
        }
        readonly List<Burst> _bursts = new List<Burst>();

        class FloatText { public TMP_Text Text; public float Age; public Vector2 Pos; public bool Active; }
        readonly List<FloatText> _texts = new List<FloatText>();

        public void Init(RectTransform lineLayer, RectTransform topLayer, TMP_Text textPrefab)
        {
            _lineLayer = lineLayer;
            _topLayer = topLayer;
            _textPrefab = textPrefab;
        }

        /// <summary>양쪽 다 찍힌 선들 (빛 알갱이가 다닐 길).</summary>
        public void SetLitSegments(List<Segment> segments)
        {
            _litSegments.Clear();
            _litSegments.AddRange(segments);
        }

        // ══════════════════════════════════════════════════════════
        //  공개 연출
        // ══════════════════════════════════════════════════════════
        public void PlayPurchase(Vector2 pos, Color color, Sprite shard, float nodeSize, string costLabel)
        {
            Color bright = Color.Lerp(color, Color.white, 0.35f);
            SpawnRing(pos, bright, nodeSize * 0.8f, nodeSize * 3.4f, 0.55f, 0f);
            SpawnRing(pos, new Color(1f, 1f, 1f, 0.9f), nodeSize * 0.6f, nodeSize * 2.2f, 0.32f, 0f);
            SpawnGlow(pos, bright, nodeSize * 2.6f, 0.35f);

            int count = 14;
            for (int i = 0; i < count; i++)
            {
                float ang = (i + Random.value * 0.6f) / count * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var b = Get(_topLayer);
                b.Img.sprite = i % 2 == 0 && shard != null ? shard : LabArt.SoftDot;
                b.Ring = false;
                b.Pos = pos + dir * nodeSize * 0.3f;
                b.Vel = dir * Random.Range(260f, 520f);
                b.Drag = 4.2f;
                b.Spin = Random.Range(-540f, 540f);
                b.Size0 = Random.Range(10f, 18f);
                b.Size1 = 0f;
                b.Life = Random.Range(0.5f, 0.8f);
                b.Color = i % 2 == 0 ? color : bright;
            }

            if (!string.IsNullOrEmpty(costLabel)) SpawnText(pos + new Vector2(0f, nodeSize * 0.7f), costLabel);
        }

        /// <summary>구매로 새로 열린 칸: from에서 빛이 달려가 도착하면 고리가 번진다.</summary>
        public void PlayUnlockTravel(Vector2 from, Vector2 to, float delay, float nodeSize)
        {
            var cyan = new Color(0.55f, 1f, 0.92f, 1f);
            float travel = Mathf.Max(0.12f, Vector2.Distance(from, to) / 900f);
            SpawnPulse(from, to, cyan, 26f, 1f / travel, delay);
            SpawnRing(to, cyan, nodeSize * 0.8f, nodeSize * 2.4f, 0.45f, delay + travel);
        }

        public void PlayDenied(Vector2 pos, float nodeSize)
        {
            SpawnRing(pos, new Color(1f, 0.35f, 0.3f, 0.9f), nodeSize * 0.9f, nodeSize * 1.7f, 0.3f, 0f);
        }

        // ══════════════════════════════════════════════════════════
        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            UpdateAmbient(dt);
            UpdatePulses(dt);
            UpdateBursts(dt);
            UpdateTexts(dt);
        }

        void UpdateAmbient(float dt)
        {
            if (_litSegments.Count == 0 || _lineLayer == null) return;
            _spawnTimer -= dt;
            if (_spawnTimer > 0f) return;
            // 찍은 선이 많을수록 자주 — 트리가 커질수록 더 반짝인다
            _spawnTimer = Mathf.Lerp(0.5f, 0.07f, Mathf.Clamp01(_litSegments.Count / 120f));

            int active = 0;
            foreach (var p in _pulses) if (p.Active) active++;
            if (active >= MaxAmbientPulses) return;

            var seg = _litSegments[Random.Range(0, _litSegments.Count)];
            bool flip = Random.value < 0.5f;
            float len = Vector2.Distance(seg.A, seg.B);
            SpawnPulse(flip ? seg.B : seg.A, flip ? seg.A : seg.B,
                       new Color(0.6f, 1f, 0.95f, 0.85f), Random.Range(12f, 18f),
                       Random.Range(180f, 300f) / Mathf.Max(1f, len), 0f);
        }

        void SpawnPulse(Vector2 a, Vector2 b, Color color, float size, float speed, float delay)
        {
            if (_lineLayer == null) return;
            Pulse p = null;
            foreach (var q in _pulses) if (!q.Active) { p = q; break; }
            if (p == null)
            {
                p = new Pulse { Img = NewImage(_lineLayer, "Pulse", LabArt.SoftDot) };
                _pulses.Add(p);
            }
            p.A = a; p.B = b; p.Color = color; p.Size = size; p.Speed = speed;
            p.T = -delay * speed;     // 지연 시간만큼 음수에서 출발
            p.Active = true;
            p.Img.gameObject.SetActive(true);
            p.Img.color = new Color(color.r, color.g, color.b, 0f);
            p.Img.rectTransform.sizeDelta = new Vector2(size, size);
            p.Img.rectTransform.anchoredPosition = a;
        }

        void UpdatePulses(float dt)
        {
            foreach (var p in _pulses)
            {
                if (!p.Active) continue;
                p.T += p.Speed * dt;
                if (p.T >= 1f) { p.Active = false; p.Img.gameObject.SetActive(false); continue; }
                if (p.T < 0f) continue;
                p.Img.rectTransform.anchoredPosition = Vector2.Lerp(p.A, p.B, p.T);
                // 양 끝에서 부드럽게 나타나고 사라진다
                float a = Mathf.Sin(p.T * Mathf.PI);
                p.Img.color = new Color(p.Color.r, p.Color.g, p.Color.b, p.Color.a * a);
            }
        }

        void SpawnRing(Vector2 pos, Color color, float from, float to, float life, float delay)
        {
            var b = Get(_topLayer);
            b.Img.sprite = LabArt.Ring;
            b.Ring = true;
            b.Pos = pos; b.Vel = Vector2.zero; b.Drag = 0f; b.Spin = 0f;
            b.Size0 = from; b.Size1 = to; b.Life = life; b.Delay = delay; b.Color = color;
        }

        void SpawnGlow(Vector2 pos, Color color, float size, float life)
        {
            var b = Get(_topLayer);
            b.Img.sprite = LabArt.SoftDot;
            b.Ring = true;   // 크기 보간을 쓴다
            b.Pos = pos; b.Vel = Vector2.zero; b.Drag = 0f; b.Spin = 0f;
            b.Size0 = size * 0.6f; b.Size1 = size; b.Life = life; b.Color = new Color(color.r, color.g, color.b, 0.7f);
        }

        Burst Get(RectTransform layer)
        {
            Burst b = null;
            foreach (var q in _bursts) if (!q.Active) { b = q; break; }
            if (b == null)
            {
                var img = NewImage(layer, "Burst", LabArt.SoftDot);
                b = new Burst { Img = img, Rt = img.rectTransform };
                _bursts.Add(b);
            }
            b.Active = true; b.Age = 0f; b.Delay = 0f;
            b.Rt.localRotation = Quaternion.identity;
            b.Img.gameObject.SetActive(true);
            b.Img.color = new Color(1f, 1f, 1f, 0f);
            return b;
        }

        void UpdateBursts(float dt)
        {
            foreach (var b in _bursts)
            {
                if (!b.Active) continue;
                if (b.Delay > 0f) { b.Delay -= dt; continue; }
                b.Age += dt;
                float k = b.Age / b.Life;
                if (k >= 1f) { b.Active = false; b.Img.gameObject.SetActive(false); continue; }

                if (b.Ring)
                {
                    float e = 1f - (1f - k) * (1f - k) * (1f - k);           // 빠르게 퍼졌다가 느려진다
                    float s = Mathf.Lerp(b.Size0, b.Size1, e);
                    b.Rt.sizeDelta = new Vector2(s, s);
                    b.Rt.anchoredPosition = b.Pos;
                    b.Img.color = new Color(b.Color.r, b.Color.g, b.Color.b, b.Color.a * (1f - k));
                }
                else
                {
                    b.Vel *= Mathf.Exp(-b.Drag * dt);
                    b.Pos += b.Vel * dt;
                    b.Rt.anchoredPosition = b.Pos;
                    b.Rt.localRotation = Quaternion.Euler(0f, 0f, b.Spin * b.Age);
                    float s = Mathf.Lerp(b.Size0, b.Size1, k * k);
                    b.Rt.sizeDelta = new Vector2(s, s);
                    b.Img.color = new Color(b.Color.r, b.Color.g, b.Color.b, 1f - k * k);
                }
            }
        }

        void SpawnText(Vector2 pos, string label)
        {
            if (_textPrefab == null || _topLayer == null) return;
            FloatText f = null;
            foreach (var q in _texts) if (!q.Active) { f = q; break; }
            if (f == null)
            {
                var t = Instantiate(_textPrefab, _topLayer);
                t.raycastTarget = false;
                t.fontStyle = FontStyles.Bold;
                t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.rectTransform.sizeDelta = new Vector2(200f, 40f);
                f = new FloatText { Text = t };
                _texts.Add(f);
            }
            f.Active = true; f.Age = 0f; f.Pos = pos;
            f.Text.text = label;
            f.Text.fontSize = 26f;
            f.Text.color = new Color(1f, 0.86f, 0.4f, 1f);
            f.Text.gameObject.SetActive(true);
            f.Text.transform.SetAsLastSibling();
        }

        void UpdateTexts(float dt)
        {
            foreach (var f in _texts)
            {
                if (!f.Active) continue;
                f.Age += dt;
                const float life = 1.0f;
                float k = f.Age / life;
                if (k >= 1f) { f.Active = false; f.Text.gameObject.SetActive(false); continue; }
                f.Text.rectTransform.anchoredPosition = f.Pos + new Vector2(0f, 70f * (1f - (1f - k) * (1f - k)));
                float pop = k < 0.15f ? Mathf.Lerp(0.6f, 1.15f, k / 0.15f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((k - 0.15f) / 0.2f));
                f.Text.transform.localScale = Vector3.one * pop;
                var c = f.Text.color;
                c.a = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
                f.Text.color = c;
            }
        }

        static Image NewImage(RectTransform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }
    }
}
