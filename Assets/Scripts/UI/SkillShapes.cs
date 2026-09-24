using System.Collections.Generic;
using FishGame.Data;
using UnityEngine;

namespace FishGame.UI
{
    /// <summary>
    /// 스킬트리 칸의 도형 스프라이트. 기획 도면(draw.io)의 범례를 그대로 따른다.
    ///
    ///   □ 커다란 배터리      ○ 덧붙인 장갑       ◇ 치아 교정         ▱ 카메라 장착
    ///   △ 물고기 전지        ⏢ 위액 산성도 증가  〰 부스터 거리 강화  ◸ 청소기 범위 강화
    ///   D 비늘 경화 강화     ☁ 황금 미끼 범위    ▭~ 황금 미끼 추가    ◖▭◗ 10만 볼트 강화
    ///   ◹ 미사일 강화        ✶ 부스터 위력 강화
    ///
    /// 에셋 없이 실행 중에 한 번 그려서 캐시한다. 밝은 채움 + 어두운 외곽선이라
    /// Image.color로 물들이면 외곽선은 같은 색의 어두운 톤이 된다.
    /// 채움은 위가 밝고 아래가 어두운 그라디언트에 왼쪽 위 광택을 얹어 입체감을 낸다.
    ///
    /// Soft(도형)은 같은 실루엣을 흐리게 번진 것 — 그림자(검정)와 빛 번짐(계열 색)에 쓴다.
    /// </summary>
    public static class SkillShapes
    {
        public enum Shape
        {
            Square, Circle, Diamond, Parallelogram, Triangle, Trapezoid,
            Tape, Card, DShape, Cloud, Document, DataStorage, Note, Star6, Pill
        }

        const int Size = 128;
        const int Supersample = 4;
        const float OutlinePx = 7f;
        const float OutlineValue = 0.36f;

        static readonly Dictionary<Shape, Sprite> _cache = new Dictionary<Shape, Sprite>();
        static readonly Dictionary<Shape, Sprite> _softCache = new Dictionary<Shape, Sprite>();

        /// <summary>Soft 스프라이트는 번짐이 잘리지 않게 여백을 두고 그린다. 칸 크기에 이 배율을 곱해 놓을 것.</summary>
        public const float SoftPad = 0.25f;   // 도형 높이 대비 사방 여백

        public static Vector2 SoftScale(Shape shape)
        {
            float w = shape == Shape.Pill ? 2.5f : 1f;
            return new Vector2((w + SoftPad * 2f) / w, 1f + SoftPad * 2f);
        }

        public static Shape ShapeOf(SkillEffectType t)
        {
            switch (t)
            {
                case SkillEffectType.SurvivalTime:    return Shape.Square;
                case SkillEffectType.BodyScale:       return Shape.Circle;
                case SkillEffectType.MouthPower:      return Shape.Diamond;
                case SkillEffectType.Vision:          return Shape.Parallelogram;
                case SkillEffectType.TimeGain:        return Shape.Triangle;
                case SkillEffectType.CurrencyGain:    return Shape.Trapezoid;
                case SkillEffectType.BoosterRange:    return Shape.Tape;
                case SkillEffectType.VacuumRange:     return Shape.Card;
                case SkillEffectType.ScaleArmorStack: return Shape.DShape;
                case SkillEffectType.BaitRange:       return Shape.Cloud;
                case SkillEffectType.BaitCount:       return Shape.Document;
                case SkillEffectType.VoltPower:       return Shape.DataStorage;
                case SkillEffectType.MissilePower:    return Shape.Note;
                case SkillEffectType.BoosterPower:    return Shape.Star6;
                default:                              return t.IsUnlock() ? Shape.Pill : Shape.Circle;
            }
        }

        public static Sprite For(SkillEffectType t) => Get(ShapeOf(t));

        /// <summary>해금 칸·시작 칸에 쓰는 가로로 긴 알약 모양.</summary>
        public static Sprite Pill => Get(Shape.Pill);

