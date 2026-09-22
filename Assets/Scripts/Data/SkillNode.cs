using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishGame.Data
{
    /// <summary>선행 조건 하나 — "이 노드가 이 레벨 이상".</summary>
    [Serializable]
    public class SkillRequirement
    {
        public SkillNode node;
        [Min(1)] public int level = 1;
    }

    /// <summary>
    /// 스킬트리 노드 1개.
    ///
    /// ★ 코스트는 노드별이 아니라 "지금까지 찍은 총 노드 수"로 정해진다 (기획서 2판).
    ///     N번째로 찍는 노드의 값 = round(1.12^(N-1))
    ///     단, 반올림 때문에 값이 정체되면 강제로 +1 (SkillCostCurve 참고)
    /// 그래서 어떤 노드를 먼저 찍든 N번째 노드의 가격은 같다.
    /// </summary>
    [CreateAssetMenu(fileName = "Skill_", menuName = "FishGame/Skill Node", order = 2)]
    public class SkillNode : ScriptableObject
    {
        [Header("식별")]
        [Tooltip("세이브에 저장되는 고유 키. 한번 정하면 바꾸지 말 것 (바꾸면 저장된 레벨이 유실됨).")]
        public string id = "skill_id";
        public string displayName = "이름 없는 스킬";
        [Tooltip("인게임 설명 (한 줄)")]
        [TextArea(2, 3)] public string tooltip = "";
        [TextArea(2, 4)] public string description = "";
        public Sprite icon;

        [Header("트리 구조")]
        [Tooltip("이 조건들을 모두 만족해야 구매 가능")]
        public List<SkillRequirement> prerequisites = new List<SkillRequirement>();
        [Tooltip("스킬트리 UI 상의 격자 좌표 (x=열, y=행)")]
        public Vector2Int gridPosition = Vector2Int.zero;

        [Header("효과")]
        public SkillEffectType effectType = SkillEffectType.SurvivalTime;
        [Tooltip("레벨 1당 값. 복리 효과는 0.10 = 레벨마다 ×1.10.")]
        public float valuePerLevel = 0.10f;
        [Tooltip("최대 레벨. 해금(Unlock) 노드는 강제로 1이 된다.")]
        [Min(1)] public int maxLevel = 50;

        [Header("코스트 보정")]
        [Tooltip("이 노드의 가격에 곱해지는 배율. 1이면 전역 곡선 그대로. " +
                 "액티브 해금처럼 비싸게 하고 싶은 노드만 올린다.")]
        [Min(0.01f)] public float costMultiplier = 1f;

        public int EffectiveMaxLevel => effectType.IsUnlock() ? 1 : Mathf.Max(1, maxLevel);

        /// <summary>
        /// 이 노드를 한 단계 올리는 비용.
        /// totalNodesPurchased = 지금까지 트리 전체에서 찍은 노드 수.
        /// </summary>
        public double GetCost(int totalNodesPurchased)
        {
            // float 1.2f는 double로 1.2000000476…이라 100 × 1.2 가 121로 올림되던 문제.
            // 소수 6자리에서 한 번 반올림해 부동소수 오차를 걷어낸 뒤 올린다.
            double mult = (double)(decimal)costMultiplier;
            return Math.Ceiling(Math.Round(SkillCostCurve.CostOfNode(totalNodesPurchased + 1) * mult, 6));
        }

        /// <summary>UI 표기용 누적 효과 문자열.</summary>
        public string FormatEffect(int level)
        {
            if (effectType.IsUnlock()) return level > 0 ? "해금됨" : "미해금";

            if (effectType.IsMultiplicative())
            {
                double mult = Math.Pow(1d + valuePerLevel, level);
                return $"×{mult:0.##}";
            }
            if (effectType.IsLinearPercent())
                return $"×{1d + valuePerLevel * level:0.##}";
            return $"+{valuePerLevel * level:0.##}";
        }

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name.ToLowerInvariant();
            if (effectType.IsUnlock()) maxLevel = 1;
        }
    }

    /// <summary>
    /// 전역 노드 코스트 곡선. 기획서의 의사코드를 그대로 옮긴 것.
    ///
    ///   currentCost = round(1.12^(N-1))
    ///   if (currentCost &lt;= previousCost) currentCost = previousCost + 1
    ///
    /// 초반에는 반올림 때문에 1,1,1,1... 로 정체되므로 +1 보정이 들어간다.
    /// 결과: 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, ... 이후 지수적으로 상승.
    /// </summary>
    public static class SkillCostCurve
    {
        const double Growth = 1.12d;
        const int CacheSize = 512;

        static double[] _cache;

        static void Build()
        {
            _cache = new double[CacheSize + 1];
            double previous = 0d;
            for (int n = 1; n <= CacheSize; n++)
            {
                double current = Math.Round(Math.Pow(Growth, n - 1), MidpointRounding.AwayFromZero);
                if (current <= previous) current = previous + 1d;
                _cache[n] = current;
                previous = current;
            }
        }

        /// <summary>N번째(1부터)로 찍는 노드의 기본 가격.</summary>
        public static double CostOfNode(int n)
        {
            if (n < 1) n = 1;
            if (_cache == null) Build();
            if (n <= CacheSize) return _cache[n];

            // 캐시를 넘어가면 순수 지수식으로 (이 구간에선 정체가 없다)
            return Math.Round(Math.Pow(Growth, n - 1), MidpointRounding.AwayFromZero);
        }

        /// <summary>노드 n개를 찍는 데 드는 누적 비용 (밸런싱 검증용).</summary>
        public static double CumulativeCost(int nodeCount)
        {
            double sum = 0d;
            for (int i = 1; i <= nodeCount; i++) sum += CostOfNode(i);
            return sum;
        }
    }
}
