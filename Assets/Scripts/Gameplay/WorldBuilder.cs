using System.Collections.Generic;
using FishGame.Data;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// WorldLayout의 동심원 맵을 실제 오브젝트로 만든다. (R14)
    ///
    ///   · 바닥 타일 — 구역마다 고리 메시 하나. ZoneData.floorTile(직접 그린 64px 물 타일)을 반복해서 깐다.
    ///                 타일 한 칸의 월드 크기는 그 구역에 들어올 때의 몸 크기에 맞춰 커져서, 화면에서 비슷한 크기로 보인다.
    ///   · 벽 바깥   — 지금 벽(ActiveRadius) 바깥을 어둡게 덮는 고리. 맵이 넓어지면 안쪽 경계가 따라 넓어진다.
    ///   · 벽         — 원형 EdgeCollider2D + 까만 윤곽선. 맵이 넓어지는 동안 SetActiveRadius로 매 프레임 갱신.
    ///   · 보스 구조물 — 보스가 있는 구역(바다)에 하나.
    ///
    /// 씬에 미리 그려두지 않고 런타임에 만드는 이유: 구역 데이터(엑셀)만 고치면 맵 전체가 따라 바뀌게.
    /// </summary>
    public class WorldBuilder : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("예전 통로 안내 문구용 폰트 (지금은 쓰지 않음 — 씬 호환용으로 남김)")]
        [SerializeField] TMPro.TMP_FontAsset labelFont;

        [Header("벽")]
        [Tooltip("콜라이더 두께. 너무 얇으면 빠른 물고기가 뚫고 나간다.")]
        [SerializeField] float edgeRadius = 0.25f;
        [SerializeField] float outlineWidth = 0.6f;
        [SerializeField] Color outlineColor = new Color(0.05f, 0.05f, 0.07f, 1f);
        [Tooltip("맵이 클수록 선도 굵어야 보인다. 벽 반지름에 이 비율을 곱해 보정.")]
        [SerializeField] float outlineWidthPerRadius = 0.006f;

        [Header("정렬 순서")]
        [SerializeField] int waterSortingOrder = -200;
        [SerializeField] int outlineSortingOrder = -100;

        const int CircleSegments = 160;      // 벽 · 고리의 둘레 분할
        const float TileWorldSizeDefault = 6f;

        WorldLayout _layout;
        readonly List<Obstacle> _obstacles = new List<Obstacle>();
        BossStructure _bossStructure;
        GameObject _waterGo, _wallsGo, _propsGo;
        EdgeCollider2D _wall;
        LineRenderer _outline;
        Mesh _outsideMesh;
        float _tileWorldSize = TileWorldSizeDefault;

        public WorldLayout Layout => _layout;
        public BossStructure Boss => _bossStructure;
        public IReadOnlyList<Obstacle> Obstacles => _obstacles;

        // ══════════════════════════════════════════════════════════
        //  빌드
        // ══════════════════════════════════════════════════════════
        public WorldLayout Build(IList<ZoneData> zones, GameDatabase db)
        {
            Clear();
            _layout = WorldLayout.Build(zones,
                db != null ? db.depthRichness : 1f, db != null ? db.globalValueScale : 1f,
                db != null ? db.mapBaseRadius : 24f, db != null ? db.mapGrowthPerZone : 8f);
            _tileWorldSize = db != null ? Mathf.Max(0.5f, db.mapTileWorldSize) : TileWorldSizeDefault;

            _waterGo = new GameObject("Water");
            _waterGo.transform.SetParent(transform, false);
            for (int i = 0; i < _layout.ZoneCount; i++) BuildZoneFloor(i);
            BuildOutside();
            BuildWalls();
            BuildBossStructure();
            SetActiveRadius(_layout.ActiveRadius);
            return _layout;
        }

        public void Clear()
        {
            _obstacles.Clear();
            Obstacle.ClearRegistry();
            _bossStructure = null;
            if (_waterGo != null) Destroy(_waterGo);
            if (_wallsGo != null) Destroy(_wallsGo);
            if (_propsGo != null) Destroy(_propsGo);
            _waterGo = _wallsGo = _propsGo = null;
            _wall = null; _outline = null; _outsideMesh = null;
        }

        // ══════════════════════════════════════════════════════════
        //  바닥 — 구역 고리마다 타일
        // ══════════════════════════════════════════════════════════
        void BuildZoneFloor(int i)
        {
            var z = _layout.GetZone(i);
            float inner = _layout.InnerRadius(i), outer = _layout.OuterRadius(i);

            var go = new GameObject($"Floor_{i}_{(z != null ? z.name : "Zone")}");
            go.transform.SetParent(_waterGo.transform, false);

            // 타일 한 칸 = 기본 크기 × (이 구역에 들어올 때의 몸 크기)^0.8 — 카메라 줌(크기^0.8)과 같은 비율이라
            // 어느 구역에서든 화면에 비슷한 크기로 보인다 (맵 반지름처럼 8배씩 키우면 바깥 구역 타일이 화면보다 커진다)
            var prevZone = i > 0 ? _layout.GetZone(i - 1) : null;
            float enterSize = prevZone != null ? Mathf.Max(1f, prevZone.exitRequiredSize) : 1f;
            float tile = _tileWorldSize * Mathf.Pow(enterSize, 0.8f);
            var mesh = Annulus($"Floor_{i}", inner, outer, tile, Color.white);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();

            var mat = CreateMaterial($"FloorMat_{i}");
            var tex = z != null ? z.floorTile : null;
            if (tex != null)
            {
                tex.wrapMode = TextureWrapMode.Repeat;
                SetTexture(mat, tex);
            }
            else
            {
                // 타일 그림이 없으면 예전 구역 물 색
                var c = z != null ? LabWater(z.waterColor) : new Color(0.1f, 0.25f, 0.3f);
                SetColor(mesh, c);
            }
            mr.sharedMaterial = mat;
            mr.sortingOrder = waterSortingOrder;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // ══════════════════════════════════════════════════════════
        //  벽 바깥 — 아직 넓어지지 않은 고리를 어둡게 덮는다
        // ══════════════════════════════════════════════════════════
        void BuildOutside()
        {
            var go = new GameObject("Outside");
            go.transform.SetParent(_waterGo.transform, false);
            _outsideMesh = new Mesh { name = "Outside" };
            _outsideMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _outsideMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CreateMaterial("OutsideMat");
            mr.sortingOrder = waterSortingOrder + 5;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // ══════════════════════════════════════════════════════════
        //  벽 — 원형 콜라이더 + 윤곽선
        // ══════════════════════════════════════════════════════════
        void BuildWalls()
        {
            _wallsGo = new GameObject("Walls");
            _wallsGo.transform.SetParent(transform, false);
            int wallLayer = LayerMask.NameToLayer("Wall");
            if (wallLayer >= 0) _wallsGo.layer = wallLayer;

            _wall = _wallsGo.AddComponent<EdgeCollider2D>();
            _wall.edgeRadius = edgeRadius;
            _wall.useAdjacentStartPoint = false;
            _wall.useAdjacentEndPoint = false;
            var rb = _wallsGo.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;

            var lineGo = new GameObject("Outline");
            lineGo.transform.SetParent(_wallsGo.transform, false);
            _outline = lineGo.AddComponent<LineRenderer>();
            _outline.useWorldSpace = false;
            _outline.loop = true;
            _outline.positionCount = CircleSegments;
            _outline.numCornerVertices = 2;
            _outline.alignment = LineAlignment.View;
            _outline.sortingOrder = outlineSortingOrder;
            _outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _outline.receiveShadows = false;
            _outline.sharedMaterial = CreateMaterial("OutlineMat");
            _outline.startColor = _outline.endColor = outlineColor;
        }

        /// <summary>벽 반지름을 바꾼다 — 콜라이더 · 윤곽선 · 벽 바깥 덮개를 같이 맞춘다.</summary>
        public void SetActiveRadius(float r)
        {
            if (_layout == null) return;
            _layout.ActiveRadius = r;

            if (_wall != null)
            {
                var pts = new Vector2[CircleSegments + 1];
                for (int k = 0; k <= CircleSegments; k++) pts[k] = Dir(k) * r;
                _wall.points = pts;
            }
            if (_outline != null)
            {
                for (int k = 0; k < CircleSegments; k++) _outline.SetPosition(k, Dir(k) * r);
                _outline.widthMultiplier = Mathf.Max(outlineWidth, r * outlineWidthPerRadius);
            }
            if (_outsideMesh != null)
            {
                // 바깥 덮개는 맵 끝의 3배까지 — 카메라가 벽 너머를 봐도 덮이게
                float far = Mathf.Max(_layout.MaxRadius * 3f, r * 4f);
                var dark = OutsideColor(_layout.GetZone(_layout.ZoneIndexAt(Vector2.right * Mathf.Max(0f, r - 0.01f))).waterColorOrDefault());
                FillAnnulus(_outsideMesh, r, far, 1f, dark);
            }
        }

        // ══════════════════════════════════════════════════════════
        //  보스 구조물
        // ══════════════════════════════════════════════════════════
        void BuildBossStructure()
        {
            for (int i = 0; i < _layout.ZoneCount; i++)
            {
                var z = _layout.GetZone(i);
                if (z == null || z.boss == null || !z.bossFromStructure) continue;
                _propsGo = _propsGo != null ? _propsGo : NewProps();
                _bossStructure = BossStructure.Create(_propsGo.transform,
                    _layout.BossStructureWorldPos(i, -Mathf.PI * 0.5f), i, z.boss, z.bossStructureRadius);
                break;
            }
        }

        /// <summary>
        /// 보스 구역까지 넓어졌을 때 구조물을 플레이어 쪽으로 옮긴다 — 바다 고리는 아주 넓어서
        /// 고정된 자리에 두면 찾으러 한참 헤엄쳐야 했다. 플레이어와 같은 방향, 화면 몇 개 떨어진 곳.
        /// </summary>
        public void PlaceBossNear(Vector2 playerPos, float viewRadius)
        {
            if (_bossStructure == null || _bossStructure.IsBroken || _layout == null) return;
            int zi = _bossStructure.ZoneIndex;
            float inner = _layout.InnerRadius(zi), outer = _layout.OuterRadius(zi);
            Vector2 dir = playerPos.sqrMagnitude > 0.01f ? playerPos.normalized : Vector2.down;
            float r = Mathf.Clamp(playerPos.magnitude + viewRadius * 2.5f, inner + 10f, outer * 0.9f);
            _bossStructure.transform.position = dir * r;
        }

        GameObject NewProps()
        {
            var go = new GameObject("Props");
            go.transform.SetParent(transform, false);
            return go;
        }

        // ══════════════════════════════════════════════════════════
        //  메시 · 재질 도우미
        // ══════════════════════════════════════════════════════════
        static Vector2 Dir(int k)
        {
            float a = k * Mathf.PI * 2f / CircleSegments;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        /// <summary>안쪽 · 바깥 반지름 사이 고리 (inner = 0이면 원판). UV = 월드 좌표 / tile.</summary>
        static Mesh Annulus(string name, float inner, float outer, float tile, Color color)
        {
            var m = new Mesh { name = name };
            FillAnnulus(m, inner, outer, tile, color);
            return m;
        }

        static void FillAnnulus(Mesh m, float inner, float outer, float tile, Color color)
        {
            int n = CircleSegments;
            var verts = new Vector3[(n + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var cols = new Color[verts.Length];
            var tris = new int[n * 6];
            Color lin = color.linear;
            for (int k = 0; k <= n; k++)
            {
                Vector2 d = Dir(k);
                Vector2 a = d * inner, b = d * outer;
                verts[k * 2] = a; verts[k * 2 + 1] = b;
                uvs[k * 2] = a / tile; uvs[k * 2 + 1] = b / tile;
                cols[k * 2] = cols[k * 2 + 1] = lin;
            }
            for (int k = 0; k < n; k++)
            {
                int a = k * 2, b = a + 1, c = a + 2, d = a + 3, o = k * 6;
                tris[o] = a; tris[o + 1] = c; tris[o + 2] = b;
                tris[o + 3] = b; tris[o + 4] = c; tris[o + 5] = d;
            }
            m.Clear();
            m.vertices = verts;
            m.uv = uvs;
            m.colors = cols;
            m.triangles = tris;
            m.RecalculateBounds();
        }

        static void SetColor(Mesh m, Color c)
        {
            var cols = new Color[m.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = c.linear;
            m.colors = cols;
        }

        static Material CreateMaterial(string name)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            var mat = new Material(sh) { name = name };
            SetTexture(mat, Texture2D.whiteTexture);
            return mat;
        }

        static void SetTexture(Material mat, Texture tex)
        {
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        }

        // ── 색 (다른 곳에서도 쓴다) ─────────────────────────────
        static readonly Color DeepBase = new Color(0.012f, 0.055f, 0.075f, 1f);
        const float ZoneTint = 0.36f;

        public static Color LabWater(Color zoneWater)
        {
            var c = Color.Lerp(DeepBase, zoneWater, ZoneTint);
            c.a = 1f;
            return c;
        }

        public static Color OutsideColor(Color zoneWater)
        {
            var c = Color.Lerp(new Color(0.004f, 0.016f, 0.024f, 1f), LabWater(zoneWater), 0.18f);
            c.a = 1f;
            return c;
        }
    }

    static class ZoneDataColorExt
    {
        public static Color waterColorOrDefault(this ZoneData z) =>
            z != null ? z.waterColor : new Color(0.2f, 0.4f, 0.55f, 1f);
    }
}
