using UnityEngine;

namespace FishGame.Data
{
    /// <summary>
    /// 게임에서 쓰는 효과음 모음.
    /// 클립이 비어 있어도 아무 일도 일어나지 않으므로, 나중에 .wav만 끌어다 넣으면 된다.
    ///
    /// Assets/_Project/Resources/SoundBank.asset 로 1개만 만든다.
    /// </summary>
    [CreateAssetMenu(fileName = "SoundBank", menuName = "FishGame/Sound Bank", order = -9)]
    public class SoundBank : ScriptableObject
    {
        [Header("포식")]
        [Tooltip("작은 물고기를 먹을 때")]
        public AudioClip eatSmall;
        [Tooltip("자기 크기에 가까운 물고기를 먹을 때")]
        public AudioClip eatBig;
        [Tooltip("보스를 삼킬 때")]
        public AudioClip eatBoss;

        [Header("액티브 스킬")]
        public AudioClip booster;
        public AudioClip vacuum;
        public AudioClip volt;
        public AudioClip missile;
        public AudioClip baitDrop;
        [Tooltip("비늘 경화가 공격을 막았을 때")]
        public AudioClip armorBlock;

        [Header("상태")]
        public AudioClip death;
        public AudioClip timeOut;
        [Tooltip("제한시간이 얼마 안 남았을 때 반복")]
        public AudioClip heartbeat;
        public AudioClip bossAppear;

        [Header("UI")]
        public AudioClip purchase;
        public AudioClip purchaseFail;
        public AudioClip buttonClick;

        [Header("배경음 (비워 두면 조용히 넘어간다)")]
        public AudioClip menuMusic;
        public AudioClip gameplayMusic;

        [Header("믹싱")]
        [Range(0f, 1f)] public float masterVolume = 0.8f;
        [Tooltip("같은 소리가 겹칠 때 음정을 이만큼 흔들어 단조로움을 없앤다")]
        [Range(0f, 0.5f)] public float pitchVariance = 0.12f;
        [Tooltip("같은 클립이 이 시간 안에 다시 울리면 무시한다 (연속 포식 시 귀 아픔 방지)")]
        [Range(0f, 0.2f)] public float minInterval = 0.04f;
    }
}
