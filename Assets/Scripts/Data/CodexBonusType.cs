namespace FishGame.Data
{
    /// <summary>
    /// 물고기 도감 보너스 — 한 종을 codexMilestone(기본 100)마리 먹을 때마다 붙는 효과.
    /// 종마다 하나씩 배정한다. "이 물고기를 계속 먹을 이유"를 만드는 장치다.
    /// </summary>
    public enum CodexBonusType
    {
        /// <summary>보너스 없음</summary>
        None = 0,
        /// <summary>재화 획득량 (+%)</summary>
        Currency = 1,
        /// <summary>포식 시 회복 시간 (+%)</summary>
        TimeGain = 2,
        /// <summary>입 크기·흡입력 (+%)</summary>
        MouthPower = 3,
        /// <summary>이동 속도 (+%)</summary>
        MoveSpeed = 4,
        /// <summary>시야 (+%)</summary>
        Vision = 5,
        /// <summary>생존 제한시간 (+초)</summary>
        SurvivalTime = 6,
    }

    public static class CodexBonusTypeExtensions
    {
        public static string ToKorean(this CodexBonusType t) => t switch
        {
            CodexBonusType.Currency     => "재화",
            CodexBonusType.TimeGain     => "시간 회복",
            CodexBonusType.MouthPower   => "입·흡입력",
            CodexBonusType.MoveSpeed    => "이동 속도",
            CodexBonusType.Vision       => "시야",
            CodexBonusType.SurvivalTime => "제한시간",
            _                           => "-",
        };

        /// <summary>초 단위로 더해지는 효과인가? (아니면 퍼센트)</summary>
        public static bool IsFlat(this CodexBonusType t) => t == CodexBonusType.SurvivalTime;
    }
}
