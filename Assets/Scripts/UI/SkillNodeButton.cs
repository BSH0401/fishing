using System;
using System.Text;
using FishGame.Core;
using FishGame.Data;
using FishGame.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>
    /// 스킬트리의 노드 버튼 하나. SkillTreeUI가 프리팹으로 찍어낸다.
    ///
    /// 한눈에 읽혀야 하는 것 4가지:
    ///   이름 · 지금 살 수 있는가 · 몇 레벨인가 · 무엇을 먼저 찍어야 하는가
    /// </summary>
    public class SkillNodeButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] Button button;
        [SerializeField] Image iconImage;
        [SerializeField] Image frameImage;
        [SerializeField] Image fillImage;      // 레벨 진행도 (아래에서 위로 차오름)
        [SerializeField] Image glowImage;      // 살 수 있을 때 은은하게 빛남
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text levelText;
        [SerializeField] TMP_Text costText;
        [SerializeField] GameObject lockIcon;

        [Header("상태 색")]
        [SerializeField] Color affordableColor = new Color(0.36f, 0.82f, 0.50f);
        [SerializeField] Color unaffordableColor = new Color(0.46f, 0.50f, 0.56f);
        [SerializeField] Color lockedColor = new Color(0.24f, 0.25f, 0.28f);
        [SerializeField] Color maxedColor = new Color(0.95f, 0.78f, 0.30f);

        public SkillNode Node { get; private set; }
        /// <summary>지금 바로 살 수 있는가 (SkillTreeUI가 강조에 쓴다).</summary>
        public bool Affordable { get; private set; }

        Action<SkillNodeButton> _onClicked;
        Action<SkillNodeButton> _onHovered;
        float _glowPhase;

        public void Bind(SkillNode node, Action<SkillNodeButton> onClicked, Action<SkillNodeButton> onHovered)
        {
            Node = node;
            _onClicked = onClicked;
            _onHovered = onHovered;
            _glowPhase = UnityEngine.Random.value * Mathf.PI * 2f;

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => _onClicked?.Invoke(this));
            }
            if (iconImage != null)
            {
                iconImage.sprite = node.icon;
                iconImage.enabled = node.icon != null;
                iconImage.color = SkillPalette.CategoryColor(node.effectType);
            }
            if (nameText != null) nameText.text = node.displayName;

            name = $"Node_{node.id}";
        }

        void Update()
        {
            if (glowImage == null) return;

            if (!Affordable)
            {
                if (glowImage.gameObject.activeSelf) glowImage.gameObject.SetActive(false);
                return;
            }

            if (!glowImage.gameObject.activeSelf) glowImage.gameObject.SetActive(true);

            // 살 수 있는 노드만 은은하게 맥동 — 어디를 눌러야 할지 바로 보인다
            _glowPhase += Time.unscaledDeltaTime * 3.2f;
            var c = glowImage.color;
            c.a = 0.25f + 0.22f * (0.5f + 0.5f * Mathf.Sin(_glowPhase));
            glowImage.color = c;
        }

        public void Refresh(PlayerProgress progress)
        {
            if (Node == null || progress == null) return;

            int level = progress.GetSkillLevel(Node.id);
            int max = Node.EffectiveMaxLevel;
            var state = SkillTreeManager.CanPurchase(Node, progress, out double cost);

            Affordable = state == PurchaseResult.Success;

            if (levelText != null)
                levelText.text = Node.effectType.IsUnlock()
                    ? (level > 0 ? "해금" : "―")
                    : $"{level}/{max}";

            if (costText != null)
            {
                bool showCost = state != PurchaseResult.MaxLevel && state != PurchaseResult.PrerequisiteLocked;
                costText.gameObject.SetActive(showCost);
                if (showCost)
                {
                    costText.text = NumberFormatter.Format(cost);
                    costText.color = Affordable
                        ? new Color(0.98f, 0.88f, 0.45f)
                        : new Color(0.72f, 0.48f, 0.44f);   // 못 사면 붉게
                }
            }

            if (lockIcon != null)
                lockIcon.SetActive(state == PurchaseResult.PrerequisiteLocked);

            // 레벨 진행도 — 아래에서 위로 차오른다
            if (fillImage != null)
            {
                fillImage.fillAmount = max > 0 ? level / (float)max : 0f;
                fillImage.color = SkillPalette.CategoryColor(Node.effectType) * 0.55f;
            }

            if (frameImage != null)
            {
                frameImage.color = state switch
                {
                    PurchaseResult.Success            => affordableColor,
                    PurchaseResult.NotEnoughCurrency  => unaffordableColor,
                    PurchaseResult.MaxLevel           => maxedColor,
                    _                                 => lockedColor,
                };
            }

            bool dim = state == PurchaseResult.PrerequisiteLocked;
            if (iconImage != null)
            {
                var c = SkillPalette.CategoryColor(Node.effectType);
                c.a = dim ? 0.30f : 1f;
                iconImage.color = c;
            }
            if (nameText != null)
                nameText.color = dim ? new Color(0.45f, 0.47f, 0.50f) : new Color(0.92f, 0.94f, 0.96f);

            // 선행이 막혀 있어도 눌러서 조건은 볼 수 있게 둔다
            if (button != null) button.interactable = true;
        }

        public void OnPointerEnter(PointerEventData eventData) => _onHovered?.Invoke(this);
        public void OnPointerExit(PointerEventData eventData) => _onHovered?.Invoke(null);
    }

    /// <summary>스킬 계열별 색. 트리에서 같은 갈래를 눈으로 묶어준다.</summary>
    public static class SkillPalette
    {
        public static readonly Color Size     = new Color(0.42f, 0.86f, 0.55f);  // 크기 — 초록
        public static readonly Color Time     = new Color(0.45f, 0.72f, 0.96f);  // 시간 — 파랑
        public static readonly Color Money    = new Color(0.98f, 0.82f, 0.38f);  // 재화 — 금색
        public static readonly Color Utility  = new Color(0.72f, 0.60f, 0.95f);  // 유틸 — 보라
        public static readonly Color Active   = new Color(0.98f, 0.60f, 0.38f);  // 액티브 — 주황

        public static Color CategoryColor(SkillEffectType t)
        {
            if (t.IsUnlock() || t.IsActiveUpgrade()) return Active;

            switch (t)
            {
                case SkillEffectType.BodyScale:     return Size;
                case SkillEffectType.SurvivalTime:
                case SkillEffectType.TimeGain:      return Time;
                case SkillEffectType.CurrencyGain:  return Money;
                default:                            return Utility;
            }
        }

        public static string CategoryName(SkillEffectType t)
        {
            if (t.IsUnlock()) return "액티브 해금";
            if (t.IsActiveUpgrade()) return "액티브 강화";

            switch (t)
            {
                case SkillEffectType.BodyScale:     return "크기";
                case SkillEffectType.SurvivalTime:
                case SkillEffectType.TimeGain:      return "시간";
                case SkillEffectType.CurrencyGain:  return "재화";
                default:                            return "유틸";
            }
        }

        /// <summary>부족한 선행 조건을 사람이 읽는 문장으로. 전부 충족이면 빈 문자열.</summary>
        public static string DescribeMissingRequirements(SkillNode node, PlayerProgress progress)
        {
            if (node == null || node.prerequisites.Count == 0) return "";

            var sb = new StringBuilder();
            foreach (var req in node.prerequisites)
            {
                if (req?.node == null) continue;
                int have = progress.GetSkillLevel(req.node.id);
                bool met = have >= req.level;

                sb.Append(met ? "<color=#5ED18A>✓ " : "<color=#E08A72>✗ ");
                sb.Append(req.node.displayName);
                sb.Append(req.level > 1 ? $"  {have}/{req.level}" : (met ? "  해금됨" : "  미해금"));
                sb.Append("</color>\n");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
