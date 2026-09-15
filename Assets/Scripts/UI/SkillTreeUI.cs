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
    /// 메인 화면의 스킬트리.
    /// GameDatabase.skills를 읽어 gridPosition대로 자동 배치하고,
    /// prerequisites를 따라 연결선을 그린다.
    ///
    /// 선 색은 3단계다 — 이게 트리를 읽는 핵심이다.
    ///   흐림   선행을 아직 안 찍음
    ///   노랑   찍긴 했는데 요구 레벨에 못 미침  (예: 3/8)
    ///   초록   조건 충족
    /// </summary>
    public class SkillTreeUI : MonoBehaviour
    {
        [Header("배치")]
        [SerializeField] RectTransform nodeContainer;
        [SerializeField] RectTransform lineContainer;
        [SerializeField] SkillNodeButton nodePrefab;
        [SerializeField] Image linePrefab;
        [SerializeField] Vector2 gridSpacing = new Vector2(150f, 130f);

        [Header("스크롤 / 줌")]
        [Tooltip("ScrollRect의 Content. 트리 크기에 맞춰 자동으로 넓혀준다.")]
        [SerializeField] RectTransform scrollContent;
        [SerializeField] ScrollRect scrollRect;
        [Tooltip("트리 바깥쪽 여백")]
        [SerializeField] Vector2 contentPadding = new Vector2(180f, 150f);
        [Tooltip("시작할 때 트리 전체가 보이도록 자동으로 축소한다")]
        [SerializeField] bool fitOnStart = true;
        [SerializeField] Button zoomInButton;
        [SerializeField] Button zoomOutButton;
        [SerializeField] Button fitButton;
        [SerializeField] float minZoom = 0.35f;
        [SerializeField] float maxZoom = 1.3f;

        float _zoom = 1f;
        Vector2 _treeSize = Vector2.one;

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

        [Header("연결선")]
        [SerializeField] Color lineMetColor = new Color(0.36f, 0.82f, 0.50f, 0.95f);
        [SerializeField] Color linePartialColor = new Color(0.95f, 0.78f, 0.32f, 0.75f);
        [SerializeField] Color lineLockedColor = new Color(1f, 1f, 1f, 0.13f);
        [SerializeField] float lineThicknessMet = 6f;
        [SerializeField] float lineThicknessLocked = 3f;
        [Tooltip("요구 레벨이 2 이상인 선에 '3/8' 같은 라벨을 붙인다")]
        [SerializeField] bool showRequirementLabels = true;
        [SerializeField] TMP_Text lineLabelPrefab;

        readonly List<SkillNodeButton> _buttons = new List<SkillNodeButton>();
        readonly List<LineView> _lines = new List<LineView>();

        GameManager _game;

        class LineView
        {
            public Image Image;
            public RectTransform Rect;
            public TMP_Text Label;
            public SkillNode From;
            public SkillNode To;
            public int RequiredLevel;
        }

        void Start()
        {
            _game = GameManager.Instance;
            if (_game == null || _game.Database == null)
            {
                Debug.LogError("[SkillTreeUI] GameManager/Database가 없습니다. MainMenu 씬에 GameManager를 배치했는지 확인하세요.");
                return;
            }

            if (zoomInButton != null)  zoomInButton.onClick.AddListener(() => SetZoom(_zoom * 1.2f));
            if (zoomOutButton != null) zoomOutButton.onClick.AddListener(() => SetZoom(_zoom / 1.2f));
            if (fitButton != null)     fitButton.onClick.AddListener(FitToView);

            Build();
            _game.OnProgressChanged += RefreshAll;
            RefreshAll();
            ShowDetail(null);

            // 캔버스 레이아웃이 아직 안 잡혔을 수 있어 한 프레임 뒤에 맞춘다
            if (fitOnStart) StartCoroutine(FitNextFrame());
        }

        System.Collections.IEnumerator FitNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            FitToView();
        }

        void OnDestroy()
        {
            if (_game != null) _game.OnProgressChanged -= RefreshAll;
        }

        // ── 생성 ────────────────────────────────────────────────
        void Build()
        {
            foreach (Transform c in nodeContainer) Destroy(c.gameObject);
            if (lineContainer != null) foreach (Transform c in lineContainer) Destroy(c.gameObject);
            _buttons.Clear();
            _lines.Clear();

            var lookup = new Dictionary<SkillNode, RectTransform>();

            // 그리드 좌표의 실제 범위를 재서 나중에 Content 크기를 맞춘다.
            // 이걸 안 하면 Content보다 트리가 커져서 바깥쪽 노드로 스크롤이 안 간다.
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            foreach (var node in _game.Database.skills)
            {
                if (node == null) continue;
                var btn = Instantiate(nodePrefab, nodeContainer);
                var rt = (RectTransform)btn.transform;
                Vector2 pos = new Vector2(
                    node.gridPosition.x * gridSpacing.x,
                    node.gridPosition.y * gridSpacing.y);
                rt.anchoredPosition = pos;

                min = Vector2.Min(min, pos);
                max = Vector2.Max(max, pos);

                btn.Bind(node, OnNodeClicked, OnNodeHovered);
                _buttons.Add(btn);
                lookup[node] = rt;
            }

            if (_buttons.Count > 0) FitContent(min, max);

            if (lineContainer == null || linePrefab == null) return;

            foreach (var node in _game.Database.skills)
            {
                if (node == null) continue;
                foreach (var req in node.prerequisites)
                {
                    if (req?.node == null) continue;
                    if (!lookup.ContainsKey(req.node) || !lookup.ContainsKey(node)) continue;
                    _lines.Add(CreateLine(lookup[req.node].anchoredPosition,
                                          lookup[node].anchoredPosition, req.node, node, req.level));
                }
            }
        }

        /// <summary>
        /// 트리 전체를 감싸도록 Content 크기를 키우고, 트리를 그 중앙에 놓는다.
        /// ScrollRect는 Content 사각형 밖으로는 스크롤해주지 않기 때문에 반드시 필요하다.
        /// </summary>
        void FitContent(Vector2 min, Vector2 max)
        {
            Vector2 span = max - min;
            _treeSize = span + contentPadding * 2f;

            if (scrollContent == null) scrollContent = nodeContainer.parent as RectTransform;
            if (scrollContent != null) scrollContent.sizeDelta = _treeSize;

            // 트리의 중심을 Content 중심에 맞춘다
            Vector2 center = (min + max) * 0.5f;
            if (nodeContainer != null) nodeContainer.anchoredPosition = -center;
            if (lineContainer != null) lineContainer.anchoredPosition = -center;
        }

        /// <summary>트리 전체가 한 화면에 들어오도록 축소한다.</summary>
        public void FitToView()
        {
            if (scrollContent == null || scrollRect == null) return;

            var viewport = scrollRect.viewport != null
                ? scrollRect.viewport
                : (RectTransform)scrollRect.transform;

            Vector2 view = viewport.rect.size;
            if (view.x <= 1f || view.y <= 1f || _treeSize.x <= 1f) return;

            float fit = Mathf.Min(view.x / _treeSize.x, view.y / _treeSize.y);
            SetZoom(Mathf.Min(1f, fit));

            scrollRect.normalizedPosition = new Vector2(0.5f, 0.5f);
        }

        public void SetZoom(float value)
        {
            _zoom = Mathf.Clamp(value, minZoom, maxZoom);
            if (scrollContent != null) scrollContent.localScale = Vector3.one * _zoom;
        }

        /// <summary>현재 배율에 factor를 곱한다. (버튼용 — 화면 중앙 기준)</summary>
        public void ZoomBy(float factor) => SetZoom(_zoom * factor);

        /// <summary>
        /// 커서 아래 지점을 그대로 둔 채 확대/축소한다.
        /// 지도 UI에서 쓰는 방식으로, 중앙 기준 줌보다 훨씬 덜 어지럽다.
        /// </summary>
        public void ZoomBy(float factor, Vector2 screenPoint, Camera cam)
        {
            if (scrollContent == null) { ZoomBy(factor); return; }

            float target = Mathf.Clamp(_zoom * factor, minZoom, maxZoom);
            if (Mathf.Approximately(target, _zoom)) return;

            // 줌 전후로 같은 화면 좌표가 Content의 어느 지점을 가리키는지 재고,
            // 그 차이만큼 Content를 밀어 커서 밑 지점을 고정한다.
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                scrollContent, screenPoint, cam, out Vector2 before);

            SetZoom(target);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                scrollContent, screenPoint, cam, out Vector2 after);

            scrollContent.anchoredPosition += (after - before) * target;
        }

        LineView CreateLine(Vector2 a, Vector2 b, SkillNode from, SkillNode to, int requiredLevel)
        {
            var line = Instantiate(linePrefab, lineContainer);
            var rt = (RectTransform)line.transform;

            Vector2 delta = b - a;
            Vector2 mid = a + delta * 0.5f;
            rt.anchoredPosition = mid;
            rt.sizeDelta = new Vector2(delta.magnitude, lineThicknessLocked);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            rt.SetAsFirstSibling();

            TMP_Text label = null;
            if (showRequirementLabels && lineLabelPrefab != null && requiredLevel > 1)
            {
                label = Instantiate(lineLabelPrefab, lineContainer);
                var lrt = label.rectTransform;
                lrt.anchoredPosition = mid;   // 선은 회전하지만 라벨은 똑바로 둔다
                lrt.localRotation = Quaternion.identity;
            }

            return new LineView
            {
                Image = line, Rect = rt, Label = label,
                From = from, To = to, RequiredLevel = requiredLevel
            };
        }

        // ── 갱신 ────────────────────────────────────────────────
        public void RefreshAll()
        {
            if (_game == null) return;

            var progress = _game.Progress;
            int affordable = 0;

            foreach (var btn in _buttons)
            {
                btn.Refresh(progress);
                if (btn.Affordable) affordable++;
            }

            foreach (var line in _lines)
            {
                int have = progress.GetSkillLevel(line.From.id);
                bool met = have >= line.RequiredLevel;
                bool started = have > 0;

                line.Image.color = met ? lineMetColor : (started ? linePartialColor : lineLockedColor);

                var size = line.Rect.sizeDelta;
                size.y = met ? lineThicknessMet : lineThicknessLocked;
                line.Rect.sizeDelta = size;

                if (line.Label != null)
                {
                    line.Label.gameObject.SetActive(!met);
                    if (!met)
                    {
                        line.Label.text = $"{have}/{line.RequiredLevel}";
                        line.Label.color = started ? linePartialColor : new Color(0.6f, 0.62f, 0.66f, 0.9f);
                    }
                }
            }

            if (currencyText != null)
            {
                double next = SkillTreeManager.NextNodeBaseCost(progress);
                currencyText.text =
                    $"{NumberFormatter.Format(progress.currency)}" +
                    $"   <size=60%>다음 노드 {NumberFormatter.Format(next)}</size>";
            }

            if (hintText != null)
            {
                hintText.text = affordable > 0
                    ? $"<color=#5ED18A>지금 찍을 수 있는 노드 {affordable}개</color>  ·  " +
                      $"찍은 노드 {progress.totalNodesPurchased}개"
                    : $"<color=#9AA3AB>재화를 더 모아야 합니다</color>  ·  " +
                      $"찍은 노드 {progress.totalNodesPurchased}개";
            }
        }

        // ── 상호작용 ────────────────────────────────────────────
        void OnNodeClicked(SkillNodeButton btn)
        {
            if (btn?.Node == null) return;

            var result = SkillTreeManager.Purchase(btn.Node, _game);
            var bank = AudioManager.Instance.Bank;

            if (result == PurchaseResult.Success)
            {
                if (bank?.purchase != null) AudioManager.Play(bank.purchase);
            }
            else if (bank?.purchaseFail != null)
            {
                AudioManager.Play(bank.purchaseFail, 0.6f);
            }

            ShowDetail(btn.Node);
        }

        void OnNodeHovered(SkillNodeButton btn) => ShowDetail(btn?.Node);

        void ShowDetail(SkillNode node)
        {
            if (detailPanel == null) return;

            if (node == null)
            {
                detailPanel.SetActive(false);
                return;
            }

            detailPanel.SetActive(true);
            var progress = _game.Progress;
            int level = progress.GetSkillLevel(node.id);
            var state = SkillTreeManager.CanPurchase(node, progress, out double cost);

            if (detailName != null)
            {
                detailName.text = node.displayName;
                detailName.color = SkillPalette.CategoryColor(node.effectType);
            }

            if (detailCategory != null)
            {
                detailCategory.text = node.effectType.IsUnlock()
                    ? SkillPalette.CategoryName(node.effectType)
                    : $"{SkillPalette.CategoryName(node.effectType)}  ·  {level}/{node.EffectiveMaxLevel}";
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
                    string next = level < node.EffectiveMaxLevel
                        ? $"   →   <color=#5ED18A>{node.FormatEffect(level + 1)}</color>" : "";
                    detailEffect.text = $"현재 {cur}{next}";
                }
            }

            // ★ 부족한 선행 조건을 그대로 보여준다 — 선 색만으로는 몇 레벨이 필요한지 알 수 없다
            if (detailRequirements != null)
            {
                string reqs = SkillPalette.DescribeMissingRequirements(node, progress);
                detailRequirements.gameObject.SetActive(!string.IsNullOrEmpty(reqs));
                detailRequirements.text = reqs;
            }

            if (detailCost != null)
            {
                detailCost.text = state switch
                {
                    PurchaseResult.MaxLevel           => "<color=#F2C74C>최대 레벨</color>",
                    PurchaseResult.PrerequisiteLocked => "<color=#E08A72>선행 조건 미충족</color>",
                    PurchaseResult.NotEnoughCurrency  => $"<color=#E08A72>비용 {NumberFormatter.Format(cost)} — 재화 부족</color>",
                    _                                 => $"<color=#5ED18A>비용 {NumberFormatter.Format(cost)} — 클릭해서 구매</color>",
                };
            }
        }
    }
}
