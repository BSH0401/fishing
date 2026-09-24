using System.Collections.Generic;
using FishGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishGame.UI
{
    /// <summary>
    /// 메인 화면의 '시작 구역 선택'.
    ///
    /// 맵이 하나로 합쳐졌으므로 더 이상 맵을 고르는 게 아니라,
    /// 통합 맵에서 이미 내려가 본 구역부터 시작할지를 고른다.
    /// 한 번 도달한 구역은 계속 열려 있어서, 매번 어항부터 훑고 내려오지 않아도 된다.
    /// </summary>
    public class ZoneSelectUI : MonoBehaviour
    {
        [SerializeField] RectTransform container;
        [SerializeField] Button buttonPrefab;
        [SerializeField] TMP_Text selectedZoneText;
        [Tooltip("선택한 구역의 설명을 보여줄 텍스트 (선택)")]
        [SerializeField] TMP_Text descriptionText;

        const float ButtonHeight = 44f;
        static readonly Color LockedText = new Color(0.45f, 0.55f, 0.58f, 1f);

        readonly List<(Button button, int index)> _buttons = new List<(Button, int)>();
        GameManager _game;

        void Start()
        {
            _game = GameManager.Instance;
            if (_game == null || _game.Database == null)
            {
                Debug.LogError("[ZoneSelectUI] GameManager/Database가 없습니다.");
                return;
            }

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
            if (container == null || buttonPrefab == null) return;

            foreach (Transform c in container) Destroy(c.gameObject);
            _buttons.Clear();

            for (int i = 0; i < _game.Database.ZoneCount; i++)
            {
                var zone = _game.Database.GetZone(i);
                if (zone == null) continue;

                int index = i;
                var btn = Instantiate(buttonPrefab, container);
                btn.name = $"Zone_{index}";

                // 수중 실험실 스타일 — 오른쪽 패널 안에 구역 4개가 다 들어가게 조금 낮춘다
                var le = btn.GetComponent<LayoutElement>();
                if (le != null) { le.preferredHeight = ButtonHeight; le.minHeight = ButtonHeight; }
                var lbl = btn.GetComponentInChildren<TMP_Text>();
                if (lbl != null) { lbl.fontSize = 17f; lbl.lineSpacing = -12f; }

                btn.onClick.AddListener(() =>
                {
                    _game.SelectStartZone(index);
                    Refresh();
                });

                _buttons.Add((btn, index));
            }
        }

        void Refresh()
        {
            if (_game == null || _game.Database == null) return;

            foreach (var (btn, index) in _buttons)
            {
                bool unlocked = _game.CanSelectZone(index);
                bool selected = index == _game.SelectedStartZone;

                btn.interactable = unlocked;
                LabStyle.Button(btn, primary: unlocked && selected);

                var label = btn.GetComponentInChildren<TMP_Text>();
                if (label == null) continue;
                if (!unlocked) label.color = LockedText;

                var zone = _game.Database.GetZone(index);
                string name = zone != null ? zone.displayName : "?";

                if (!unlocked)
                {
                    bool reached = index <= _game.Progress.DeepestZoneReached;
                    if (reached && !_game.ZoneFitsCurrentSize(index))
                    {
                        // 가 본 구역인데 몸이 출구보다 커져서 못 들어가는 경우
                        label.text = $"{index + 1}. {name}\n<size=70%>몸이 너무 커서 출구를 못 지남</size>";
                        continue;
                    }

                    // 바로 다음 구역만 "필요 크기"를 알려준다. 그 아래는 감춘다.
                    bool isNext = index == _game.Progress.DeepestZoneReached + 1;
                    var prev = _game.Database.GetZone(index - 1);
                    label.text = isNext && prev != null
                        ? $"{index + 1}. ???\n<size=70%>크기 {prev.exitRequiredSize:0.#}로 통로 개방</size>"
                        : $"{index + 1}. ???";
                }
                else
                {
                    // 스킬 초기화 등으로 몸이 작아졌는데 깊은 구역을 고르면 시작하자마자 잡아먹힌다 — 미리 알린다
                    string warn = _game.IsZoneDangerous(index) ? "  <color=#FF8A7A>위험 — 몸이 작음</color>" : "";
                    label.text = $"{index + 1}. {name}\n<size=70%>깊이 {DepthLabel(index)}{warn}</size>";
                }
            }

            var sel = _game.SelectedZone;
            if (selectedZoneText != null)
                selectedZoneText.text = sel != null ? sel.displayName : "-";
            if (descriptionText != null)
                descriptionText.text = sel != null ? sel.description : "";
        }

        /// <summary>그 구역의 천장이 수면에서 얼마나 깊은지 (월드 유닛).</summary>
        string DepthLabel(int zoneIndex)
        {
            float depth = 0f;
            for (int i = 0; i < zoneIndex; i++)
            {
                var z = _game.Database.GetZone(i);
                if (z == null) continue;
                depth += z.height;
                if (z.hasExit) depth += z.exitHeight;
            }
            return $"{depth:0}m";
        }
    }
}
