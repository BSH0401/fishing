using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>켜기/끄기 스위치.</summary>
    public class LabSwitch : MonoBehaviour
    {
        Button _button;
        Image _track;
        RectTransform _knob;
        bool _value;
        float _t;

        public event Action<bool> Changed;
        public bool Value => _value;

        public void Init(Button button, Image track, RectTransform knob)
        {
            _button = button;
            _track = track;
            _knob = knob;
            _button.onClick.AddListener(() => Set(!_value, notify: true));
            Set(false, notify: false, instant: true);
        }

        public void Set(bool value, bool notify = false, bool instant = false)
        {
            _value = value;
            if (instant) _t = value ? 1f : 0f;
            if (notify) Changed?.Invoke(value);
        }

        void Update()
        {
            _t = Mathf.MoveTowards(_t, _value ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            if (_knob != null) _knob.anchoredPosition = new Vector2(Mathf.Lerp(-26f, 26f, _t), 0f);
            if (_track != null) _track.color = Color.Lerp(new Color(0.16f, 0.24f, 0.27f), new Color(0.2f, 0.72f, 0.66f), _t);
        }
    }
}
