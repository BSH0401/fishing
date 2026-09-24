using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>◀ 값 ▶ 로 목록에서 하나를 고른다.</summary>
    public class LabSelector : MonoBehaviour
    {
        Button _left, _right;
        TMP_Text _value;
        string[] _options = Array.Empty<string>();
        int _index;
        bool _interactable = true;

        public event Action<int> Changed;
        public int Index => _index;

        public void Init(Button left, Button right, TMP_Text value)
        {
            _left = left;
            _right = right;
            _value = value;
            _left.onClick.AddListener(() => Step(-1));
            _right.onClick.AddListener(() => Step(+1));
        }

        public void SetOptions(string[] options, int index)
        {
            _options = options ?? Array.Empty<string>();
            _index = Mathf.Clamp(index, 0, Mathf.Max(0, _options.Length - 1));
            Refresh();
        }

        public void SetInteractable(bool on, string overrideText = null)
        {
            _interactable = on;
            _left.interactable = on && _options.Length > 1;
            _right.interactable = on && _options.Length > 1;
            if (_value != null)
            {
                _value.text = overrideText ?? (_options.Length > 0 ? _options[_index] : "");
                _value.color = on ? UIKit.Ink : UIKit.InkDim;
            }
        }

        void Step(int dir)
        {
            if (!_interactable || _options.Length == 0) return;
            _index = (_index + dir + _options.Length) % _options.Length;
            Refresh();
            Changed?.Invoke(_index);
        }

        void Refresh()
        {
            if (_value != null) _value.text = _options.Length > 0 ? _options[_index] : "";
            if (_left != null) _left.interactable = _interactable && _options.Length > 1;
            if (_right != null) _right.interactable = _interactable && _options.Length > 1;
        }
    }
}
