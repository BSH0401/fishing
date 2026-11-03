using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>
    /// "수중 실험실" 계기판 스타일을 기존 UI에 입힌다. 씬을 다시 만들지 않아도 실행 중에 적용된다.
    ///   패널   어두운 청록 유리 + 가는 청록 테두리 + 네 모서리 ㄱ자 장식
    ///   버튼   같은 재질의 작은 판, 글자는 밝은 청록
    /// </summary>
    public static class LabStyle
    {
        public static readonly Color Fill = new Color(0.03f, 0.10f, 0.13f, 0.88f);
        public static readonly Color Border = new Color(0.36f, 0.86f, 0.86f, 0.45f);
        public static readonly Color Accent = new Color(0.45f, 0.95f, 0.90f, 1f);
        public static readonly Color TextDim = new Color(0.62f, 0.78f, 0.80f, 1f);

        // ── 직접 그린 도트 UI (Resources/UI, 구글 드라이브 FishGame/이미지) ──────────
        //   FishButton  메인 화면 버튼          FishIndex  물고기 도감 창
        //   SquareUI    스킬트리 구역 테두리    BlueLine / OrangeLine  스킬트리 연결선
        // 그림이 없으면 예전 계기판 스타일로 돌아간다.
        static readonly System.Collections.Generic.Dictionary<string, Sprite> _ui =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        public static Sprite UiSprite(string name)
        {
            if (_ui.TryGetValue(name, out var s)) return s;
            s = Resources.Load<Sprite>("UI/" + name);
            _ui[name] = s;
            return s;
        }

        /// <summary>
        /// Image에 도트 판을 9-slice로 입힌다. pixelScale = 도트 1칸이 화면에서 몇 단위인지 (테두리 굵기).
        /// 예전 스타일의 테두리 · 모서리 장식은 끈다. 그림이 없으면 false.
        /// </summary>
        public static bool PixelPanel(Image img, string sprite, float pixelScale = 3f, Color? tint = null)
        {
            if (img == null) return false;
            var s = UiSprite(sprite);
            if (s == null) return false;
            img.sprite = s;
            img.type = Image.Type.Sliced;
            img.fillCenter = true;
            img.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.1f, pixelScale);
            img.color = tint ?? Color.white;
            foreach (Transform c in img.transform)
                if (c.name == "LabBorder" || c.name == "LabCorner") c.gameObject.SetActive(false);
            return true;
        }

        /// <summary>메인 화면 버튼 — 도트 버튼 판 + 흰 글씨. 그림이 없으면 Button()과 같다.</summary>
        public static void FishButton(Button button, bool primary = false)
        {
            if (button == null) return;
            var img = button.targetGraphic as Image;
            if (img == null) img = button.GetComponent<Image>();
            if (!PixelPanel(img, "FishButton", 3f, primary ? Color.white : new Color(0.82f, 0.9f, 0.92f, 1f)))
            {
                Button(button, primary);
                return;
            }
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.7f, 0.8f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.color = Color.white;
        }

        /// <summary>Image를 계기판 패널로. 이미 적용했으면 색만 다시 맞춘다.</summary>
        public static void Panel(Image img, bool corners = true, Color? fill = null, Color? border = null,
                                 Color? corner = null)
        {
            if (img == null) return;
            img.sprite = LabArt.Panel;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.color = fill ?? Fill;

            var t = img.transform;
            var existing = t.Find("LabBorder");
            if (existing != null)
            {
                // 이미 입혔으면 테두리 색만 맞춘다 (탭 선택처럼 상태가 바뀔 때)
                var eb = existing.GetComponent<Image>();
                if (eb != null) eb.color = border ?? Border;
                return;
            }

            var b = NewChild(t, "LabBorder");
            Stretch(b.rectTransform, 0f);
            b.sprite = LabArt.PanelBorder;
            b.type = Image.Type.Sliced;
            b.color = border ?? Border;
            b.transform.SetAsFirstSibling();

            if (!corners) return;
            // 네 모서리 장식 — 스프라이트는 왼쪽 위 방향이라 회전해서 쓴다
            Color cc = corner ?? Accent;
            AddCorner(t, new Vector2(0f, 1f), 0f, cc);
            AddCorner(t, new Vector2(1f, 1f), -90f, cc);
            AddCorner(t, new Vector2(1f, 0f), 180f, cc);
            AddCorner(t, new Vector2(0f, 0f), 90f, cc);
        }

        static void AddCorner(Transform parent, Vector2 anchor, float rotation, Color color)
        {
            var c = NewChild(parent, "LabCorner");
            var r = c.rectTransform;
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(14f, 14f);
            r.localRotation = Quaternion.Euler(0f, 0f, rotation);
            // 모서리에서 살짝 안쪽
            Vector2 inward = new Vector2(anchor.x < 0.5f ? 5f : -5f, anchor.y < 0.5f ? 5f : -5f);
            r.anchoredPosition = inward;
            c.sprite = LabArt.Corner;
            c.color = color;
        }

        /// <summary>버튼을 계기판 버튼으로. 글자색도 맞춘다.</summary>
        public static void Button(Button button, bool primary = false)
        {
            if (button == null) return;
            var img = button.targetGraphic as Image;
            if (img == null) img = button.GetComponent<Image>();
            Panel(img, corners: false,
                  fill: primary ? new Color(0.10f, 0.42f, 0.42f, 0.95f) : new Color(0.05f, 0.16f, 0.19f, 0.92f),
                  border: primary ? Accent : Border);

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.75f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.color = primary ? Color.white : Accent;
        }

        static Image NewChild(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            // 테두리 · 모서리 장식은 꾸밈일 뿐 — 레이아웃 그룹이 있는 패널(툴팁 등)에 붙으면
            // 줄 하나로 끼어들어 내용이 밀리고 크기가 깨졌다
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            return img;
        }

        static void Stretch(RectTransform r, float inset)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset);
            r.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
