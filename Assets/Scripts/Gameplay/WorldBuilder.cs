using System.Collections.Generic;
using FishGame.Data;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// WorldLayout이 계산한 실루엣을 실제 오브젝트로 만든다.
    ///
    ///   · 물 메시   — 좌/우 실루엣 사이를 채우는 띠. 존마다 색이 달라 아래로 갈수록 어두워진다.
    ///   · 벽 콜라이더 — 실루엣을 따라가는 닫힌 EdgeCollider2D 하나
    ///   · 벽 윤곽선  — 까만 LineRenderer. 기획 메모대로 "물리적 작용이 되는 오브젝트"임을 분명히 한다.
    ///   · 통로 게이트 — 존마다 하나. ZoneGate가 열고 닫는다.
    ///
    /// 씬에 미리 그려두지 않고 런타임에 만드는 이유: 존 데이터만 고치면
    /// 맵 전체가 따라 바뀌어야 밸런스 조정이 돌아가기 때문이다.
    /// </summary>
    public class WorldBuilder : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("게이트 안내 문구용 한글 폰트. 비어 있으면 TMP 기본 폰트를 쓴다.")]
        [SerializeField] TMPro.TMP_FontAsset labelFont;

        [Header("벽")]
        [Tooltip("콜라이더 두께. 너무 얇으면 빠른 물고기가 뚫고 나간다.")]
        [SerializeField] float edgeRadius = 0.25f;
        [SerializeField] float outlineWidth = 0.6f;
        [SerializeField] Color outlineColor = new Color(0.05f, 0.05f, 0.07f, 1f);
        [Tooltip("월드가 클수록 선도 굵어야 보인다. 존 반폭에 이 비율을 곱해 보정.")]
        [SerializeField] float outlineWidthPerHalfWidth = 0.012f;

        [Header("정렬 순서")]
        [SerializeField] int waterSortingOrder = -200;
        [SerializeField] int outlineSortingOrder = -100;
        [SerializeField] int obstacleSortingOrder = -90;

        WorldLayout _layout;
        readonly List<ZoneGate> _gates = new List<ZoneGate>();
        readonly List<HiddenItem> _hiddenItems = new List<HiddenItem>();
        readonly List<Obstacle> _obstacles = new List<Obstacle>();
        BossStructure _bossStructure;

        GameObject _waterGo, _wallsGo, _propsGo;

        public WorldLayout Layout => _layout;
        public IReadOnlyList<ZoneGate> Gates => _gates;
        public BossStructure Boss => _bossStructure;

        // ══════════════════════════════════════════════════════════
        //  빌드
        // ══════════════════════════════════════════════════════════
        public WorldLayout Build(IList<ZoneData> zones, HashSet<string> collectedHiddenIds,
                                 float depthRichness = 1f, float globalValueScale = 1f)
        {
            Clear();

            _layout = WorldLayout.Build(zones, depthRichness, globalValueScale);
            if (_layout == null) return null;

            BuildOutlinePaths(out var left, out var right, out var rowZone);

            BuildWaterMesh(left, right);
            BuildWaterGrid(left, right, rowZone);
            BuildWalls(left, right);
            BuildObstacles();          // 히든 아이템보다 먼저 — 아이템이 장애물 안에 박히지 않게
            BuildGates();
            BuildBossStructure();
            BuildHiddenItems(collectedHiddenIds);

            return _layout;
        }

        public IReadOnlyList<Obstacle> Obstacles => _obstacles;

        public void Clear()
        {
            _gates.Clear();
            _hiddenItems.Clear();
            _obstacles.Clear();
            Obstacle.ClearRegistry();
            _bossStructure = null;

            if (_waterGo != null) Destroy(_waterGo);
            if (_wallsGo != null) Destroy(_wallsGo);
            if (_propsGo != null) Destroy(_propsGo);
            _waterGo = _wallsGo = _propsGo = null;
        }

        // ══════════════════════════════════════════════════════════
        //  실루엣 경로
        // ══════════════════════════════════════════════════════════
        /// <summary>
        /// 위에서 아래로 내려가며 좌/우 벽의 점을 뽑는다.
        /// 존 바닥에서는 통로 입구까지 가로로 꺾어 "구멍 뚫린 바닥"을 만든다.
        /// </summary>
        void BuildOutlinePaths(out List<Vector2> left, out List<Vector2> right, out List<int> rowZone)
        {
            left = new List<Vector2>(256);
            right = new List<Vector2>(256);
            rowZone = new List<int>(256);

            int count = _layout.ZoneCount;
            for (int i = 0; i < count; i++)
            {
                var s = _layout.GetSlice(i);
                var z = s.Zone;
                if (z == null) continue;

                int segs = Mathf.Max(2, z.outlineSegments);
                for (int k = 0; k <= segs; k++)
                {
                    float t = (float)k / segs;
                    float y = Mathf.Lerp(s.YTop, s.YBottom, t);
                    float hw = z.HalfWidthAt(t);
                    left.Add(new Vector2(-hw, y));
                    right.Add(new Vector2(hw, y));
                    rowZone.Add(i);
                }

                if (s.HasCorridor)
                {
                    float cl = s.CorridorCenterX - s.CorridorHalfWidth;
                    float cr = s.CorridorCenterX + s.CorridorHalfWidth;

                    // 존 바닥(가로) → 통로 벽(세로)
                    left.Add(new Vector2(cl, s.YBottom));
                    right.Add(new Vector2(cr, s.YBottom));
                    left.Add(new Vector2(cl, s.CorridorBottom));
                    right.Add(new Vector2(cr, s.CorridorBottom));
                    rowZone.Add(i); rowZone.Add(i);

                    // 통로 바닥 → 다음 존 천장(가로로 벌어짐)
                    var next = _layout.GetSlice(i + 1);
                    if (next.Zone != null)
                    {
                        float nhw = next.Zone.HalfWidthAt(0f);
                        left.Add(new Vector2(-nhw, s.CorridorBottom));
                        right.Add(new Vector2(nhw, s.CorridorBottom));
                        rowZone.Add(i + 1);
                    }
                }
            }
        }

        // ══════════════════════════════════════════════════════════
        //  물 메시 — 좌/우 경로 사이를 채우는 띠
        // ══════════════════════════════════════════════════════════
        void BuildWaterMesh(List<Vector2> left, List<Vector2> right)
        {
            int n = Mathf.Min(left.Count, right.Count);
            if (n < 2) return;

            _waterGo = new GameObject("Water");
            _waterGo.transform.SetParent(transform, false);

            // 한 줄에 점 3개(왼쪽 벽 · 가운데 · 오른쪽 벽). 가운데를 밝게, 벽 쪽을 어둡게 해서
            // 메인 화면처럼 가운데로 빛이 모이는 느낌을 낸다.
            var verts = new Vector3[n * 3];
            var colors = new Color[n * 3];
            var uvs = new Vector2[n * 3];
            var tris = new int[(n - 1) * 12];

            for (int i = 0; i < n; i++)
            {
                Vector2 l = left[i], r = right[i];
                Vector2 m = (l + r) * 0.5f;
                verts[i * 3] = l;
                verts[i * 3 + 1] = m;
                verts[i * 3 + 2] = r;

                Color c = WaterColorAt(l.y);
                Color edge = c * EdgeShade; edge.a = 1f;
                Color mid = c * CenterGlow;  mid.a = 1f;
                // 메시 정점 색은 선형 색공간에서 변환되지 않는다 — 직접 .linear로 바꿔야
                // 인스펙터에서 고른 색 그대로 보인다 (안 하면 물이 뿌옇고 밝게 뜬다)
                colors[i * 3] = edge.linear;
                colors[i * 3 + 1] = mid.linear;
                colors[i * 3 + 2] = edge.linear;

                float v = (float)i / (n - 1);
                uvs[i * 3] = new Vector2(0f, v);
                uvs[i * 3 + 1] = new Vector2(0.5f, v);
                uvs[i * 3 + 2] = new Vector2(1f, v);
            }

            for (int i = 0; i < n - 1; i++)
            {
                int o = i * 12;
                for (int col = 0; col < 2; col++)
                {
                    int a = i * 3 + col, b = a + 1, c = (i + 1) * 3 + col, d = c + 1;
                    int k = o + col * 6;
                    tris[k + 0] = a; tris[k + 1] = c; tris[k + 2] = b;
                    tris[k + 3] = b; tris[k + 4] = c; tris[k + 5] = d;
                }
            }

            var mesh = new Mesh { name = "WaterStrip" };
            mesh.indexFormat = verts.Length > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            _waterGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = _waterGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CreateVertexColorMaterial();
            mr.sortingOrder = waterSortingOrder;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        // ── 수중 실험실 팔레트 ──────────────────────────────────
        // 구역 데이터의 물 색(어항 민트 · 하수구 탁한 초록 · 강 파랑 · 바다 남색)은 살리되,
        // 타이틀 · 스킬트리 배경처럼 어둡고 깊은 청록 쪽으로 끌어내린다.
        static readonly Color DeepBase = new Color(0.012f, 0.055f, 0.075f, 1f);
        const float ZoneTint = 0.36f;      // 구역 색이 남는 비율
        const float EdgeShade = 0.72f;     // 벽 쪽 어둡기
        const float CenterGlow = 1.12f;    // 가운데 밝기

        /// <summary>구역 물 색 → 수중 실험실 톤.</summary>
        public static Color LabWater(Color zoneWater)
        {
            var c = Color.Lerp(DeepBase, zoneWater, ZoneTint);
            c.a = 1f;
            return c;
        }

        /// <summary>벽 바깥(카메라 배경). 물보다 한참 어두운 바위 · 어둠.</summary>
        public static Color OutsideColor(Color zoneWater)
        {
            var c = Color.Lerp(new Color(0.004f, 0.016f, 0.024f, 1f), LabWater(zoneWater), 0.18f);
            c.a = 1f;
            return c;
        }

        Color WaterColorAt(float y)
        {
            int i = _layout.ZoneIndexAt(y);
            var z = _layout.GetZone(i);
            if (z == null) return LabWater(new Color(0.2f, 0.4f, 0.55f));

            // 같은 존 안에서도 아래로 갈수록 어둡게 — 깊이감
            float d = _layout.DepthFactorInZone(y);
            Color c = LabWater(z.waterColor);
            return Color.Lerp(c * 1.18f, c * 0.70f, d);
        }

        // ══════════════════════════════════════════════════════════
        //  물 위의 옅은 격자 — 스킬트리 배경과 같은 실험실 도면 격자
        // ══════════════════════════════════════════════════════════
        void BuildWaterGrid(List<Vector2> left, List<Vector2> right, List<int> rowZone)
        {
            int n = Mathf.Min(left.Count, Mathf.Min(right.Count, rowZone.Count));
            if (n < 2 || _waterGo == null) return;

            var go = new GameObject("WaterGrid");
            go.transform.SetParent(_waterGo.transform, false);

            var verts = new Vector3[n * 2];
            var colors = new Color[n * 2];
            var uvs = new Vector2[n * 2];
            var tris = new int[(n - 1) * 6];

            for (int i = 0; i < n; i++)
            {
                // 구역마다 칸 크기를 그 구역 폭에 맞춘다 — 바다에서 카메라가 멀어져도 격자가 빽빽해지지 않게
                var z = _layout.GetZone(rowZone[i]);
                float cell = z != null ? Mathf.Max(2f, z.HalfWidthAt(0f) * 0.22f) : 4f;
                float depth = _layout.ZoneCount > 1 ? rowZone[i] / (float)(_layout.ZoneCount - 1) : 0f;
                // 얕은 실험실(어항)일수록 격자가 또렷하고, 바다로 갈수록 거의 사라진다
                var col = new Color(0.55f, 0.95f, 0.92f, Mathf.Lerp(0.085f, 0.025f, depth));

                verts[i * 2] = left[i];
                verts[i * 2 + 1] = right[i];
                colors[i * 2] = colors[i * 2 + 1] = col.linear;
                uvs[i * 2] = left[i] / cell;
                uvs[i * 2 + 1] = right[i] / cell;
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3, o = i * 6;
                tris[o] = a; tris[o + 1] = c; tris[o + 2] = b;
                tris[o + 3] = b; tris[o + 4] = c; tris[o + 5] = d;
            }

            var mesh = new Mesh { name = "WaterGrid" };
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = CreateVertexColorMaterial();
            mat.name = "WaterGridMat";
            var tex = FishGame.UI.LabArt.GridTile.texture;
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            mr.sharedMaterial = mat;
            mr.sortingOrder = waterSortingOrder + 2;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>
        /// 정점 색을 그대로 쓰는 머티리얼.
        ///
        /// 스프라이트 셰이더는 _MainTex를 곱하므로, 텍스처를 안 넣으면
        /// 환경에 따라 검게 나올 수 있다. 흰 1×1 텍스처를 넣어 정점 색만 남긴다.
        /// </summary>
        static Material CreateVertexColorMaterial()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            if (sh == null)
            {
                Debug.LogWarning("[WorldBuilder] 스프라이트 셰이더를 찾지 못했습니다. 물 색이 이상하게 보일 수 있습니다.");
                sh = Shader.Find("Unlit/Color");
            }

            var mat = new Material(sh) { name = "WaterVertexColor" };
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            return mat;
        }

        // ══════════════════════════════════════════════════════════
        //  벽
        // ══════════════════════════════════════════════════════════
        void BuildWalls(List<Vector2> left, List<Vector2> right)
        {
            _wallsGo = new GameObject("Walls");
            _wallsGo.transform.SetParent(transform, false);
            int wallLayer = LayerMask.NameToLayer("Wall");
            if (wallLayer >= 0) _wallsGo.layer = wallLayer;

            // 닫힌 고리: 왼쪽 위 → 왼쪽 아래 → 바닥 → 오른쪽 아래 → 오른쪽 위 → 천장
            var loop = new List<Vector2>(left.Count + right.Count + 2);
            loop.AddRange(left);
            for (int i = right.Count - 1; i >= 0; i--) loop.Add(right[i]);

            var edge = _wallsGo.AddComponent<EdgeCollider2D>();
            edge.edgeRadius = edgeRadius;
            edge.useAdjacentStartPoint = false;
            edge.useAdjacentEndPoint = false;

            // 첫 점을 끝에 한 번 더 넣어 고리를 완전히 닫는다 (천장·바닥 막기)
            loop.Add(loop[0]);
            edge.points = loop.ToArray();

            var rb = _wallsGo.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;

            DrawOutline("Outline_Left", left);
            DrawOutline("Outline_Right", right);
        }

        void DrawOutline(string name, List<Vector2> path)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_wallsGo.transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = path.Count;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.sortingOrder = outlineSortingOrder;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            var mat = CreateVertexColorMaterial();
            mat.name = "OutlineMat";
            lr.sharedMaterial = mat;
            lr.startColor = lr.endColor = outlineColor;

            for (int i = 0; i < path.Count; i++)
                lr.SetPosition(i, new Vector3(path[i].x, path[i].y, 0f));

            // 존이 넓을수록 선도 굵게 — 안 그러면 바다에서 실처럼 보인다
            float avgHalfWidth = 0f;
            for (int i = 0; i < path.Count; i++) avgHalfWidth += Mathf.Abs(path[i].x);
            avgHalfWidth = path.Count > 0 ? avgHalfWidth / path.Count : 20f;

            float w = Mathf.Max(outlineWidth, avgHalfWidth * outlineWidthPerHalfWidth);
            lr.widthMultiplier = w;
        }

        // ══════════════════════════════════════════════════════════
        //  장애물
        // ══════════════════════════════════════════════════════════
        /// <summary>
        /// 구역 데이터의 장애물을 실제로 놓는다.
        ///
        /// 배치에 두 가지 안전장치를 건다.
        ///   1) 벽을 뚫지 않도록 그 깊이의 반폭 안으로 밀어 넣는다
        ///   2) 아래 통로 입구를 막는 자리면 아예 건너뛴다 — 길이 막히면 판이 진행 불가가 된다
        /// </summary>
        void BuildObstacles()
        {
            Obstacle.ClearRegistry();
            _obstacles.Clear();
            _propsGo = _propsGo != null ? _propsGo : NewProps();

            for (int i = 0; i < _layout.ZoneCount; i++)
            {
                var s = _layout.GetSlice(i);
                var z = s.Zone;
                if (z == null || z.obstacles == null) continue;

                foreach (var def in z.obstacles)
                {
                    if (def == null) continue;

                    float t = Mathf.Clamp01(def.depthT);
                    float y = Mathf.Lerp(s.YTop, s.YBottom, t);
                    float hw = z.HalfWidthAt(t);
                    float half = def.size * 0.5f;

                    // ① 벽 안쪽으로 — 장애물이 벽에 걸치면 벽 바깥으로 삐져나와 보인다
                    float room = hw - half * 1.25f;
                    if (room <= 0f)
                    {
                        Debug.LogWarning($"[WorldBuilder] '{z.displayName}'의 장애물이 구역 폭보다 큽니다. 건너뜁니다.");
                        continue;
                    }
                    float x = Mathf.Clamp(def.sideRatio * hw, -room, room);

                    // ② 통로를 막지 않도록
                    if (s.HasCorridor && BlocksCorridor(s, new Vector2(x, y), half))
                    {
                        Debug.LogWarning($"[WorldBuilder] '{z.displayName}'의 장애물이 통로를 막아 건너뜁니다. " +
                                         $"depthT를 줄이거나 sideRatio를 키우세요.");
                        continue;
                    }

                    var ob = Obstacle.Create(_propsGo.transform, new Vector2(x, y),
                                             def.shape, def.size, def.rotation,
                                             z.wallColor, obstacleSortingOrder);
                    _obstacles.Add(ob);
                }
            }
        }

        /// <summary>
        /// 통로 입구로 들어가는 길을 막는가.
        /// 통로는 존 바닥 중앙의 좁은 목이라, 그 위쪽 깔때기 영역을 비워 둬야 한다.
        /// </summary>
        static bool BlocksCorridor(WorldLayout.Slice s, Vector2 pos, float halfSize)
        {
            // 입구 바로 위만 비우면 된다. 구역 중간쯤의 장애물은 옆으로 돌아가면 그만이라
            // 여기서 막으면 장애물이 거의 다 걷러져 버린다.
            float band = Mathf.Max(s.CorridorHalfWidth * 2.5f, s.Height * 0.10f);
            if (pos.y > s.YBottom + band) return false;

            float clearance = s.CorridorHalfWidth + halfSize;
            return Mathf.Abs(pos.x - s.CorridorCenterX) < clearance;
        }

        // ══════════════════════════════════════════════════════════
        //  통로 게이트
        // ══════════════════════════════════════════════════════════
        void BuildGates()
        {
            _propsGo = _propsGo != null ? _propsGo : NewProps();

            for (int i = 0; i < _layout.ZoneCount; i++)
            {
                var s = _layout.GetSlice(i);
                if (!s.HasCorridor || s.Zone == null) continue;

                var next = _layout.GetZone(i + 1);
                var gate = ZoneGate.Create(
                    _propsGo.transform,
                    new Vector3(s.CorridorCenterX, s.CorridorTop, 0f),
                    i,
                    s.Zone.exitRequiredSize,
                    s.CorridorHalfWidth,
                    s.Zone.displayName,
                    next != null ? next.displayName : "",
                    labelFont);
                _gates.Add(gate);
            }
        }

        GameObject NewProps()
        {
            var go = new GameObject("Props");
            go.transform.SetParent(transform, false);
            return go;
        }

        // ══════════════════════════════════════════════════════════
        //  보스 구조물
        // ══════════════════════════════════════════════════════════
        void BuildBossStructure()
        {
            _propsGo = _propsGo != null ? _propsGo : NewProps();

            for (int i = 0; i < _layout.ZoneCount; i++)
            {
                var z = _layout.GetZone(i);
                if (z == null || z.boss == null || !z.bossFromStructure) continue;

                _bossStructure = BossStructure.Create(
                    _propsGo.transform,
                    _layout.BossStructureWorldPos(i),
                    i, z.boss, z.bossStructureRadius);
                break;
            }
        }

        // ══════════════════════════════════════════════════════════
        //  히든 아이템
        // ══════════════════════════════════════════════════════════
        /// <summary>
        /// 구역마다 정해진 개수만큼. 위치는 존 인덱스로 시드를 고정해 항상 같은 자리에 둔다
        /// — 매번 바뀌면 "찾았다"는 느낌이 사라진다.
        /// </summary>
        void BuildHiddenItems(HashSet<string> collected)
        {
            _propsGo = _propsGo != null ? _propsGo : NewProps();

            for (int i = 0; i < _layout.ZoneCount; i++)
            {
                var s = _layout.GetSlice(i);
                var z = s.Zone;
                if (z == null || z.hiddenItemCount <= 0) continue;

                var rng = new System.Random(9173 + i * 7919);

                for (int k = 0; k < z.hiddenItemCount; k++)
                {
                    string id = $"{z.name}#{k}";
                    if (collected != null && collected.Contains(id)) continue;

                    // 존을 세로로 n등분해 한 칸에 하나씩 — 한쪽에 몰리지 않게
                    float band = (k + 0.5f) / z.hiddenItemCount;
                    float jitter = (float)(rng.NextDouble() - 0.5) * (0.6f / z.hiddenItemCount);
                    float t = Mathf.Clamp01(band + jitter);

                    float y = Mathf.Lerp(s.YTop, s.YBottom, t);
                    float hw = z.HalfWidthAt(t);
                    // 벽 가까이 — 구석을 뒤져야 나오도록
                    float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                    float x = side * hw * (float)(0.55 + rng.NextDouble() * 0.33);

                    // 존이 커질수록 아이템도 커야 눈에 띈다
                    float scale = Mathf.Clamp(hw * 0.055f, 1.2f, 9f);

                    // 장애물 안에 박히면 영영 못 먹는다 — 반대쪽으로 옮겨 본다
                    var pos = new Vector2(x, y);
                    if (Obstacle.Overlaps(pos, scale * 1.5f))
                    {
                        pos.x = -pos.x;
                        if (Obstacle.Overlaps(pos, scale * 1.5f))
                        {
                            pos = new Vector2(x * 0.45f, y);
                            if (Obstacle.Overlaps(pos, scale * 1.5f)) continue;
                        }
                    }

                    var item = HiddenItem.Create(_propsGo.transform,
                                                 new Vector3(pos.x, pos.y, 0f), id, i, scale);
                    _hiddenItems.Add(item);
                }
            }
        }
    }
}
