using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>마우스를 올리거나 키보드로 고르면 살짝 커지는 버튼 반응.</summary>
    public class UIButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        bool _hot;
        float _s = 1f;
        Selectable _sel;

        void Awake() => _sel = GetComponent<Selectable>();

        public void OnPointerEnter(PointerEventData e) => _hot = true;
        public void OnPointerExit(PointerEventData e) => _hot = false;
        public void OnSelect(BaseEventData e) => _hot = true;
        public void OnDeselect(BaseEventData e) => _hot = false;
        void OnDisable() { _hot = false; _s = 1f; transform.localScale = Vector3.one; }

        void Update()
        {
            bool on = _hot && (_sel == null || _sel.IsInteractable());
            float target = on ? 1.05f : 1f;
            if (Mathf.Abs(_s - target) < 0.001f) return;
            _s = Mathf.Lerp(_s, target, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
            transform.localScale = new Vector3(_s, _s, 1f);
        }
    }
}
