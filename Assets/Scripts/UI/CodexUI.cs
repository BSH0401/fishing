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
    /// 물고기 도감. 메인 화면의 패널로 열린다.
    ///
    /// 종을 codexMilestone(100)마리 먹을 때마다 그 종의 고유 효과가 한 단계씩 붙는다.
    /// 행 하나하나가 "이 물고기를 더 먹을 이유"다.
    /// </summary>
    public class CodexUI : MonoBehaviour
    {
        [Header("패널")]
        [SerializeField] GameObject root;
        [SerializeField] Button openButton;
        [SerializeField] Button closeButton;

        [Header("목록")]
        [SerializeField] RectTransform listContainer;
        [SerializeField] CodexRow rowPrefab;
        [SerializeField] TMP_Text summaryText;

        readonly List<CodexRow> _rows = new List<CodexRow>();
        GameManager _game;
        bool _built;

        void Awake()
        {
            if (root != null) root.SetActive(false);
            if (openButton != null) openButton.onClick.AddListener(Open);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        void Start()
        {
            _game = GameManager.Instance;
            if (_game != null) _game.OnProgressChanged += RefreshIfOpen;
        }

        void OnDestroy()
        {
            if (_game != null) _game.OnProgressChanged -= RefreshIfOpen;
        }

        public void Open()
        {
            if (_game == null || _game.Database == null) return;
            if (!_built) Build();
            if (root != null) root.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            if (root != null) root.SetActive(false);
        }

        void RefreshIfOpen()
        {
            if (root != null && root.activeSelf) Refresh();
        }

        // ── 생성 ────────────────────────────────────────────────
        void Build()
        {
            if (listContainer == null || rowPrefab == null) return;

            foreach (Transform c in listContainer) Destroy(c.gameObject);
            _rows.Clear();

            foreach (var fish in _game.Database.allFish)
            {
                if (fish == null) continue;
                var row = Instantiate(rowPrefab, listContainer);
                row.Bind(fish);
                _rows.Add(row);
            }
            _built = true;
        }

        void Refresh()
        {
            var db = _game.Database;
            var progress = _game.Progress;

            int discovered = 0, total = 0;
            foreach (var row in _rows)
            {
                row.Refresh(db, progress);
                total++;
                if (progress.GetCodexCount(row.Species.CodexKey) > 0) discovered++;
            }

            if (summaryText != null)
            {
                int tiers = 0;
                foreach (var row in _rows)
                    tiers += PlayerStats.CodexTier(row.Species, db, progress);

                summaryText.text =
                    $"발견 {discovered}/{total}종   ·   획득한 도감 효과 {tiers}단계\n" +
                    $"<size=80%>한 종을 {db.codexMilestone}마리 먹을 때마다 그 종의 효과가 한 단계씩 붙습니다</size>";
            }
        }
    }

    /// <summary>도감 한 줄. CodexUI가 프리팹으로 찍어낸다.</summary>
    public class CodexRow : MonoBehaviour
    {
        [SerializeField] Image icon;
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text countText;
        [SerializeField] TMP_Text bonusText;
        [SerializeField] Image progressFill;
        [SerializeField] Image background;

        [SerializeField] Color discoveredColor = new Color(0.14f, 0.18f, 0.22f, 0.9f);
        [SerializeField] Color unknownColor = new Color(0.10f, 0.11f, 0.13f, 0.9f);

        public FishSpecies Species { get; private set; }

        public void Bind(FishSpecies fish)
        {
            Species = fish;
            if (icon != null)
            {
                icon.sprite = fish.sprite;
                icon.enabled = fish.sprite != null;
            }
            name = $"Codex_{fish.CodexKey}";
        }

        public void Refresh(GameDatabase db, PlayerProgress progress)
        {
            if (Species == null) return;

            int eaten = progress.GetCodexCount(Species.CodexKey);
            bool found = eaten > 0;
            int milestone = Mathf.Max(1, db.codexMilestone);
            int tier = PlayerStats.CodexTier(Species, db, progress);
            int maxTier = Mathf.Max(1, db.codexMaxTiers);

            if (background != null) background.color = found ? discoveredColor : unknownColor;

            if (nameText != null)
                nameText.text = found ? Species.displayName : "???";

            if (icon != null)
                icon.color = found ? Color.white : new Color(0.25f, 0.25f, 0.28f, 0.8f);

            if (countText != null)
            {
                if (!found) { countText.text = "-"; }
                else if (tier >= maxTier) { countText.text = $"{NumberFormatter.Format(eaten)}  (완성)"; }
                else
                {
                    int inTier = eaten % milestone;
                    countText.text = $"{NumberFormatter.Format(eaten)}   <size=80%>({inTier}/{milestone})</size>";
                }
            }

            if (progressFill != null)
            {
                float t = tier >= maxTier ? 1f : (eaten % milestone) / (float)milestone;
                progressFill.fillAmount = found ? t : 0f;
            }

            if (bonusText != null)
            {
                if (!found || Species.codexBonusType == CodexBonusType.None)
                {
                    bonusText.text = "";
                }
                else
                {
                    string label = Species.codexBonusType.ToKorean();
                    float amount = Species.codexBonusPerTier * tier;

                    if (tier <= 0)
                    {
                        float next = Species.codexBonusPerTier;
                        bonusText.text = Species.codexBonusType.IsFlat()
                            ? $"<alpha=#77>{milestone}마리 → {label} +{next:0.#}초"
                            : $"<alpha=#77>{milestone}마리 → {label} +{next * 100f:0.#}%";
                    }
                    else
                    {
                        bonusText.text = Species.codexBonusType.IsFlat()
                            ? $"{label} +{amount:0.#}초  <size=80%>({tier}단계)</size>"
                            : $"{label} +{amount * 100f:0.#}%  <size=80%>({tier}단계)</size>";
                    }
                }
            }
        }
    }
}
