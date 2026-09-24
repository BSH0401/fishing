using System;
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
    /// 스킬트리 칸 하나. SkillTreeUI가 프리팹으로 찍어낸다.
    ///
    /// 도형은 기획 도면의 범례 그대로 (□ 배터리, ○ 장갑, ◇ 치아 …).
    /// 해금 칸(부스터·청소기 …)과 시작 칸은 이름이 적힌 알약 모양이다.
    ///
    /// 층 (뒤 → 앞): 그림자 · 빛 번짐 · 테두리 · 도형 · 이름 · 가격표 · 자물쇠
    /// 그림자·빛 번짐·가격표 받침은 프리팹에 없어도 여기서 만든다 (씬을 다시 안 만들어도 적용).
    ///
    /// 상태 4가지가 한눈에 보여야 한다:
    ///   찍음          선명한 계열 색 + 금빛 테두리 + 은은한 빛 번짐
    ///   지금 살 수 있음 밝은 테두리 + 민트빛 맥동 + 살짝 숨쉬듯 커졌다 작아짐
    ///   열렸지만 돈 부족 어두운 계열 색 + 흐린 테두리 + 붉은 가격
    ///   잠김          바다에 가라앉은 실루엣, 가격 숨김
    /// </summary>
    public class SkillNodeButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] Button button;
        [SerializeField] Image shapeImage;
        [SerializeField] Image haloImage;      // 도형을 조금 크게 뒤에 깐 테두리
        [SerializeField] Image glowImage;      // 빛 번짐
        [SerializeField] TMP_Text nameText;    // 해금·시작 칸에만 보인다
        [SerializeField] TMP_Text costText;
        [SerializeField] GameObject lockIcon;

        [Header("크기")]
        [SerializeField] Vector2 shapeSize = new Vector2(64f, 64f);
        [SerializeField] Vector2 pillSize = new Vector2(150f, 60f);

        [Header("상태 색")]
        [SerializeField] Color haloColor = new Color(1f, 0.84f, 0.36f);
        [SerializeField] Color readyRimColor = new Color(0.75f, 1f, 0.95f, 0.95f);
        [SerializeField] Color readyGlowColor = new Color(0.45f, 1f, 0.8f, 1f);
        [SerializeField] Color lockedColor = new Color(0.16f, 0.24f, 0.28f, 0.85f);

        public int SlotIndex { get; private set; } = -1;
        public SkillTreeSlot Slot { get; private set; }
        public SkillNode Node => Slot?.skill;
        /// <summary>지금 바로 살 수 있는가 (SkillTreeUI가 강조에 쓴다).</summary>
        public bool Affordable { get; private set; }
        public bool Purchased { get; private set; }
        public PurchaseResult State { get; private set; }
        public Vector2 Size { get; private set; }
        public bool IsPill => _isPill;

        /// <summary>도형 색 (연출이 같은 색으로 튄다).</summary>
        public Color TypeColor => Slot == null || Slot.IsRoot ? new Color(0.30f, 0.62f, 0.95f)
                                                             : SkillPalette.TypeColor(Node.effectType);
        public Sprite ShapeSprite => shapeImage != null ? shapeImage.sprite : null;

        bool _isPill;
        Action<SkillNodeButton> _onClicked;
        Action<SkillNodeButton> _onHovered;

        Image _shadow;
        Image _chip;
        RectTransform _visual;        // 크기·흔들림 애니메이션은 이 묶음에만 건다 (클릭 판정은 그대로)
        Color _shapeBase = Color.white;
        float _glowPhase;
        bool _hovered;
        float _scale = 1f;
        float _punch;                 // 구매 순간 튀어오름 (1 → 0)
        float _flash;                 // 흰빛 번쩍 (1 → 0)
        float _shake;                 // 거절 흔들림 (1 → 0)
        float _unlockPop;             // 새로 열림 (1 → 0)

        public void Bind(int slotIndex, SkillTreeSlot slot,
                         Action<SkillNodeButton> onClicked, Action<SkillNodeButton> onHovered)
        {
            SlotIndex = slotIndex;
            Slot = slot;
            _onClicked = onClicked;
            _onHovered = onHovered;
            _glowPhase = UnityEngine.Random.value * Mathf.PI * 2f;
            _isPill = slot.IsRoot || slot.skill.effectType.IsUnlock();

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => _onClicked?.Invoke(this));
                button.transition = Selectable.Transition.None;   // 색 변화는 우리가 직접 한다
            }

            Size = _isPill ? pillSize : shapeSize;
            ((RectTransform)transform).sizeDelta = Size;

            var shape = _isPill ? SkillShapes.Shape.Pill : SkillShapes.ShapeOf(slot.skill.effectType);
            Sprite sprite = SkillShapes.Get(shape);
            Sprite soft = SkillShapes.Soft(shape);
            Vector2 softSize = Vector2.Scale(Size, SkillShapes.SoftScale(shape));

            BuildVisualGroup();

            if (shapeImage != null)
            {
                shapeImage.sprite = sprite;
                shapeImage.preserveAspect = !_isPill;
                shapeImage.rectTransform.sizeDelta = Size;
            }
            if (haloImage != null)
            {
                haloImage.sprite = sprite;
                haloImage.preserveAspect = !_isPill;
                haloImage.rectTransform.sizeDelta = Size + new Vector2(10f, 10f);
            }
            if (glowImage != null)
            {
                glowImage.sprite = soft;
                glowImage.preserveAspect = false;
                glowImage.rectTransform.sizeDelta = softSize * 1.25f;
            }
            if (_shadow != null)
            {
                _shadow.sprite = soft;
                _shadow.rectTransform.sizeDelta = softSize * 1.05f;
                _shadow.rectTransform.anchoredPosition = new Vector2(3f, -6f);
            }

            if (nameText != null)
            {
                nameText.gameObject.SetActive(_isPill);
                nameText.text = slot.IsRoot ? "시작" : slot.skill.displayName;
                nameText.rectTransform.sizeDelta = Size;
                nameText.fontStyle = FontStyles.Bold;
            }
            if (costText != null)
            {
                costText.rectTransform.anchoredPosition = new Vector2(0f, -Size.y * 0.5f - 15f);
                costText.fontSize = 15f;
            }
            if (_chip != null)
                _chip.rectTransform.anchoredPosition = new Vector2(0f, -Size.y * 0.5f - 15f);
            if (lockIcon != null)
                ((RectTransform)lockIcon.transform).anchoredPosition = new Vector2(Size.x * 0.5f - 6f, Size.y * 0.5f - 6f);

            name = $"Slot_{slot.id}";
        }

        /// <summary>
        /// 그림자·가격표 받침을 만들고, 보이는 것들을 "Visual" 묶음으로 옮긴다.
        /// 루트는 클릭 판정용이라 크기를 바꾸면 안 되고, 애니메이션은 이 묶음에만 건다.
        /// </summary>
        void BuildVisualGroup()
        {
            if (_visual != null) return;

            var go = new GameObject("Visual", typeof(RectTransform));
            _visual = (RectTransform)go.transform;
            _visual.SetParent(transform, false);
            _visual.anchorMin = _visual.anchorMax = new Vector2(0.5f, 0.5f);
            _visual.sizeDelta = Vector2.zero;

            _shadow = NewImage("Shadow", _visual);
            _shadow.color = new Color(0f, 0.02f, 0.05f, 0.65f);

            // 기존 자식을 뒤→앞 순서로 옮긴다
            Move(glowImage != null ? glowImage.transform : null);
            Move(haloImage != null ? haloImage.transform : null);
            Move(shapeImage != null ? shapeImage.transform : null);
            Move(nameText != null ? nameText.transform : null);

            _chip = NewImage("CostChip", _visual);
            _chip.sprite = LabArt.Panel;
            _chip.type = Image.Type.Sliced;
            _chip.pixelsPerUnitMultiplier = 2.2f;          // 22px 높이에 맞게 모서리를 줄인다
            _chip.color = new Color(0.02f, 0.07f, 0.09f, 0.88f);
            Move(costText != null ? costText.transform : null);
            Move(lockIcon != null ? lockIcon.transform : null);

            if (glowImage != null) glowImage.raycastTarget = false;
        }

        void Move(Transform t)
        {
            if (t == null) return;
            t.SetParent(_visual, false);
            t.SetAsLastSibling();
        }

        static Image NewImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        // ══════════════════════════════════════════════════════════
        //  연출
        // ══════════════════════════════════════════════════════════
        public void PlayPurchase() { _punch = 1f; _flash = 1f; }
        public void PlayDenied() { _shake = 1f; }
        public void PlayUnlock(float delay) { _unlockPop = 1f + Mathf.Max(0f, delay) * 4f; }

        void Update()
        {
            if (_visual == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            // ── 크기: 마우스 올림 · 구매 튀어오름 · 살 수 있음 숨쉬기 · 새로 열림 ──
            float target = _hovered ? 1.14f : 1f;
            _scale = Mathf.Lerp(_scale, target, 1f - Mathf.Exp(-16f * dt));

            float extra = 0f;
            if (_punch > 0f)
            {
                _punch = Mathf.Max(0f, _punch - dt * 2.6f);
                float k = 1f - _punch;                                   // 0 → 1
                extra += Mathf.Sin(k * Mathf.PI) * 0.38f * (1f - k * 0.5f);
            }
            if (_unlockPop > 0f)
            {
                _unlockPop = Mathf.Max(0f, _unlockPop - dt * 4f);
                if (_unlockPop < 1f) extra += Mathf.Sin((1f - _unlockPop) * Mathf.PI) * 0.22f;
            }
            if (Affordable)
            {
                _glowPhase += dt * 3.2f;
                extra += 0.035f * Mathf.Sin(_glowPhase);
            }

            float s = _scale + extra;
            if (Mathf.Abs(_visual.localScale.x - s) > 0.0005f) _visual.localScale = new Vector3(s, s, 1f);

            // ── 흔들림 ──
            Vector2 offset = Vector2.zero;
            if (_shake > 0f)
            {
                _shake = Mathf.Max(0f, _shake - dt * 3.5f);
                offset.x = Mathf.Sin(_shake * 40f) * 7f * _shake;
                // 이번 프레임에 흔들림이 끝났으면 아래 색 블록이 건너뛰므로 여기서 원래 색으로 돌린다
                if (_shake <= 0f && _flash <= 0f && shapeImage != null) shapeImage.color = _shapeBase;
            }
            if (_visual.anchoredPosition != offset) _visual.anchoredPosition = offset;

            // ── 흰빛 번쩍 / 거절 붉은빛 ──
            if (shapeImage != null && (_flash > 0f || _shake > 0f))
            {
                _flash = Mathf.Max(0f, _flash - dt * 3f);
                Color c = _shapeBase;
                if (_flash > 0f) c = Color.Lerp(c, Color.white, _flash * 0.85f);
                if (_shake > 0f) c = Color.Lerp(c, new Color(1f, 0.3f, 0.25f, c.a), _shake * 0.6f);
                shapeImage.color = c;
                if (_flash <= 0f && _shake <= 0f) shapeImage.color = _shapeBase;
            }

            // ── 살 수 있는 칸의 맥동하는 빛 ──
            if (glowImage != null && Affordable)
            {
                var c = readyGlowColor;
                c.a = 0.30f + 0.30f * (0.5f + 0.5f * Mathf.Sin(_glowPhase));
                glowImage.color = c;
            }
        }

        public void Refresh(GameDatabase db, PlayerProgress progress)
        {
            if (Slot == null || progress == null) return;

            if (Slot.IsRoot)
            {
                State = PurchaseResult.MaxLevel;
                Purchased = true;
                Affordable = false;
                SetShape(new Color(0.30f, 0.62f, 0.95f));
                SetHalo(true, haloColor);
                SetGlow(true, new Color(0.35f, 0.7f, 1f, 0.45f));
                SetCost(false, "", Color.white);
                if (lockIcon != null) lockIcon.SetActive(false);
                if (nameText != null) nameText.color = Color.white;
                if (_shadow != null) _shadow.enabled = true;
                return;
            }

            State = SkillTreeManager.CanPurchase(db, SlotIndex, progress, out double cost);
            Purchased = State == PurchaseResult.MaxLevel;
            Affordable = State == PurchaseResult.Success;
            bool locked = State == PurchaseResult.PrerequisiteLocked;
            Color cat = TypeColor;

            if (Purchased)
            {
                SetShape(cat);
                SetHalo(true, haloColor);
                SetGlow(true, new Color(cat.r, cat.g, cat.b, 0.38f));
            }
            else if (Affordable)
            {
                SetShape(Color.Lerp(cat, Color.white, 0.08f) * new Color(0.92f, 0.92f, 0.92f, 1f));
                SetHalo(true, readyRimColor);
                SetGlow(true, readyGlowColor);           // 알파는 Update가 맥동시킨다
            }
            else if (!locked)
            {
                SetShape(new Color(cat.r * 0.55f, cat.g * 0.55f, cat.b * 0.55f, 0.95f));
                SetHalo(true, new Color(0.45f, 0.85f, 0.85f, 0.35f));
                SetGlow(false, Color.clear);
            }
            else
            {
                SetShape(lockedColor);
                SetHalo(false, Color.clear);
                SetGlow(false, Color.clear);
            }

            if (_shadow != null) _shadow.enabled = !locked;

            bool showCost = !Purchased && !locked;
            SetCost(showCost, showCost ? NumberFormatter.Format(cost) : "",
                    Affordable ? new Color(1f, 0.88f, 0.45f) : new Color(0.92f, 0.52f, 0.46f));

            if (lockIcon != null) lockIcon.SetActive(false);   // 잠김은 실루엣으로 충분히 읽힌다
            if (nameText != null)
                nameText.color = locked ? new Color(0.45f, 0.55f, 0.58f) : Color.white;

            // 잠긴 칸도 눌러서 설명은 볼 수 있게 둔다
            if (button != null) button.interactable = true;
        }

        void SetShape(Color c)
        {
            _shapeBase = c;
            if (shapeImage != null && _flash <= 0f && _shake <= 0f) shapeImage.color = c;
        }

        void SetHalo(bool on, Color c)
        {
            if (haloImage == null) return;
            haloImage.enabled = on;
            haloImage.color = c;
        }

        void SetGlow(bool on, Color c)
        {
            if (glowImage == null) return;
            glowImage.gameObject.SetActive(on);
            glowImage.color = c;
        }

        void SetCost(bool on, string text, Color color)
        {
            if (costText != null)
            {
                costText.gameObject.SetActive(on);
                if (on) { costText.text = text; costText.color = color; }
            }
            if (_chip != null)
            {
                _chip.gameObject.SetActive(on);
                if (on && costText != null)
                {
                    float w = costText.GetPreferredValues(text).x + 18f;
                    _chip.rectTransform.sizeDelta = new Vector2(Mathf.Max(34f, w), 22f);
                }
            }
        }

        public void OnPointerEnter(PointerEventData eventData) { _hovered = true; _onHovered?.Invoke(this); }
        public void OnPointerExit(PointerEventData eventData) { _hovered = false; _onHovered?.Invoke(null); }
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

        /// <summary>
        /// 트리 칸의 색. 기본 강화 6종은 도형과 함께 색도 달라야 멀리서도 구분된다
        /// (계열 색만 쓰면 배터리·전지, 치아·카메라가 같은 색이 된다).
        /// </summary>
        public static Color TypeColor(SkillEffectType t)
        {
            switch (t)
            {
                case SkillEffectType.SurvivalTime: return new Color(0.36f, 0.64f, 0.96f);  // 배터리 — 파랑
                case SkillEffectType.BodyScale:    return new Color(0.42f, 0.86f, 0.55f);  // 장갑 — 초록
                case SkillEffectType.MouthPower:   return new Color(0.95f, 0.43f, 0.40f);  // 치아 — 빨강
                case SkillEffectType.Vision:       return new Color(0.70f, 0.56f, 0.95f);  // 카메라 — 보라
                case SkillEffectType.TimeGain:     return new Color(0.35f, 0.85f, 0.85f);  // 전지 — 청록
                case SkillEffectType.CurrencyGain: return new Color(0.98f, 0.82f, 0.38f);  // 위액 — 금색
                default:                           return CategoryColor(t);                 // 액티브 — 주황
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
    }
}
