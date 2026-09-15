namespace FishGame.Data
{
    /// <summary>
    /// 스킬 노드가 주는 효과. 기획서 2판 기준.
    ///
    /// Unlock*  : 액티브 스킬 해금 (레벨 1 고정, 찍는 순간 사용 가능)
    /// 나머지   : 강화 노드 (레벨당 누적)
    /// </summary>
    public enum SkillEffectType
    {
        // ── 액티브 해금 ─────────────────────────────────────────
        /// <summary>부스터 — 우클릭. 앞으로 짧게 대쉬하며 경로의 적을 먹는다.</summary>
        UnlockBooster = 0,
        /// <summary>청소기 — 좌클릭. 범위 내 소형 물고기를 빨아들인다.</summary>
        UnlockVacuum = 1,
        /// <summary>비늘 경화 — 적의 공격을 1회 막는다.</summary>
        UnlockScaleArmor = 2,
        /// <summary>황금 미끼 — 자동. 미끼를 뿌려 주변 물고기를 끌어모은다.</summary>
        UnlockGoldenBait = 3,
        /// <summary>10만 볼트 — 자동. 주변에 전기 충격을 일으켜 마비시킨다.</summary>
        UnlockVolt = 4,
        /// <summary>미사일 — 자동. 바라보는 방향으로 발사, 맞은 적은 즉시 먹힌다.</summary>
        UnlockMissile = 5,

        // ── 기본 강화 ───────────────────────────────────────────
        /// <summary>커다란 배터리 — 생존 제한시간 +1초 (레벨당 가산)</summary>
        SurvivalTime = 100,
        /// <summary>덧붙인 장갑 — 몸 크기 및 이동속도 +10% (복리)</summary>
        BodyScale = 101,
        /// <summary>치아 교정 — 입 크기 및 흡입력 +10% (복리)</summary>
        MouthPower = 102,
        /// <summary>카메라 장착 — 시야 +10% (복리)</summary>
        Vision = 103,
        /// <summary>물고기 전지 — 포식 시 얻는 제한시간 +5% (복리)</summary>
        TimeGain = 104,
        /// <summary>위액 산성도 증가 — 포식 시 얻는 재화 +10% (복리)</summary>
        CurrencyGain = 105,

        // ── 액티브 강화 ─────────────────────────────────────────
        /// <summary>부스터 거리 강화 — 이동 거리 +30% (복리)</summary>
        BoosterRange = 200,
        /// <summary>부스터 위력 강화 — 부스터 중에는 더 큰 물고기도 먹는다 (1레벨)</summary>
        BoosterPower = 201,
        /// <summary>청소기 범위 강화 — 효과 범위 +50% (복리)</summary>
        VacuumRange = 202,
        /// <summary>비늘 경화 강화 — 방어 횟수 +1 (최대 3회)</summary>
        ScaleArmorStack = 203,
        /// <summary>황금 미끼 범위 강화 — 효과 범위 +50% (복리)</summary>
        BaitRange = 204,
        /// <summary>황금 미끼 추가 — 미끼 개수 +1</summary>
        BaitCount = 205,
        /// <summary>10만 볼트 강화 — 범위 및 마비 시간 +50% (복리)</summary>
        VoltPower = 206,
        /// <summary>미사일 발사 강화 — 범위 +50%, 미사일 개수 +1</summary>
        MissilePower = 207,
    }

    public static class SkillEffectTypeExtensions
    {
        /// <summary>액티브 해금 노드인가? (최대 레벨 1)</summary>
        public static bool IsUnlock(this SkillEffectType t) => (int)t < 100;

        /// <summary>액티브 스킬을 강화하는 노드인가? (선행으로 해금이 필요)</summary>
        public static bool IsActiveUpgrade(this SkillEffectType t) => (int)t >= 200;

        /// <summary>퍼센트(복리)로 적용되는 효과인가?</summary>
        public static bool IsMultiplicative(this SkillEffectType t)
        {
            switch (t)
            {
                case SkillEffectType.BodyScale:
                case SkillEffectType.MouthPower:
                case SkillEffectType.Vision:
                case SkillEffectType.TimeGain:
                case SkillEffectType.CurrencyGain:
                case SkillEffectType.BoosterRange:
                case SkillEffectType.VacuumRange:
                case SkillEffectType.BaitRange:
                case SkillEffectType.VoltPower:
                case SkillEffectType.MissilePower:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>이 강화 노드가 필요로 하는 액티브 해금. 없으면 null.</summary>
        public static SkillEffectType? RequiredUnlock(this SkillEffectType t)
        {
            switch (t)
            {
                case SkillEffectType.BoosterRange:
                case SkillEffectType.BoosterPower:     return SkillEffectType.UnlockBooster;
                case SkillEffectType.VacuumRange:      return SkillEffectType.UnlockVacuum;
                case SkillEffectType.ScaleArmorStack:  return SkillEffectType.UnlockScaleArmor;
                case SkillEffectType.BaitRange:
                case SkillEffectType.BaitCount:        return SkillEffectType.UnlockGoldenBait;
                case SkillEffectType.VoltPower:        return SkillEffectType.UnlockVolt;
                case SkillEffectType.MissilePower:     return SkillEffectType.UnlockMissile;
                default:                               return null;
            }
        }
    }
}
