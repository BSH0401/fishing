namespace FishGame.Data
{
    /// <summary>
    /// AI 물고기의 단순 움직임 패턴.
    /// 기획서: "보스가 아닌 물고기들도 한 가지의 단순한 움직임 패턴을 가지게 됨"
    /// 맵 단계가 올라갈수록 더 뒤쪽 패턴이 등장한다.
    /// </summary>
    public enum AIPatternType
    {
        /// <summary>1맵: 한 방향으로 유영하다 벽에서 반전</summary>
        Straight = 0,
        /// <summary>1~2맵: 사인파를 그리며 유영</summary>
        SineWave = 1,
        /// <summary>2맵~: 일정 반경을 랜덤 배회</summary>
        Wander = 2,
        /// <summary>3맵~: 자기보다 작은 대상(플레이어 포함)을 추격</summary>
        Chase = 3,
        /// <summary>3맵~: 자기보다 큰 대상이 접근하면 도주</summary>
        Flee = 4,
        /// <summary>4맵: 멈춰 있다가 사거리 안에 들어오면 급습</summary>
        Ambush = 5,
    }
}
