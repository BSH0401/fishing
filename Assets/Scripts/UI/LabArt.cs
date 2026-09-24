using UnityEngine;

namespace FishGame.UI
{
    /// <summary>
    /// 스킬트리 창("수중 실험실")에 쓰는 절차적 스프라이트 모음. 에셋 없이 실행 중에 한 번 그려서 캐시한다.
    ///
    ///   Panel / PanelBorder   둥근 모서리 패널 (9-슬라이스) — 실험실 계기판
    ///   GridTile              청사진 모눈 (타일)
    ///   Ray                   수면에서 내려오는 빛줄기
    ///   Bubble                물방울
    ///   SoftDot / Ring        빛 점, 퍼져 나가는 고리
    ///   Vignette              화면 가장자리를 어둡게
    ///   Backdrop              위는 옅고 아래로 깊어지는 바다색
    ///   Dash                  흐르는 점선 (반복 텍스처)
    ///   LineSoft              가장자리가 부드러운 선 (빛 번짐)
    /// 대부분 흰색이라 Image.color로 물들여 쓴다.
    /// </summary>
    public static class LabArt
    {
        static Sprite _panel, _panelBorder, _grid, _ray, _bubble, _softDot, _ring, _vignette, _backdrop, _lineSoft, _corner;
        static Texture2D _dash;

        // ── 패널 ────────────────────────────────────────────────
        public static Sprite Panel => _panel != null ? _panel : (_panel = RoundedRect("LabPanel", 64, 14, false));
        public static Sprite PanelBorder => _panelBorder != null ? _panelBorder : (_panelBorder = RoundedRect("LabPanelBorder", 64, 14, true));

