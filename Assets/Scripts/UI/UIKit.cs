using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>
    /// 실행 중에 UI를 짜는 작은 도구 모음 — 타이틀·설정 화면이 쓴다.
    /// 씬을 다시 만들지 않아도 어느 씬에서나 같은 모양(수중 실험실 스타일)으로 뜬다.
    /// </summary>
    public static class UIKit
    {
        public static readonly Color Ink = new Color(0.93f, 0.96f, 0.97f);
        public static readonly Color InkDim = new Color(0.62f, 0.76f, 0.78f);
        public static readonly Color Accent = new Color(0.45f, 0.95f, 0.90f);
        public static readonly Color Track = new Color(0.02f, 0.07f, 0.09f, 0.95f);

        // ── 기본 ────────────────────────────────────────────────
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>앵커·피벗을 한 점에 두고 위치·크기를 잡는다. (0,1) = 왼쪽 위 기준</summary>
        public static RectTransform Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot ?? anchor;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }

        public static RectTransform Stretch(RectTransform r, float inset = 0f)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset);
            r.offsetMax = new Vector2(-inset, -inset);
            return r;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, Color color, bool raycast = false)
        {
            var img = Rect(parent, name).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color,
                                           TextAlignmentOptions align = TextAlignmentOptions.Left,
                                           FontStyles style = FontStyles.Normal)
        {
            var t = Rect(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>화면 전체를 덮는 오버레이 캔버스 (1920×1080 기준으로 늘어난다).</summary>
        public static Canvas OverlayCanvas(string name, int sortingOrder, bool persistent = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (persistent) UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        // ── 버튼 ────────────────────────────────────────────────
        public static Button Button(Transform parent, string name, string label, Vector2 size,
                                    bool primary = false, float fontSize = 24f)
        {
            var r = Rect(parent, name);
            r.sizeDelta = size;
            var img = r.gameObject.AddComponent<Image>();
            var btn = r.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var t = Text(r, "Label", label, fontSize, primary ? Color.white : Accent, TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(t.rectTransform);

            LabStyle.Button(btn, primary);
            r.gameObject.AddComponent<UIButtonFx>();
            return btn;
        }

        public static void SetLabel(Button b, string text)
        {
            var t = b != null ? b.GetComponentInChildren<TMP_Text>() : null;
            if (t != null) t.text = text;
        }

        // ── 슬라이더 ────────────────────────────────────────────
        public static Slider Slider(Transform parent, string name, Vector2 size)
        {
            var r = Rect(parent, name);
            r.sizeDelta = size;
            var slider = r.gameObject.AddComponent<Slider>();

            var bg = Image(r, "Background", LabArt.Panel, Track, raycast: true);
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 2.5f;
            var bgr = bg.rectTransform;
            bgr.anchorMin = new Vector2(0f, 0.5f); bgr.anchorMax = new Vector2(1f, 0.5f);
            bgr.sizeDelta = new Vector2(0f, 14f); bgr.anchoredPosition = Vector2.zero;

            var fillArea = Rect(r, "Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.5f); fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.offsetMin = new Vector2(2f, -5f); fillArea.offsetMax = new Vector2(-2f, 5f);
            var fill = Image(fillArea, "Fill", LabArt.Panel, Accent);
            fill.type = UnityEngine.UI.Image.Type.Sliced;
            fill.pixelsPerUnitMultiplier = 3.5f;
            Stretch(fill.rectTransform);

            var handleArea = Rect(r, "Handle Slide Area");
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(12f, 0f); handleArea.offsetMax = new Vector2(-12f, 0f);
            var handle = Image(handleArea, "Handle", SkillShapes.Get(SkillShapes.Shape.Circle), Color.white, raycast: true);
            handle.rectTransform.sizeDelta = new Vector2(30f, 30f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f;
            var colors = slider.colors;
            colors.highlightedColor = new Color(0.85f, 1f, 0.97f);
            colors.pressedColor = Accent;
            colors.selectedColor = new Color(0.85f, 1f, 0.97f);
            slider.colors = colors;
            return slider;
        }

        // ── 켜기/끄기 스위치 ────────────────────────────────────
        public static LabSwitch Switch(Transform parent, string name)
        {
            var r = Rect(parent, name);
            r.sizeDelta = new Vector2(96f, 44f);
            var track = r.gameObject.AddComponent<Image>();
            track.sprite = SkillShapes.Pill;
            track.type = UnityEngine.UI.Image.Type.Simple;
            var btn = r.gameObject.AddComponent<Button>();
            btn.targetGraphic = track;
            btn.transition = Selectable.Transition.None;

            var knob = Image(r, "Knob", SkillShapes.Get(SkillShapes.Shape.Circle), Color.white);
            knob.rectTransform.sizeDelta = new Vector2(34f, 34f);

            var sw = r.gameObject.AddComponent<LabSwitch>();
            sw.Init(btn, track, knob.rectTransform);
            r.gameObject.AddComponent<UIButtonFx>();
            return sw;
        }

        // ── ◀ 값 ▶ 고르기 ───────────────────────────────────────
        public static LabSelector Selector(Transform parent, string name, Vector2 size)
        {
            var r = Rect(parent, name);
            r.sizeDelta = size;
            var bg = r.gameObject.AddComponent<Image>();
            LabStyle.Panel(bg, corners: false, fill: Track, border: LabStyle.Border);

            var left = Button(r, "Prev", "◀", new Vector2(52f, size.y - 8f), fontSize: 20f);
            Place((RectTransform)left.transform, new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(52f, size.y - 8f));
            var right = Button(r, "Next", "▶", new Vector2(52f, size.y - 8f), fontSize: 20f);
            Place((RectTransform)right.transform, new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(52f, size.y - 8f));

            var value = Text(r, "Value", "", 22f, Ink, TextAlignmentOptions.Center);
            Stretch(value.rectTransform);
            value.rectTransform.offsetMin = new Vector2(60f, 0f);
            value.rectTransform.offsetMax = new Vector2(-60f, 0f);

            var sel = r.gameObject.AddComponent<LabSelector>();
            sel.Init(left, right, value);
            return sel;
        }
    }
}
