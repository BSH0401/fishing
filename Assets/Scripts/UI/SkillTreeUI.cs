using System.Collections.Generic;
using FishGame.Core;
using FishGame.Data;
using FishGame.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>
    /// 메인 화면의 스킬트리 — "수중 실험실" 계기판.
    /// GameDatabase.skillTree(기획 도면을 옮긴 칸 그래프)를 도면 좌표대로 배치하고,
    /// 칸 사이의 연결선을 그린다.
    ///
    /// 규칙: 선으로 이어진 칸 중 하나라도 찍혀 있어야 그 칸이 열린다.
    /// 선은 3단계다 — 어디까지 열렸는지가 선만 봐도 읽힌다.
    ///   청록 (빛 번짐 + 빛 알갱이가 오감)   양쪽 다 찍음
    ///   호박색 (찍은 쪽에서 빛이 흘러감)     한쪽만 찍음 → 반대쪽 칸이 지금 열려 있다
    ///   가라앉은 회청색                      잠김
    ///
    /// Content 안의 층 (뒤 → 앞):
    ///   청사진 모눈 · 모듈 판(3×3 덩어리) · 선 · 흐르는 빛 · 칸 · 폭발 연출
    /// 배경(바다·빛줄기·물방울·비네트)은 뷰포트에 붙은 UnderwaterBackdrop이 맡는다.
    /// 이 층들과 패널 스타일은 전부 실행 중에 만들어지므로 씬을 다시 생성할 필요가 없다.
    /// </summary>
    public class SkillTreeUI : MonoBehaviour
    {
        [Header("배치")]
        [SerializeField] RectTransform nodeContainer;
        [SerializeField] RectTransform lineContainer;
        [SerializeField] SkillNodeButton nodePrefab;
        [SerializeField] Image linePrefab;
        [Tooltip("도면 좌표 1당 UI 픽셀. 도면에서 칸 간격이 약 50이라 3이면 150px")]
        [SerializeField] float positionScale = 3f;

        [Header("스크롤 / 줌")]
        [Tooltip("ScrollRect의 Content. 트리 크기에 맞춰 자동으로 넓혀준다.")]
        [SerializeField] RectTransform scrollContent;
        [SerializeField] ScrollRect scrollRect;
        [Tooltip("트리 바깥쪽 여백")]
        [SerializeField] Vector2 contentPadding = new Vector2(260f, 220f);
        [Tooltip("켜면 시작할 때 트리 전체가 보이게 축소한다. 끄면 시작 칸을 가운데 두고 startZoom으로 연다.")]
        [SerializeField] bool fitOnStart = false;
        [SerializeField] float startZoom = 0.6f;
        [SerializeField] Button zoomInButton;
        [SerializeField] Button zoomOutButton;
        [SerializeField] Button fitButton;
        [SerializeField] float minZoom = 0.12f;
        [SerializeField] float maxZoom = 1.4f;
        [Tooltip("줌이 목표 배율을 따라가는 빠르기 (클수록 즉각적)")]
        [SerializeField] float zoomSharpness = 14f;

        float _zoom = 1f;
        float _targetZoom = 1f;
        bool _zooming;
        Vector2 _zoomAnchor;
        Camera _zoomCam;
        Vector2 _treeSize = Vector2.one;
        Vector2 _treeCenter = Vector2.zero;

        [Header("상세 패널")]
        [SerializeField] GameObject detailPanel;
        [SerializeField] TMP_Text detailName;
        [SerializeField] TMP_Text detailCategory;
        [SerializeField] TMP_Text detailDescription;
        [SerializeField] TMP_Text detailEffect;
        [SerializeField] TMP_Text detailRequirements;
        [SerializeField] TMP_Text detailCost;

        [Header("공통")]
        [SerializeField] TMP_Text currencyText;
        [SerializeField] TMP_Text hintText;
        [Tooltip("도형 범례를 채울 가로 줄 (비어 있으면 범례를 만들지 않는다)")]
        [SerializeField] RectTransform legendContainer;

        [Header("연결선")]
        [SerializeField] Color lineLitColor = new Color(0.40f, 1f, 0.86f, 0.95f);
        [SerializeField] Color lineOpenColor = new Color(1f, 0.76f, 0.34f, 0.55f);
        [SerializeField] Color lineFlowColor = new Color(1f, 0.86f, 0.45f, 0.95f);
        [SerializeField] Color lineLockedColor = new Color(0.30f, 0.46f, 0.52f, 0.38f);
        [SerializeField] float lineThicknessLit = 16f;
        [SerializeField] float lineThicknessOpen = 11f;
        [SerializeField] float lineThicknessLocked = 6f;
        [Tooltip("흐르는 빛의 속도 (px/초)와 간격 (px)")]
        [SerializeField] float flowSpeed = 80f;
        [SerializeField] float flowPeriod = 56f;
        [Tooltip("범례·떠오르는 글자에 쓸 TMP 프리팹 (선 라벨 프리팹을 재사용한다)")]
        [SerializeField] TMP_Text lineLabelPrefab;

        readonly List<SkillNodeButton> _buttons = new List<SkillNodeButton>();
        readonly Dictionary<int, SkillNodeButton> _buttonBySlot = new Dictionary<int, SkillNodeButton>();
        readonly List<LineView> _lines = new List<LineView>();
        readonly Dictionary<int, List<LineView>> _linesBySlot = new Dictionary<int, List<LineView>>();
        readonly List<PlateView> _plates = new List<PlateView>();
        SkillNodeButton _rootButton;
        int _hoverSlot = -1;

        RectTransform _plateLayer, _lineFxLayer, _topFxLayer;
        SkillTreeFx _fx;
        Image _detailIcon;
        float _currencyPunch;
        Color _currencyBaseColor = Color.white;
        float _currencyDeny;

        GameManager _game;
        string _notice;

        /// <summary>재화 표시를 이 창이 맡고 있으면 그 글자 (MainMenuUI가 같은 글자를 덮어쓰지 않게).</summary>
        public TMP_Text CurrencyLabel => _buttons.Count > 0 ? currencyText : null;

        class LineView
        {
            public Image Image;
            public RectTransform Rect;
            public int A, B;
            public Vector2 PA, PB;
            public RawImage Flow;
            public bool FlowForward;     // A → B 방향으로 흐르는가
        }

        class PlateView
        {
            public Image Fill;
            public Image Border;
            public TMP_Text Label;
            public List<int> Slots = new List<int>();
            public string Code;
        }

        void Start()
        {
            _game = GameManager.Instance;
            if (_game == null || _game.Database == null)
            {
                Debug.LogError("[SkillTreeUI] GameManager/Database가 없습니다. MainMenu 씬에 GameManager를 배치했는지 확인하세요.");
                return;
            }
            if (_game.Database.skillTree == null || _game.Database.skillTree.Count == 0)
            {
                Debug.LogError("[SkillTreeUI] 스킬트리 칸이 없습니다. [FishGame ▸ 1. 콘텐츠 에셋 생성]을 다시 실행하세요.");
                return;
            }

            if (zoomInButton != null)  zoomInButton.onClick.AddListener(() => ZoomAroundViewCenter(1.25f));
            if (zoomOutButton != null) zoomOutButton.onClick.AddListener(() => ZoomAroundViewCenter(1f / 1.25f));
            if (fitButton != null)     fitButton.onClick.AddListener(FitToView);

            _notice = _game.ConsumeNotice();
            if (currencyText != null) _currencyBaseColor = currencyText.color;

            ApplyLabStyle();
            Build();
            BuildLegend();
            _game.OnProgressChanged += RefreshAll;
            RefreshAll();
            ShowDetail(null);

            // 캔버스 레이아웃이 아직 안 잡혔을 수 있어 한 프레임 뒤에 맞춘다
            StartCoroutine(InitialViewNextFrame());
        }

        System.Collections.IEnumerator InitialViewNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (fitOnStart) FitToView();
            else FocusRoot(startZoom);
        }

        void OnDestroy()
        {
            if (_game != null) _game.OnProgressChanged -= RefreshAll;
        }

        // ══════════════════════════════════════════════════════════
        //  스타일 — 수중 실험실
        // ══════════════════════════════════════════════════════════
        void ApplyLabStyle()
        {
            // 스크롤 영역: 바다 배경 + 테두리
            if (scrollRect != null)
            {
                var viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;
                var bg = viewport.GetComponent<Image>();
                if (bg != null)
                {
                    bg.sprite = LabArt.Panel;
                    bg.type = Image.Type.Sliced;
                    bg.color = new Color(0.02f, 0.06f, 0.09f, 1f);
                }

                var backdrop = viewport.GetComponent<UnderwaterBackdrop>();
                if (backdrop == null) backdrop = viewport.gameObject.AddComponent<UnderwaterBackdrop>();
                backdrop.Build();

                // 테두리는 비네트보다도 앞
                if (viewport.Find("LabFrame") == null)
                {
                    var frame = NewImage(viewport, "LabFrame", LabArt.PanelBorder);
                    frame.type = Image.Type.Sliced;
                    frame.color = LabStyle.Border;
                    var fr = frame.rectTransform;
                    fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
                    fr.offsetMin = fr.offsetMax = Vector2.zero;
                    fr.SetAsLastSibling();
                }

                // 관성 스크롤을 부드럽게
                scrollRect.inertia = true;
                scrollRect.decelerationRate = 0.06f;
            }

            if (detailPanel != null)
            {
                LabStyle.Panel(detailPanel.GetComponent<Image>());
                if (_detailIcon == null)
                {
                    _detailIcon = NewImage((RectTransform)detailPanel.transform, "TypeIcon", null);
                    var r = _detailIcon.rectTransform;
                    r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
                    r.pivot = new Vector2(1f, 1f);
                    r.anchoredPosition = new Vector2(-18f, -16f);
                    r.sizeDelta = new Vector2(42f, 42f);
                    _detailIcon.preserveAspect = true;

                    // 이름이 아이콘 밑으로 파고들지 않게
                    if (detailName != null)
                    {
                        var nr = detailName.rectTransform;
                        nr.sizeDelta = new Vector2(Mathf.Max(120f, nr.sizeDelta.x - 76f), nr.sizeDelta.y);
                    }
                }
            }

            if (legendContainer != null && legendContainer.parent != null)
                LabStyle.Panel(legendContainer.parent.GetComponent<Image>(), corners: false);

            LabStyle.Button(zoomInButton);
            LabStyle.Button(zoomOutButton);
            LabStyle.Button(fitButton);
        }

        // ══════════════════════════════════════════════════════════
        //  생성
        // ══════════════════════════════════════════════════════════
        void Build()
        {
            foreach (Transform c in nodeContainer) Destroy(c.gameObject);
            if (lineContainer != null) foreach (Transform c in lineContainer) Destroy(c.gameObject);
            _buttons.Clear();
            _buttonBySlot.Clear();
            _lines.Clear();
            _linesBySlot.Clear();

            if (scrollContent == null) scrollContent = nodeContainer.parent as RectTransform;
            EnsureLayers();

            var db = _game.Database;
            var positions = new Vector2[db.skillTree.Count];
            var placed = new bool[db.skillTree.Count];

            // 좌표의 실제 범위를 재서 나중에 Content 크기를 맞춘다.
            // 이걸 안 하면 Content보다 트리가 커져서 바깥쪽 칸으로 스크롤이 안 간다.
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            for (int i = 0; i < db.skillTree.Count; i++)
            {
                var slot = db.skillTree[i];
                if (slot == null) continue;
                if (!slot.IsRoot && slot.skill == null) continue;

                var btn = Instantiate(nodePrefab, nodeContainer);
                var rt = (RectTransform)btn.transform;
                Vector2 pos = slot.position * positionScale;
                rt.anchoredPosition = pos;
                positions[i] = pos;
                placed[i] = true;

                min = Vector2.Min(min, pos);
                max = Vector2.Max(max, pos);

                btn.Bind(i, slot, OnNodeClicked, OnNodeHovered);
                _buttons.Add(btn);
                _buttonBySlot[i] = btn;
                if (slot.IsRoot) _rootButton = btn;
            }

            if (_buttons.Count > 0) FitContent(min, max);

            BuildPlates(positions, placed);

            if (lineContainer == null || linePrefab == null) return;

            for (int i = 0; i < db.skillTree.Count; i++)
            {
                var slot = db.skillTree[i];
                if (slot == null) continue;
                foreach (int j in slot.links)
                {
                    if (j <= i || j >= db.skillTree.Count) continue;   // 양방향이라 한 번만
                    if (!placed[i] || !placed[j]) continue;
                    var line = CreateLine(positions[i], positions[j], i, j);
                    _lines.Add(line);
                    AddLineIndex(i, line);
                    AddLineIndex(j, line);
                }
            }

            if (_fx == null) _fx = gameObject.AddComponent<SkillTreeFx>();
            _fx.Init(_lineFxLayer, _topFxLayer, lineLabelPrefab);
        }

        void AddLineIndex(int slot, LineView line)
        {
            if (!_linesBySlot.TryGetValue(slot, out var list)) _linesBySlot[slot] = list = new List<LineView>();
            list.Add(line);
        }

        /// <summary>Content 안의 층을 만든다: 모눈 · 모듈 판 · (선) · 흐르는 빛 · (칸) · 폭발.</summary>
        void EnsureLayers()
        {
            if (scrollContent == null) return;

            if (scrollContent.Find("Blueprint") == null)
            {
                var grid = NewImage(scrollContent, "Blueprint", LabArt.GridTile);
                grid.type = Image.Type.Tiled;
                grid.pixelsPerUnitMultiplier = 64f / (positionScale * 25f);   // 모눈 한 칸 = 칸 간격의 절반
                grid.color = new Color(0.45f, 0.95f, 0.95f, 0.07f);
                var gr = grid.rectTransform;
                gr.anchorMin = Vector2.zero; gr.anchorMax = Vector2.one;
                gr.offsetMin = gr.offsetMax = Vector2.zero;
                gr.SetAsFirstSibling();
            }

            if (_plateLayer == null)
            {
                _plateLayer = NewLayer("Plates");
                _plateLayer.SetSiblingIndex(lineContainer != null ? lineContainer.GetSiblingIndex() : 1);
            }
            if (_lineFxLayer == null)
            {
                _lineFxLayer = NewLayer("LineFx");
                _lineFxLayer.SetSiblingIndex(nodeContainer.GetSiblingIndex());          // 칸 바로 아래
            }
            if (_topFxLayer == null)
            {
                _topFxLayer = NewLayer("TopFx");
                _topFxLayer.SetAsLastSibling();
            }
        }

        RectTransform NewLayer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(scrollContent, false);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = Vector2.zero;
            return r;
        }

        /// <summary>
        /// 3×3 덩어리마다 실험실 모듈 판을 깐다.
        /// 가까운 칸끼리 묶은 뒤(단일 연결), 붙어 있는 덩어리는 9칸 단위로 다시 나눈다(k-평균).
        /// </summary>
        void BuildPlates(Vector2[] positions, bool[] placed)
        {
            foreach (var p in _plates) if (p.Fill != null) Destroy(p.Fill.gameObject);
            _plates.Clear();
            if (_plateLayer == null) return;

            var db = _game.Database;
            var nodes = new List<int>();
            for (int i = 0; i < positions.Length; i++)
            {
                if (!placed[i]) continue;
                var s = db.skillTree[i];
                if (s.IsRoot || s.skill.effectType.IsUnlock()) continue;   // 알약 칸은 판 밖
                nodes.Add(i);
            }

            // 1) 단일 연결 묶기 — 도면 좌표 58 이내면 같은 무리
            float link = 58f * positionScale;
            var parent = new Dictionary<int, int>();
            foreach (int i in nodes) parent[i] = i;
            int RootOf(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int a = 0; a < nodes.Count; a++)
            for (int b = a + 1; b < nodes.Count; b++)
                if ((positions[nodes[a]] - positions[nodes[b]]).sqrMagnitude <= link * link)
                    parent[RootOf(nodes[a])] = RootOf(nodes[b]);

            var groups = new Dictionary<int, List<int>>();
            foreach (int i in nodes)
            {
                int root = RootOf(i);
                if (!groups.TryGetValue(root, out var g)) groups[root] = g = new List<int>();
                g.Add(i);
            }

            // 2) 9칸보다 큰 무리는 k-평균으로 쪼갠다
            var clusters = new List<List<int>>();
            foreach (var g in groups.Values)
            {
                int k = Mathf.Max(1, Mathf.RoundToInt(g.Count / 9f));
                if (k == 1) { clusters.Add(g); continue; }
                clusters.AddRange(KMeans(g, positions, k));
            }

            // 위에서 아래, 왼쪽에서 오른쪽 순으로 번호를 매긴다
            clusters.Sort((x, y) =>
            {
                Vector2 cx = Centroid(x, positions), cy = Centroid(y, positions);
                return Mathf.Abs(cx.y - cy.y) > 1f ? cy.y.CompareTo(cx.y) : cx.x.CompareTo(cy.x);
            });

            const float padX = 62f, padTop = 58f, padBottom = 74f;
            int n = 0;
            foreach (var c in clusters)
            {
                Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = new Vector2(float.MinValue, float.MinValue);
                foreach (int i in c) { lo = Vector2.Min(lo, positions[i]); hi = Vector2.Max(hi, positions[i]); }
                lo -= new Vector2(padX, padBottom);
                hi += new Vector2(padX, padTop);

                var fill = NewImage(_plateLayer, $"Plate{n}", LabArt.Panel);
                LabStyle.Panel(fill, corners: true,
                               fill: new Color(0.03f, 0.13f, 0.16f, 0.62f),
                               border: new Color(0.35f, 0.85f, 0.85f, 0.22f),
                               corner: new Color(0.45f, 0.95f, 0.9f, 0.55f));
                var r = fill.rectTransform;
                r.anchoredPosition = (lo + hi) * 0.5f;
                r.sizeDelta = hi - lo;

                var view = new PlateView { Fill = fill, Code = $"LAB-{n + 1:00}" };
                view.Slots.AddRange(c);
                var border = fill.transform.Find("LabBorder");
                if (border != null) view.Border = border.GetComponent<Image>();

                if (lineLabelPrefab != null)
                {
                    var label = Instantiate(lineLabelPrefab, fill.transform);
                    label.raycastTarget = false;
                    label.fontSize = 15f;
                    label.fontStyle = FontStyles.Bold;
                    label.alignment = TextAlignmentOptions.TopLeft;
                    label.textWrappingMode = TextWrappingModes.NoWrap;
                    var lr = label.rectTransform;
                    lr.anchorMin = lr.anchorMax = new Vector2(0f, 1f);
                    lr.pivot = new Vector2(0f, 1f);
                    lr.anchoredPosition = new Vector2(22f, -8f);
                    lr.sizeDelta = new Vector2(200f, 22f);
                    view.Label = label;
                }
                _plates.Add(view);
                n++;
            }
        }

        static Vector2 Centroid(List<int> c, Vector2[] positions)
        {
            Vector2 sum = Vector2.zero;
            foreach (int i in c) sum += positions[i];
            return sum / Mathf.Max(1, c.Count);
        }

        static List<List<int>> KMeans(List<int> items, Vector2[] positions, int k)
        {
            // 가장 먼 점부터 차례로 중심을 고른다 — 덩어리가 또렷해서 이것만으로도 잘 갈린다
            var centers = new List<Vector2> { positions[items[0]] };
            while (centers.Count < k)
            {
                int best = items[0]; float bestD = -1f;
                foreach (int i in items)
                {
                    float d = float.MaxValue;
                    foreach (var c in centers) d = Mathf.Min(d, (positions[i] - c).sqrMagnitude);
                    if (d > bestD) { bestD = d; best = i; }
                }
                centers.Add(positions[best]);
            }

            var assign = new int[items.Count];
            for (int iter = 0; iter < 12; iter++)
            {
                for (int n = 0; n < items.Count; n++)
                {
                    float bestD = float.MaxValue;
                    for (int c = 0; c < k; c++)
                    {
                        float d = (positions[items[n]] - centers[c]).sqrMagnitude;
                        if (d < bestD) { bestD = d; assign[n] = c; }
                    }
                }
                for (int c = 0; c < k; c++)
                {
                    Vector2 sum = Vector2.zero; int cnt = 0;
                    for (int n = 0; n < items.Count; n++) if (assign[n] == c) { sum += positions[items[n]]; cnt++; }
                    if (cnt > 0) centers[c] = sum / cnt;
                }
            }

            var result = new List<List<int>>();
            for (int c = 0; c < k; c++) result.Add(new List<int>());
            for (int n = 0; n < items.Count; n++) result[assign[n]].Add(items[n]);
            result.RemoveAll(l => l.Count == 0);
            return result;
        }

        /// <summary>도형 범례: 도형 + 이름. 기획 도면 왼쪽 위의 범례와 같다.</summary>
        void BuildLegend()
        {
            if (legendContainer == null || lineLabelPrefab == null) return;
            foreach (Transform c in legendContainer) Destroy(c.gameObject);

            foreach (var node in _game.Database.skills)
            {
                if (node == null || node.effectType.IsUnlock()) continue;

                var item = new GameObject($"Legend_{node.id}", typeof(RectTransform));
                item.transform.SetParent(legendContainer, false);
                var h = item.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 4f;
                h.childAlignment = TextAnchor.MiddleLeft;
                h.childControlWidth = true; h.childControlHeight = true;
                h.childForceExpandWidth = false; h.childForceExpandHeight = false;

                var iconGo = new GameObject("Shape", typeof(RectTransform));
                iconGo.transform.SetParent(item.transform, false);
                var icon = iconGo.AddComponent<Image>();
                icon.sprite = SkillShapes.For(node.effectType);
                icon.color = SkillPalette.TypeColor(node.effectType);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                var le = iconGo.AddComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = 20f;

                var label = Instantiate(lineLabelPrefab, item.transform);
                label.text = ShortName(node);
                label.fontSize = 14f;
                label.fontStyle = FontStyles.Normal;
                label.color = LabStyle.TextDim;
                label.alignment = TextAlignmentOptions.Left;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                var lle = label.gameObject.AddComponent<LayoutElement>();
                lle.preferredWidth = label.GetPreferredValues(label.text).x + 2f;
            }
        }

        static string ShortName(SkillNode node)
        {
            string n = node.displayName;
            return n.Replace(" 강화", "").Replace("황금 미끼 ", "미끼 ").Replace("10만 볼트", "볼트");
        }

        /// <summary>
        /// 트리 전체를 감싸도록 Content 크기를 키우고, 트리를 그 중앙에 놓는다.
        /// ScrollRect는 Content 사각형 밖으로는 스크롤해주지 않기 때문에 반드시 필요하다.
        /// </summary>
        void FitContent(Vector2 min, Vector2 max)
        {
            Vector2 span = max - min;
            _treeSize = span + contentPadding * 2f;

            if (scrollContent != null) scrollContent.sizeDelta = _treeSize;

            // 트리의 중심을 Content 중심에 맞춘다
            Vector2 center = (min + max) * 0.5f;
            _treeCenter = center;
            foreach (var layer in new[] { nodeContainer, lineContainer, _plateLayer, _lineFxLayer, _topFxLayer })
                if (layer != null) layer.anchoredPosition = -center;
        }

        LineView CreateLine(Vector2 a, Vector2 b, int ia, int ib)
        {
            var line = Instantiate(linePrefab, lineContainer);
            line.sprite = LabArt.LineSoft;
            line.type = Image.Type.Simple;
            line.raycastTarget = false;
            var rt = (RectTransform)line.transform;

            Vector2 delta = b - a;
            rt.anchoredPosition = a + delta * 0.5f;
            rt.sizeDelta = new Vector2(delta.magnitude, lineThicknessLocked);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

            return new LineView { Image = line, Rect = rt, A = ia, B = ib, PA = a, PB = b };
        }

        // ══════════════════════════════════════════════════════════
        //  줌 — 목표 배율을 부드럽게 따라간다
        // ══════════════════════════════════════════════════════════
        /// <summary>트리 전체가 한 화면에 들어오도록 축소한다.</summary>
        public void FitToView()
        {
            if (scrollContent == null || scrollRect == null) return;

            var viewport = Viewport;
            Vector2 view = viewport.rect.size;
            if (view.x <= 1f || view.y <= 1f || _treeSize.x <= 1f) return;

            float fit = Mathf.Min(view.x / _treeSize.x, view.y / _treeSize.y);
            _zooming = false;
            SetZoom(Mathf.Min(1f, fit));
            scrollRect.velocity = Vector2.zero;
            scrollRect.normalizedPosition = new Vector2(0.5f, 0.5f);
        }

        /// <summary>시작 칸을 화면 가운데에 두고 주어진 배율로 본다.</summary>
        public void FocusRoot(float zoom)
        {
            _zooming = false;
            SetZoom(zoom);
            if (scrollContent == null || _rootButton == null) return;
            Vector2 rootInContent = ((RectTransform)_rootButton.transform).anchoredPosition - _treeCenter;
            scrollContent.anchoredPosition = -rootInContent * _zoom;
        }

        public void SetZoom(float value)
        {
            _zoom = Mathf.Clamp(value, minZoom, maxZoom);
            _targetZoom = _zoom;
            if (scrollContent != null) scrollContent.localScale = Vector3.one * _zoom;
        }

        RectTransform Viewport => scrollRect != null && scrollRect.viewport != null
            ? scrollRect.viewport
            : (scrollRect != null ? (RectTransform)scrollRect.transform : null);

        Camera CanvasCamera
        {
            get
            {
                var canvas = GetComponentInParent<Canvas>();
                if (canvas == null) return null;
                canvas = canvas.rootCanvas;
                return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            }
        }

        /// <summary>현재 배율에 factor를 곱한다. (버튼용 — 화면 중앙 기준)</summary>
        public void ZoomBy(float factor) => ZoomAroundViewCenter(factor);

        void ZoomAroundViewCenter(float factor)
        {
            var vp = Viewport;
            if (vp == null) { SetZoom(_zoom * factor); return; }
            var cam = CanvasCamera;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, vp.TransformPoint(vp.rect.center));
            ZoomBy(factor, screen, cam);
        }

        /// <summary>
        /// 커서 아래 지점을 그대로 둔 채 확대/축소한다 (휠 한 칸 = 목표 배율 변경, 실제 배율은 Update에서 따라감).
        /// 지도 UI에서 쓰는 방식으로, 중앙 기준 줌보다 훨씬 덜 어지럽다.
        /// </summary>
        public void ZoomBy(float factor, Vector2 screenPoint, Camera cam)
        {
            if (scrollContent == null) { SetZoom(_zoom * factor); return; }
            float from = _zooming ? _targetZoom : _zoom;
            _targetZoom = Mathf.Clamp(from * factor, minZoom, maxZoom);
            _zoomAnchor = screenPoint;
            _zoomCam = cam;
            _zooming = !Mathf.Approximately(_targetZoom, _zoom);
        }

        void ApplyZoomAt(float value)
        {
            // 줌 전후로 같은 화면 좌표가 Content의 어느 지점을 가리키는지 재고,
            // 그 차이만큼 Content를 밀어 커서 밑 지점을 고정한다.
            RectTransformUtility.ScreenPointToLocalPointInRectangle(scrollContent, _zoomAnchor, _zoomCam, out Vector2 before);
            _zoom = Mathf.Clamp(value, minZoom, maxZoom);
            scrollContent.localScale = Vector3.one * _zoom;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(scrollContent, _zoomAnchor, _zoomCam, out Vector2 after);
            scrollContent.anchoredPosition += (after - before) * _zoom;
        }

        // ══════════════════════════════════════════════════════════
        //  매 프레임
        // ══════════════════════════════════════════════════════════
        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            if (_zooming && scrollContent != null)
            {
                float next = Mathf.Lerp(_zoom, _targetZoom, 1f - Mathf.Exp(-zoomSharpness * dt));
                if (Mathf.Abs(next - _targetZoom) < 0.0005f) { next = _targetZoom; _zooming = false; }
                ApplyZoomAt(next);
            }

            // 열린 선 위로 빛이 흐른다 (찍은 쪽 → 열린 칸 쪽)
            float du = flowSpeed / Mathf.Max(1f, flowPeriod) * dt;
            foreach (var line in _lines)
            {
                if (line.Flow == null || !line.Flow.gameObject.activeSelf) continue;
                var uv = line.Flow.uvRect;
                uv.x -= du;
                if (uv.x < -1000f) uv.x += 1000f;
                line.Flow.uvRect = uv;
            }

            // 재화 글자: 구매하면 톡 튀고, 부족하면 붉게 깜빡
            if (currencyText != null && (_currencyPunch > 0f || _currencyDeny > 0f))
            {
                _currencyPunch = Mathf.Max(0f, _currencyPunch - dt * 4f);
                _currencyDeny = Mathf.Max(0f, _currencyDeny - dt * 2.5f);
                float s = 1f + Mathf.Sin((1f - _currencyPunch) * Mathf.PI) * 0.12f * (_currencyPunch > 0f ? 1f : 0f);
                currencyText.transform.localScale = new Vector3(s, s, 1f);
                currencyText.color = Color.Lerp(_currencyBaseColor, new Color(1f, 0.35f, 0.3f),
                                                Mathf.PingPong(_currencyDeny * 4f, 1f) * (_currencyDeny > 0f ? 1f : 0f));
            }
        }

        // ══════════════════════════════════════════════════════════
        //  갱신
        // ══════════════════════════════════════════════════════════
        public void RefreshAll()
        {
            if (_game == null) return;

            var db = _game.Database;
            var progress = _game.Progress;
            int affordable = 0;

            foreach (var btn in _buttons)
            {
                btn.Refresh(db, progress);
                if (btn.Affordable) affordable++;
            }

            RefreshLines();
            RefreshPlates();

            if (currencyText != null)
            {
                double next = SkillTreeManager.NextNodeBaseCost(progress);
                currencyText.text =
                    $"{NumberFormatter.Format(progress.currency)}" +
                    $"   <size=60%><color=#8FB7B8>다음 칸 {NumberFormatter.Format(next)}</color></size>";
            }

            if (hintText != null)
            {
                string count = $"찍은 칸 {progress.totalNodesPurchased} / {db.PurchasableSlotCount}";
                string main = affordable > 0
                    ? $"<color=#6FF2C8>지금 찍을 수 있는 칸 {affordable}개</color>  ·  {count}"
                    : $"<color=#8FA7AB>재화를 더 모아야 합니다</color>  ·  {count}";
                hintText.text = string.IsNullOrEmpty(_notice) ? main : $"<color=#F2C74C>{_notice}</color>\n{main}";
            }
        }

        void RefreshLines()
        {
            var db = _game.Database;
            var progress = _game.Progress;
            var lit = new List<SkillTreeFx.Segment>();

            foreach (var line in _lines)
            {
                bool a = SkillTreeManager.IsPurchased(db, line.A, progress);
                bool b = SkillTreeManager.IsPurchased(db, line.B, progress);
                bool hover = _hoverSlot >= 0 && (line.A == _hoverSlot || line.B == _hoverSlot);

                Color c; float thick;
                if (a && b) { c = lineLitColor; thick = lineThicknessLit; lit.Add(new SkillTreeFx.Segment { A = line.PA, B = line.PB }); }
                else if (a || b) { c = lineOpenColor; thick = lineThicknessOpen; }
                else { c = lineLockedColor; thick = lineThicknessLocked; }

                if (hover)
                {
                    c = Color.Lerp(c, Color.white, 0.35f);
                    c.a = Mathf.Min(1f, c.a + 0.35f);
                    thick += 4f;
                }

                line.Image.color = c;
                var size = line.Rect.sizeDelta;
                size.y = thick;
                line.Rect.sizeDelta = size;

                SetFlow(line, a != b, a);
            }

            _fx?.SetLitSegments(lit);
        }

        /// <summary>한쪽만 찍힌 선에 흐르는 빛을 켠다. 빛은 찍은 쪽에서 열린 칸 쪽으로 흐른다.</summary>
        void SetFlow(LineView line, bool on, bool fromA)
        {
            if (!on)
            {
                if (line.Flow != null) line.Flow.gameObject.SetActive(false);
                return;
            }
            if (_lineFxLayer == null) return;

            if (line.Flow == null)
            {
                var go = new GameObject("Flow", typeof(RectTransform));
                go.transform.SetParent(_lineFxLayer, false);
                go.transform.SetAsFirstSibling();                 // 빛 알갱이보다 뒤
                line.Flow = go.AddComponent<RawImage>();
                line.Flow.texture = LabArt.Dash;
                line.Flow.raycastTarget = false;
            }

            Vector2 from = fromA ? line.PA : line.PB;
            Vector2 to = fromA ? line.PB : line.PA;
            Vector2 delta = to - from;
            var r = line.Flow.rectTransform;
            r.anchoredPosition = from + delta * 0.5f;
            r.sizeDelta = new Vector2(delta.magnitude, lineThicknessOpen + 2f);
            r.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

            var uv = line.Flow.uvRect;
            uv.width = delta.magnitude / Mathf.Max(1f, flowPeriod);
            uv.height = 1f;
            line.Flow.uvRect = uv;
            line.Flow.color = lineFlowColor;
            line.Flow.gameObject.SetActive(true);
            line.FlowForward = fromA;
        }

        void RefreshPlates()
        {
            var db = _game.Database;
            var progress = _game.Progress;
            foreach (var p in _plates)
            {
                int bought = 0, open = 0;
                foreach (int i in p.Slots)
                {
                    if (SkillTreeManager.IsPurchased(db, i, progress)) bought++;
                    else if (SkillTreeManager.IsReachable(db, i, progress)) open++;
                }
                bool done = bought == p.Slots.Count;
                bool active = bought > 0 || open > 0;

                if (p.Border != null)
                    p.Border.color = done ? new Color(1f, 0.84f, 0.4f, 0.75f)
                                   : active ? new Color(0.40f, 0.95f, 0.9f, 0.45f)
                                   : new Color(0.35f, 0.75f, 0.8f, 0.14f);
                p.Fill.color = done ? new Color(0.08f, 0.16f, 0.14f, 0.7f)
                             : active ? new Color(0.03f, 0.15f, 0.18f, 0.66f)
                             : new Color(0.02f, 0.08f, 0.11f, 0.55f);

                if (p.Label != null)
                {
                    string hex = done ? "FFD666" : active ? "7FF0E0" : "4E7478";
                    p.Label.text = $"<color=#{hex}>{p.Code}</color>  <color=#{(active ? "B8D8D8" : "4E7478")}>{bought}/{p.Slots.Count}</color>";
                }
            }
        }

        // ══════════════════════════════════════════════════════════
        //  상호작용
        // ══════════════════════════════════════════════════════════
        void OnNodeClicked(SkillNodeButton btn)
        {
            if (btn == null || btn.Slot == null || btn.Slot.IsRoot) return;

            var db = _game.Database;
            var progress = _game.Progress;

            // 이번 구매로 새로 열릴 이웃을 미리 알아 둔다
            var closedNeighbors = new List<int>();
            foreach (int n in btn.Slot.links)
                if (!SkillTreeManager.IsReachable(db, n, progress)) closedNeighbors.Add(n);

            SkillTreeManager.CanPurchase(db, btn.SlotIndex, progress, out double cost);
            var result = SkillTreeManager.Purchase(btn.SlotIndex, _game);
            var bank = AudioManager.Instance.Bank;
            Vector2 pos = ((RectTransform)btn.transform).anchoredPosition;

            if (result == PurchaseResult.Success)
            {
                _notice = null;   // 첫 구매 뒤에는 안내 문구를 내린다
                if (bank?.purchase != null) AudioManager.Play(bank.purchase);

                btn.PlayPurchase();
                _currencyPunch = 1f;
                _fx?.PlayPurchase(pos, btn.TypeColor, btn.IsPill ? null : btn.ShapeSprite,
                                  Mathf.Min(btn.Size.x, btn.Size.y), $"-{NumberFormatter.Format(cost)}");

                int k = 0;
                foreach (int n in closedNeighbors)
                {
                    if (!_buttonBySlot.TryGetValue(n, out var nb)) continue;
                    float delay = 0.08f + 0.07f * k++;
                    Vector2 to = ((RectTransform)nb.transform).anchoredPosition;
                    float travel = Mathf.Max(0.12f, Vector2.Distance(pos, to) / 900f);
                    nb.PlayUnlock(delay + travel);
                    _fx?.PlayUnlockTravel(pos, to, delay, Mathf.Min(nb.Size.x, nb.Size.y));
                }
            }
            else
            {
                if (bank?.purchaseFail != null) AudioManager.Play(bank.purchaseFail, 0.6f);
                if (result != PurchaseResult.MaxLevel)
                {
                    btn.PlayDenied();
                    _fx?.PlayDenied(pos, Mathf.Min(btn.Size.x, btn.Size.y));
                    if (result == PurchaseResult.NotEnoughCurrency) _currencyDeny = 1f;
                }
            }

            ShowDetail(btn);
        }

        void OnNodeHovered(SkillNodeButton btn)
        {
            int slot = btn != null ? btn.SlotIndex : -1;
            if (slot != _hoverSlot)
            {
                _hoverSlot = slot;
                if (_game != null) RefreshLines();
            }
            ShowDetail(btn);
        }

        void ShowDetail(SkillNodeButton btn)
        {
            if (detailPanel == null) return;

            if (btn == null || btn.Slot == null)
            {
                detailPanel.SetActive(false);
                return;
            }

            detailPanel.SetActive(true);
            var db = _game.Database;
            var progress = _game.Progress;

            if (_detailIcon != null)
            {
                _detailIcon.sprite = btn.ShapeSprite;
                _detailIcon.color = btn.TypeColor;
                _detailIcon.rectTransform.sizeDelta = btn.IsPill ? new Vector2(70f, 28f) : new Vector2(42f, 42f);
            }

            if (btn.Slot.IsRoot)
            {
                if (detailName != null) { detailName.text = "시작"; detailName.color = Color.white; }
                if (detailCategory != null) detailCategory.text = $"찍은 칸 {progress.totalNodesPurchased} / {db.PurchasableSlotCount}";
                if (detailDescription != null)
                    detailDescription.text = "여기서부터 뻗어 나갑니다.\n선으로 이어진 칸 중 하나라도 찍으면 그 칸이 열립니다.";
                if (detailEffect != null) detailEffect.text = "";
                if (detailRequirements != null) detailRequirements.gameObject.SetActive(false);
                if (detailCost != null) detailCost.text = "";
                return;
            }

            var node = btn.Node;
            int level = progress.GetSkillLevel(node.id);
            int max = Mathf.Max(1, db.SlotCountOf(node));
            var state = SkillTreeManager.CanPurchase(db, btn.SlotIndex, progress, out double cost);

            if (detailName != null)
            {
                detailName.text = node.displayName;
                detailName.color = SkillPalette.TypeColor(node.effectType);
            }

            if (detailCategory != null)
            {
                detailCategory.text = node.effectType.IsUnlock()
                    ? SkillPalette.CategoryName(node.effectType)
                    : $"{SkillPalette.CategoryName(node.effectType)}  ·  이 종류 {level}/{max}칸 찍음";
            }

            if (detailDescription != null)
                detailDescription.text = string.IsNullOrEmpty(node.tooltip)
                    ? node.description
                    : $"<i>{node.tooltip}</i>\n{node.description}";

            if (detailEffect != null)
            {
                if (node.effectType.IsUnlock())
                {
                    detailEffect.text = level > 0 ? "해금 완료" : "미해금";
                }
                else
                {
                    string cur = node.FormatEffect(level);
                    string next = state != PurchaseResult.MaxLevel
                        ? $"   →   <color=#6FF2C8>{node.FormatEffect(level + 1)}</color>" : "";
                    detailEffect.text = $"합계 {cur}{next}";
                }
            }

            if (detailRequirements != null)
            {
                bool locked = state == PurchaseResult.PrerequisiteLocked;
                detailRequirements.gameObject.SetActive(locked);
                if (locked)
                    detailRequirements.text = "<color=#E08A72>잠김 — 선으로 이어진 칸 중\n하나를 먼저 찍어야 합니다</color>";
            }

            if (detailCost != null)
            {
                detailCost.text = state switch
                {
                    PurchaseResult.MaxLevel           => "<color=#F2C74C>찍음</color>",
                    PurchaseResult.PrerequisiteLocked => "<color=#E08A72>잠김</color>",
                    PurchaseResult.NotEnoughCurrency  => $"<color=#E08A72>비용 {NumberFormatter.Format(cost)} — 재화 부족</color>",
                    _                                 => $"<color=#6FF2C8>비용 {NumberFormatter.Format(cost)} — 클릭해서 구매</color>",
                };
            }
        }

        // ── 헬퍼 ────────────────────────────────────────────────
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