        /// <summary>패널 모서리의 ㄱ자 장식 (왼쪽 위 방향). 회전해서 네 모서리에 쓴다.</summary>
        public static Sprite Corner
        {
            get
            {
                if (_corner != null) return _corner;
                const int n = 32;
                var tex = SkillShapes.NewTexture("LabCorner", n, n);
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int fromTop = n - 1 - y;
                    bool on = (fromTop < 4 && x < 26) || (x < 4 && fromTop < 26);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(on ? 255 : 0));
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                return _corner = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0f, 1f), 100f, 0, SpriteMeshType.FullRect);
            }
        }

        static Sprite RoundedRect(string name, int size, int radius, bool borderOnly)
        {
            var tex = SkillShapes.NewTexture(name, size, size);
            var px = new Color32[size * size];
            const int ss = 4;
            float thick = 2.2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int cover = 0;
                for (int sy = 0; sy < ss; sy++)
                for (int sx = 0; sx < ss; sx++)
                {
                    float fx = x + (sx + 0.5f) / ss, fy = y + (sy + 0.5f) / ss;
                    float d = RoundRectDistance(fx, fy, size, radius);        // 안쪽이 음수
                    bool inside = borderOnly ? (d <= 0f && d > -thick) : d <= 0f;
                    if (inside) cover++;
                }
                px[y * size + x] = new Color32(255, 255, 255, (byte)(255f * cover / (ss * ss)));
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            var border = new Vector4(radius + 2, radius + 2, radius + 2, radius + 2);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, border);
        }

        static float RoundRectDistance(float x, float y, int size, int radius)
        {
            float half = size * 0.5f;
            float qx = Mathf.Abs(x - half) - (half - radius);
            float qy = Mathf.Abs(y - half) - (half - radius);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        // ── 청사진 모눈 ─────────────────────────────────────────
        /// <summary>64px 타일: 가는 모눈선 + 교차점의 작은 십자. Image.type = Tiled로 깐다.</summary>
        public static Sprite GridTile
        {
            get
            {
                if (_grid != null) return _grid;
                const int n = 64;
                var tex = SkillShapes.NewTexture("LabGrid", n, n, repeat: true);
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = 0f;
                    if (x == 0 || y == 0) a = 0.55f;                       // 굵은 칸 경계
                    else if (x == n / 2 || y == n / 2) a = 0.18f;          // 가는 보조선
                    bool cross = (x <= 3 || x >= n - 3) && (y <= 3 || y >= n - 3);
                    if (cross && (x == 0 || y == 0)) a = 1f;               // 교차점 십자
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * a));
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                return _grid = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }
        }

        // ── 빛줄기 ──────────────────────────────────────────────
        /// <summary>세로로 긴 빛줄기. 가로는 가우시안, 세로는 위가 진하고 아래로 사라진다.</summary>
        public static Sprite Ray
        {
            get
            {
                if (_ray != null) return _ray;
                const int w = 64, h = 256;
                var tex = SkillShapes.NewTexture("LabRay", w, h);
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f;
                    float v = (y + 0.5f) / h;                               // 0 아래 → 1 위
                    float a = Mathf.Exp(-u * u * 4.5f) * Mathf.Pow(v, 1.6f);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(255f * a));
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                return _ray = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), 100f, 0, SpriteMeshType.FullRect);
            }
        }

        // ── 물방울 ──────────────────────────────────────────────
        public static Sprite Bubble
        {
            get
            {
                if (_bubble != null) return _bubble;
                const int n = 48;
                var tex = SkillShapes.NewTexture("LabBubble", n, n);
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.1f);          // 테두리
                    float body = r < 0.82f ? 0.12f + 0.2f * r : 0f;                           // 속은 옅게
                    float hx = u + 0.32f, hy = v - 0.34f;
                    float spec = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) / 0.2f);  // 반짝임
                    float a = Mathf.Clamp01(Mathf.Max(rim * 0.8f, body) + spec);
                    if (r > 0.95f) a = 0f;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * a));
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                return _bubble = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }
        }

        // ── 빛 점 / 고리 ────────────────────────────────────────
        public static Sprite SoftDot => _softDot != null ? _softDot : (_softDot = Radial("LabSoftDot", 64, r => Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f)));

        public static Sprite Ring => _ring != null ? _ring
            : (_ring = Radial("LabRing", 128, r => Mathf.Clamp01(1f - Mathf.Abs(r - 0.85f) / 0.1f)));

        /// <summary>가운데는 투명, 가장자리로 갈수록 검게.</summary>
        public static Sprite Vignette => _vignette != null ? _vignette
            : (_vignette = Radial("LabVignette", 128, r => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 0.55f) / 0.9f))));

        static Sprite Radial(string name, int n, System.Func<float, float> alphaOfRadius)
        {
            var tex = SkillShapes.NewTexture(name, n, n);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(alphaOfRadius(Mathf.Sqrt(u * u + v * v)));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * a));
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        // ── 바다 배경 ───────────────────────────────────────────
        /// <summary>위(수면 쪽)는 옅은 청록, 아래는 깊은 남색. 색이 구워져 있으니 흰색으로 둔다.</summary>
        public static Sprite Backdrop
        {
            get
            {
                if (_backdrop != null) return _backdrop;
                const int w = 4, h = 256;
                var tex = SkillShapes.NewTexture("LabBackdrop", w, h);
                var top = new Color(0.08f, 0.26f, 0.30f);
                var mid = new Color(0.04f, 0.14f, 0.19f);
                var bottom = new Color(0.02f, 0.05f, 0.09f);
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    float t = (y + 0.5f) / h;                               // 0 아래 → 1 위
                    Color c = t > 0.55f ? Color.Lerp(mid, top, (t - 0.55f) / 0.45f)
                                        : Color.Lerp(bottom, mid, t / 0.55f);
                    for (int x = 0; x < w; x++) px[y * w + x] = c;
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                return _backdrop = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }
        }

        // ── 선 ─────────────────────────────────────────────────
        /// <summary>
        /// 흐르는 점선 텍스처. 가로로 반복되며, RawImage.uvRect.x를 움직이면 선을 따라 흐른다.
        /// 한 주기 = 밝은 점 하나 (앞쪽이 진하고 꼬리가 흐려지는 혜성 모양).
        /// </summary>
        public static Texture2D Dash
        {
            get
            {
                if (_dash != null) return _dash;
                const int w = 64, h = 16;
                var tex = SkillShapes.NewTexture("LabDash", w, h, repeat: true);
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = (y + 0.5f) / h * 2f - 1f;
                    float across = Mathf.Exp(-v * v * 5f);
                    float t = (x + 0.5f) / w;                               // 0..1
                    float head = 0.42f;
                    float along = t <= head ? Mathf.Pow(t / head, 2.5f) : Mathf.Clamp01(1f - (t - head) / 0.08f);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(255f * across * along));
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                return _dash = tex;
            }
        }

        /// <summary>세로 단면이 부드러운 선: 가운데 심은 진하고 바깥으로 빛이 번진다.</summary>
        public static Sprite LineSoft
        {
            get
            {
                if (_lineSoft != null) return _lineSoft;
                const int w = 4, h = 32;
                var tex = SkillShapes.NewTexture("LabLineSoft", w, h);
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    float v = Mathf.Abs((y + 0.5f) / h * 2f - 1f);        // 0 가운데 → 1 가장자리
                    float a = v < 0.3f ? 1f : Mathf.Pow(Mathf.Clamp01(1f - (v - 0.3f) / 0.7f), 1.8f) * 0.55f;
                    for (int x = 0; x < w; x++) px[y * w + x] = new Color32(255, 255, 255, (byte)(255f * a));
                }
                tex.SetPixels32(px); tex.Apply(false, true);
                return _lineSoft = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }
        }
    }
}
