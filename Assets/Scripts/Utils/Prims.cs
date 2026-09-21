using UnityEngine;

namespace FishGame.Utils
{
    /// <summary>
    /// 런타임에 만들어 쓰는 단순 도형 스프라이트.
    ///
    /// 통로 게이트·히든 아이템·보스 구조물처럼 "모양은 단순하지만 반드시 있어야 하는"
    /// 오브젝트를 프리팹 없이 만들기 위한 것이다. 프리팹을 거치지 않으니
    /// 인스펙터 참조가 끊겨서 게이트가 사라지는 사고가 나지 않는다.
    ///
    /// 나중에 도트 아트가 나오면 여기 대신 실제 스프라이트를 꽂으면 된다.
    /// </summary>
    public static class Prims
    {
        const float PPU = 32f;

        static Sprite _white;
        static Sprite _diamond;
        static Sprite _ring;
        static Sprite _softDot;
        static Sprite _disc;
        static Sprite _triangle;

        /// <summary>1×1 흰 사각형. 스케일로 늘려 쓴다.</summary>
        public static Sprite White
        {
            get
            {
                if (_white != null) return _white;
                var tex = MakeTex(4, 4, (x, y, w, h) => Color.white);
                _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                _white.name = "Prim_White";
                return _white;
            }
        }

        /// <summary>마름모. 히든 아이템 표식(◇)에 쓴다.</summary>
        public static Sprite Diamond
        {
            get
            {
                if (_diamond != null) return _diamond;
                const int S = 32;
                var tex = MakeTex(S, S, (x, y, w, h) =>
                {
                    float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
                    float d = Mathf.Abs(x - cx) / cx + Mathf.Abs(y - cy) / cy;
                    if (d > 1f) return Color.clear;
                    // 가장자리는 어둡게 — 배경과 구분되도록
                    return d > 0.72f ? new Color(0f, 0f, 0f, 1f) : Color.white;
                });
                _diamond = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), PPU);
                _diamond.name = "Prim_Diamond";
                return _diamond;
            }
        }

        /// <summary>속이 빈 고리. 바다의 보스 구조물에 쓴다.</summary>
        public static Sprite Ring
        {
            get
            {
                if (_ring != null) return _ring;
                const int S = 64;
                var tex = MakeTex(S, S, (x, y, w, h) =>
                {
                    float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
                    float r = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / cx;
                    if (r > 1f || r < 0.62f) return Color.clear;
                    bool edge = r > 0.93f || r < 0.70f;
                    return edge ? new Color(0f, 0f, 0f, 1f) : Color.white;
                });
                _ring = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), PPU);
                _ring.name = "Prim_Ring";
                return _ring;
            }
        }

        /// <summary>속이 찬 원. 하수구의 쇠창살 원형 장애물에 쓴다.</summary>
        public static Sprite Disc
        {
            get
            {
                if (_disc != null) return _disc;
                const int S = 64;
                var tex = MakeTex(S, S, (x, y, w, h) =>
                {
                    float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
                    float r = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / cx;
                    if (r > 1f) return Color.clear;
                    // 테두리는 까맣게 — 물리적 오브젝트임을 배경과 구분해 보여준다
                    return r > 0.86f ? new Color(0f, 0f, 0f, 1f) : Color.white;
                });
                _disc = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), PPU);
                _disc.name = "Prim_Disc";
                return _disc;
            }
        }

        /// <summary>위를 향한 삼각형. 강의 자갈 무더기·바다 암초에 쓴다.</summary>
        public static Sprite Triangle
        {
            get
            {
                if (_triangle != null) return _triangle;
                const int S = 64;
                var tex = MakeTex(S, S, (x, y, w, h) =>
                {
                    // 밑변이 아래, 꼭짓점이 위인 이등변삼각형
                    float u = x / (float)(w - 1);          // 0..1
                    float v = y / (float)(h - 1);          // 0(아래)..1(위)
                    float halfAt = (1f - v) * 0.5f;        // 그 높이에서의 반폭
                    float d = Mathf.Abs(u - 0.5f);
                    if (d > halfAt) return Color.clear;
                    bool edge = halfAt - d < 0.055f || v < 0.06f;
                    return edge ? new Color(0f, 0f, 0f, 1f) : Color.white;
                });
                _triangle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), PPU);
                _triangle.name = "Prim_Triangle";
                return _triangle;
            }
        }

        /// <summary>가장자리가 부드러운 원. 발광 효과용.</summary>
        public static Sprite SoftDot
        {
            get
            {
                if (_softDot != null) return _softDot;
                const int S = 64;
                var tex = MakeTex(S, S, (x, y, w, h) =>
                {
                    float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
                    float r = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / cx;
                    float a = Mathf.Clamp01(1f - r);
                    return new Color(1f, 1f, 1f, a * a);
                });
                _softDot = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), PPU);
                _softDot.name = "Prim_SoftDot";
                return _softDot;
            }
        }

        delegate Color Fill(int x, int y, int w, int h);

        static Texture2D MakeTex(int w, int h, Fill fill)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "PrimTex",
            };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = fill(x, y, w, h);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>스프라이트 하나짜리 자식 오브젝트를 빠르게 만든다.</summary>
        public static SpriteRenderer NewSprite(Transform parent, string name, Sprite sprite,
                                               Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
            return sr;
        }
    }
}
