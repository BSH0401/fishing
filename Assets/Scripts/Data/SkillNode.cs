using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishGame.Data
{
    /// <summary>
    /// 강화 종류 1개 (커다란 배터리, 덧붙인 장갑, 치아 교정 …).
    ///
    /// 트리 위의 칸(SkillTreeSlot)들이 이 종류를 가리킨다. 같은 종류의 칸을 k개 찍으면 k레벨이다.
    /// 그래서 maxLevel = 트리에 놓인 그 종류의 칸 수 (생성기가 맞춰 준다).
    ///
    /// ★ 가격은 칸별이 아니라 "지금까지 찍은 총 칸 수"로 정해진다 (SkillCostCurve).
    ///   어떤 칸을 먼저 찍든 N번째 칸의 기본 가격은 같고, costMultiplier만 종류별로 다르다.
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

        [Header("효과")]
        public SkillEffectType effectType = SkillEffectType.SurvivalTime;
        [Tooltip("칸 하나당 값. 복리 효과는 0.10 = 칸마다 ×1.10.")]
        public float valuePerLevel = 0.10f;
        [Tooltip("최대 레벨 = 트리에 놓인 이 종류의 칸 수. 생성기가 자동으로 맞춘다.")]
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
            // 부스터 위력 강화는 강화 칸이지만 효과가 켜고 끄는 것뿐이다 (+0 → +1로 보이던 문제)
            if (effectType.IsUnlock() || effectType == SkillEffectType.BoosterPower)
                return level > 0 ? "해금됨" : "미해금";

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
    /// 전역 코스트 곡선. 기획서의 의사코드에 후반 완화 구간을 더했다.
    ///
    ///   지수 e(N) = N-1                                  (N ≤ softcap)
    ///            = softcap-1 + (N-softcap)·ln(G2)/ln(G1)  (N > softcap)
    ///   currentCost = round(G1^e(N))
    ///   if (currentCost &lt;= previousCost) currentCost = previousCost + 1
    ///
    /// 칸이 231개라 한 가지 성장률로는 "클리어 4.5시간"과 "풀트리 달성"을 함께 맞출 수 없다.
    /// 그래서 softcap번째 칸부터 성장률을 낮춘다 (G2 = 1이면 그 뒤로는 +1씩만 오른다).
    /// 값은 GameDatabase에 있고, GameManager가 시작할 때 Configure로 넘겨준다.
    /// </summary>
    public static class SkillCostCurve
    {
        const int CacheSize = 1024;

        static double _growth = 1.10d;
        static double _growthLate = 1.0d;
        static int _softcap = 100;
        static double[] _cache;

        public static double Growth => _growth;
        public static double GrowthLate => _growthLate;
        public static int Softcap => _softcap;

        public static void Configure(double growth, double growthLate, int softcap)
        {
            growth = Math.Max(1.0001d, growth);
            growthLate = Math.Max(1d, growthLate);
            softcap = Math.Max(1, softcap);
            if (growth == _growth && growthLate == _growthLate && softcap == _softcap && _cache != null) return;
            _growth = growth;
            _growthLate = growthLate;
            _softcap = softcap;
            _cache = null;
        }

        static double Exponent(int n)
        {
            if (n <= _softcap) return n - 1;
            return (_softcap - 1) + (n - _softcap) * (Math.Log(_growthLate) / Math.Log(_growth));
        }

        static void Build()
        {
            _cache = new double[CacheSize + 1];
            double previous = 0d;
            for (int n = 1; n <= CacheSize; n++)
            {
                double current = Math.Round(Math.Pow(_growth, Exponent(n)), MidpointRounding.AwayFromZero);
                if (current <= previous) current = previous + 1d;
                _cache[n] = current;
                previous = current;
            }
        }

        /// <summary>N번째(1부터)로 찍는 칸의 기본 가격.</summary>
        public static double CostOfNode(int n)
        {
            if (n < 1) n = 1;
            if (_cache == null) Build();
            if (n <= CacheSize) return _cache[n];
            double far = Math.Round(Math.Pow(_growth, Exponent(n)), MidpointRounding.AwayFromZero);
            return Math.Max(far, _cache[CacheSize] + (n - CacheSize));   // 캐시 끝에서도 계속 오르게
        }

        /// <summary>칸 n개를 찍는 데 드는 누적 기본 비용 (밸런싱 검증용).</summary>
        public static double CumulativeCost(int nodeCount)
        {
            double sum = 0d;
            for (int i = 1; i <= nodeCount; i++) sum += CostOfNode(i);
            return sum;
        }

        /// <summary>
        /// 옛 곡선(1.12 단일 성장률)의 누적 비용. 예전 세이브를 새 트리로 옮길 때 환불액 계산에만 쓴다.
        /// </summary>
        public static double LegacyCumulativeCost(int nodeCount)
        {
            double sum = 0d, previous = 0d;
            for (int n = 1; n <= nodeCount; n++)
            {
                double current = Math.Round(Math.Pow(1.12d, n - 1), MidpointRounding.AwayFromZero);
                if (current <= previous) current = previous + 1d;
                sum += current;
                previous = current;
            }
            return sum;
        }
    }
}