        public static Sprite Get(Shape shape)
        {
            if (_cache.TryGetValue(shape, out var s) && s != null) return s;
            s = Build(shape);
            _cache[shape] = s;
            return s;
        }

        /// <summary>도형 실루엣을 흐리게 번진 스프라이트 (흰색, 알파만). 그림자·빛 번짐용.</summary>
        public static Sprite Soft(Shape shape)
        {
            if (_softCache.TryGetValue(shape, out var s) && s != null) return s;
            s = BuildSoft(shape);
            _softCache[shape] = s;
            return s;
        }

        static Sprite BuildSoft(Shape shape)
        {
            const int h0 = 64;                         // 번짐은 해상도가 낮아도 티가 안 난다
            int w0 = shape == Shape.Pill ? h0 * 5 / 2 : h0;
            int pad = Mathf.RoundToInt(h0 * SoftPad);
            int w = w0 + pad * 2, h = h0 + pad * 2;
            float aspect = w0 / (float)h0;

            var a = new float[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fx = (x - pad + 0.5f) / h0 * 2f - aspect;
                float fy = (y - pad + 0.5f) / h0 * 2f - 1f;
                a[y * w + x] = Inside(shape, fx, fy, aspect) ? 1f : 0f;
            }

            // 상자 흐림 3번 ≈ 가우시안
            int r = Mathf.Max(2, h0 / 12);
            var tmp = new float[a.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlur(a, tmp, w, h, r, true);
                BoxBlur(tmp, a, w, h, r, false);
            }

            var tex = NewTexture($"SkillShapeSoft_{shape}", w, h);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(a[i] * 1.15f)));
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        public static void BoxBlur(float[] src, float[] dst, int w, int h, int r, bool horizontal)
        {
            float inv = 1f / (r * 2 + 1);
            if (horizontal)
            {
                for (int y = 0; y < h; y++)
                {
                    float acc = 0f;
                    for (int k = -r; k <= r; k++) acc += src[y * w + Mathf.Clamp(k, 0, w - 1)];
                    for (int x = 0; x < w; x++)
                    {
                        dst[y * w + x] = acc * inv;
                        acc += src[y * w + Mathf.Min(x + r + 1, w - 1)] - src[y * w + Mathf.Max(x - r, 0)];
                    }
                }
            }
            else
            {
                for (int x = 0; x < w; x++)
                {
                    float acc = 0f;
                    for (int k = -r; k <= r; k++) acc += src[Mathf.Clamp(k, 0, h - 1) * w + x];
                    for (int y = 0; y < h; y++)
                    {
                        dst[y * w + x] = acc * inv;
                        acc += src[Mathf.Min(y + r + 1, h - 1) * w + x] - src[Mathf.Max(y - r, 0) * w + x];
                    }
                }
            }
        }

