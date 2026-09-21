namespace FishGame.Utils
{
    /// <summary>
    /// 개발자 모드가 켜는 스위치들.
    ///
    /// 이 클래스 자체는 모든 빌드에 들어가지만, 값을 바꾸는 쪽(DevConsole)은
    /// 에디터와 Development Build에서만 컴파일된다. 정식 빌드에서는 아무도 건드리지 않으므로
    /// 항상 기본값(꺼짐) 그대로다 — 게임 코드 곳곳에 #if를 흩뿌리지 않으려고 이렇게 나눴다.
    /// </summary>
    public static class DevFlags
    {
        /// <summary>잡아먹혀도 죽지 않는다.</summary>
        public static bool GodMode;

        /// <summary>제한시간이 줄지 않는다.</summary>
        public static bool InfiniteTime;

        /// <summary>
        /// 마우스가 개발자 창 위에 있다. 이때 좌·우클릭이 청소기·부스터로 새지 않게 막는다.
        /// </summary>
        public static bool PointerOverPanel;

        /// <summary>개발자 모드 코드가 이 빌드에 들어 있는가.</summary>
        public static bool Available =>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif
    }
}
