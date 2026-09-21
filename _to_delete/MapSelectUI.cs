using System.Collections.Generic;
using FishGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>
    /// 메인 화면의 맵 선택.
    /// 기획서: "해금된 맵은 매번 첫 맵을 거치지 않고 메인 화면에서 시작 맵을 선택할 수 있음"
    /// </summary>
    public class MapSelectUI : MonoBehaviour
    {
        [SerializeField] RectTransform container;
        [SerializeField] Button buttonPrefab;
        [SerializeField] TMP_Text selectedMapText;

        [Header("색")]
        [SerializeField] Color selectedColor = new Color(0.35f, 0.78f, 0.45f);
        [SerializeField] Color unlockedColor = Color.white;
        [SerializeField] Color lockedColor = new Color(0.35f, 0.35f, 0.38f);

        readonly List<(Button button, int index)> _buttons = new();
        GameManager _game;

        void Start()
        {
            _game = GameManager.Instance;
            if (_game == null || _game.Database == null) return;

            Build();
            _game.OnProgressChanged += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (_game != null) _game.OnProgressChanged -= Refresh;
        }

        void Build()
        {
            foreach (Transform c in container) Destroy(c.gameObject);
            _buttons.Clear();

            for (int i = 0; i < _game.Database.MapCount; i++)
            {
                var map = _game.Database.GetMap(i);
                if (map == null) continue;

                int index = i;
                var btn = Instantiate(buttonPrefab, container);
                btn.name = $"Map_{index}";

                var label = btn.GetComponentInChildren<TMP_Text>();
                if (label != null) label.text = $"{index + 1}. {map.displayName}";

                btn.onClick.AddListener(() =>
                {
                    _game.SelectMap(index);
                    Refresh();
                });

                _buttons.Add((btn, index));
            }
        }

        void Refresh()
        {
            foreach (var (btn, index) in _buttons)
            {
                bool unlocked = _game.CanSelectMap(index);
                bool selected = index == _game.SelectedMapIndex;

                btn.interactable = unlocked;
                var img = btn.GetComponent<Image>();
                if (img != null)
                    img.color = !unlocked ? lockedColor : (selected ? selectedColor : unlockedColor);

                var label = btn.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    var map = _game.Database.GetMap(index);
                    string name = map != null ? map.displayName : "?";
                    label.text = unlocked ? $"{index + 1}. {name}" : $"{index + 1}. ???  🔒";
                }
            }

            if (selectedMapText != null)
            {
                var map = _game.SelectedMap;
                selectedMapText.text = map != null ? map.displayName : "-";
            }
        }
    }
}