        public static Texture2D NewTexture(string name, int w, int h, bool repeat = false)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        // ══════════════════════════════════════════════════════════
        //  그리기
        // ══════════════════════════════════════════════════════════
        static Sprite Build(Shape shape)
        {
            int w = shape == Shape.Pill ? Size * 5 / 2 : Size;
            int h = Size;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = $"SkillShape_{shape}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var px = new Color32[w * h];
            float inv = 1f / Supersample;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int cover = 0, coreCover = 0;
                    for (int sy = 0; sy < Supersample; sy++)
                    for (int sx = 0; sx < Supersample; sx++)
                    {
                        // 도형 좌표: 가로 -w/h..w/h, 세로 -1..1 (위가 +)
                        float fx = (x + (sx + 0.5f) * inv) / h * 2f - (float)w / h;
                        float fy = (y + (sy + 0.5f) * inv) / h * 2f - 1f;
                        if (!Inside(shape, fx, fy, w / (float)h)) continue;
                        cover++;
                        if (IsCore(shape, fx, fy, w / (float)h)) coreCover++;
                    }

                    int total = Supersample * Supersample;
                    float a = cover / (float)total;
                    float core = cover > 0 ? coreCover / (float)cover : 0f;

                    // 채움: 위가 밝고(0.98) 아래가 어둡다(0.70) + 왼쪽 위 광택
                    float cy = (y + 0.5f) / h * 2f - 1f;
                    float cx = (x + 0.5f) / h * 2f - (float)w / h;
                    float fill = Mathf.Lerp(0.70f, 0.98f, (cy + 1f) * 0.5f);
                    float gx = (cx + 0.30f * w / h) / (0.55f * (shape == Shape.Pill ? 2.2f : 1f));
                    float gy = (cy - 0.48f) / 0.24f;
                    float gloss = Mathf.Clamp01(1f - (gx * gx + gy * gy));
                    fill = Mathf.Lerp(fill, 1f, gloss * gloss * 0.9f);

                    byte v = (byte)(255f * Mathf.Lerp(OutlineValue, fill, core));
                    px[y * w + x] = new Color32(v, v, v, (byte)(255f * a));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f,
                                       0, SpriteMeshType.FullRect);
            sprite.name = tex.name;
            return sprite;
        }

        /// <summary>외곽선 안쪽인가 — 사방으로 외곽선 두께만큼 옮겨도 여전히 안이면 안쪽.</summary>
        static bool IsCore(Shape shape, float x, float y, float aspect)
        {
            float d = OutlinePx / Size * 2f;
            const float k = 0.7071f;
            return Inside(shape, x + d, y, aspect) && Inside(shape, x - d, y, aspect) &&
                   Inside(shape, x, y + d, aspect) && Inside(shape, x, y - d, aspect) &&
                   Inside(shape, x + d * k, y + d * k, aspect) && Inside(shape, x - d * k, y + d * k, aspect) &&
                   Inside(shape, x + d * k, y - d * k, aspect) && Inside(shape, x - d * k, y - d * k, aspect);
        }

        const float M = 0.9f;   // 가장자리 여백 (외곽선이 잘리지 않게)

        static bool Inside(Shape shape, float x, float y, float aspect)
        {
            switch (shape)
            {
                case Shape.Square:
                    return Mathf.Abs(x) <= M * 0.86f && Mathf.Abs(y) <= M * 0.86f;

                case Shape.Circle:
                    return x * x + y * y <= M * M;

                case Shape.Diamond:
                    return Mathf.Abs(x) + Mathf.Abs(y) <= M;

                case Shape.Parallelogram:
                {
                    // 위가 오른쪽으로 밀린 평행사변형
                    if (Mathf.Abs(y) > M * 0.62f) return false;
                    float shift = y * 0.45f;
                    return Mathf.Abs(x - shift) <= M * 0.66f;
                }

                case Shape.Triangle:
                {
                    // 오른쪽을 가리키는 삼각형 (도면의 draw.io triangle)
                    if (x < -M * 0.8f || x > M) return false;
                    float t = (x + M * 0.8f) / (M * 1.8f);
                    return Mathf.Abs(y) <= M * (1f - t);
                }

                case Shape.Trapezoid:
                {
                    // 아래가 넓은 사다리꼴
                    if (Mathf.Abs(y) > M * 0.62f) return false;
                    float t = (y + M * 0.62f) / (M * 1.24f);   // 0 아래 → 1 위
                    float half = Mathf.Lerp(M, M * 0.55f, t);
                    return Mathf.Abs(x) <= half;
                }

                case Shape.Tape:
                {
                    // 위아래가 물결인 띠
                    if (Mathf.Abs(x) > M) return false;
                    float wave = 0.14f * Mathf.Sin(x * Mathf.PI);
                    return y <= M * 0.6f + wave && y >= -M * 0.6f + wave;
                }

                case Shape.Card:
                {
                    // 왼쪽 위 모서리를 자른 카드
                    if (Mathf.Abs(x) > M * 0.72f || Mathf.Abs(y) > M) return false;
                    return (x + M * 0.72f) + (M - y) >= M * 0.55f;
                }

                case Shape.DShape:
                {
                    // 왼쪽이 평평하고 오른쪽이 둥근 D
                    if (Mathf.Abs(y) > M * 0.85f || x < -M * 0.75f) return false;
                    if (x <= 0f) return true;
                    float r = M * 0.85f;
                    return (x / (M * 0.95f)) * (x / (M * 0.95f)) + (y / r) * (y / r) <= 1f;
                }

                case Shape.Cloud:
                {
                    // 원 여러 개를 합친 구름
                    return InCircle(x, y, -0.48f, -0.10f, 0.40f) || InCircle(x, y, 0.02f, 0.22f, 0.50f) ||
                           InCircle(x, y, 0.50f, -0.06f, 0.40f) || InCircle(x, y, 0.0f, -0.24f, 0.42f) ||
                           InCircle(x, y, -0.28f, 0.30f, 0.32f);
                }

                case Shape.Document:
                {
                    // 아래쪽이 물결인 문서
                    if (Mathf.Abs(x) > M * 0.9f || y > M * 0.7f) return false;
                    float bottom = -M * 0.55f + 0.16f * Mathf.Sin((x + 0.2f) * Mathf.PI * 1.1f);
                    return y >= bottom;
                }

                case Shape.DataStorage:
                {
                    // 왼쪽은 바깥으로 볼록, 오른쪽은 안으로 오목한 원통 옆면 (draw.io dataStorage)
                    if (Mathf.Abs(y) > M * 0.62f) return false;
                    float q = y / (M * 0.62f);
                    float bulge = 0.24f * (1f - q * q);
                    return x >= -M + 0.24f - bulge && x <= M - bulge;
                }

                case Shape.Note:
                {
                    // 오른쪽 위 모서리가 접힌 메모지
                    if (Mathf.Abs(x) > M * 0.8f || Mathf.Abs(y) > M) return false;
                    return (M * 0.8f - x) + (M - y) >= M * 0.5f;
                }

                case Shape.Star6:
                    return InStar(x, y, 6, M, M * 0.52f);

                case Shape.Pill:
                {
                    float halfW = aspect * M;
                    float r = M * 0.8f;
                    if (Mathf.Abs(y) > r) return false;
                    float cx = Mathf.Clamp(x, -halfW + r, halfW - r);
                    return (x - cx) * (x - cx) + y * y <= r * r;
                }
            }
            return false;
        }

        static bool InCircle(float x, float y, float cx, float cy, float r) =>
            (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

        static bool InStar(float x, float y, int points, float outer, float inner)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > outer) return false;
            if (r <= inner) return true;

            // 꼭짓점 하나와 골 하나 사이의 직선 변까지의 거리로 판정한다 (곧은 변의 별)
            float seg = Mathf.PI * 2f / points;
            float half = seg * 0.5f;
            float a = Mathf.Repeat(Mathf.Atan2(y, x) - Mathf.PI * 0.5f, seg);   // 꼭짓점이 위로
            float phi = a > half ? seg - a : a;                                    // 0(꼭짓점) ~ half(골)

            // A = 꼭짓점 (outer, 0), B = 골 (inner·cos half, inner·sin half), 방향 u = (cos φ, sin φ)
            float ax = outer, ay = 0f;
            float bx = inner * Mathf.Cos(half), by = inner * Mathf.Sin(half);
            float ux = Mathf.Cos(phi), uy = Mathf.Sin(phi);
            float cross_ab = ax * by - ay * bx;
            float cross_u = ux * (by - ay) - uy * (bx - ax);
            if (Mathf.Abs(cross_u) < 1e-6f) return false;
            return r <= cross_ab / cross_u;
        }
    }
}
