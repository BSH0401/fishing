using System.Collections.Generic;
using FishGame.Core;
using FishGame.Data;
using FishGame.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishGame.UI
{
    // CodexUI.cs 안에 있던 클래스를 분리했다.
    // Unity는 MonoBehaviour의 클래스 이름과 파일 이름이 같아야 프리팹·씬에 저장할 수 있다.
    // 같은 파일에 두면 CodexRow 프리팹이 "Missing Script"로 저장돼 도감이 항상 비어 보였다.
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
