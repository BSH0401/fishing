using UnityEngine;

namespace FishGame.Data
{
    /// <summary>
    /// 필드에 스폰되는 물고기 1종의 정의.
    /// Assets/_Project/ScriptableObjects/Fish/ 아래에 에셋으로 만든다.
    /// </summary>
    [CreateAssetMenu(fileName = "Fish_", menuName = "FishGame/Fish Species", order = 0)]
    public class FishSpecies : ScriptableObject
    {
        [Header("식별")]
        [Tooltip("도감 저장용 고유 키. 한번 정하면 바꾸지 말 것.")]
        public string speciesId = "";
        public string displayName = "이름 없는 물고기";
        [TextArea(2, 3)] public string codexNote = "";

        [Header("표시")]
        public Sprite sprite;
        [Tooltip("스프라이트가 기본적으로 오른쪽을 보고 있으면 false, 왼쪽이면 true")]
        public bool spriteFacesLeft = false;
        public Color tint = Color.white;

        [Header("크기")]
        [Tooltip("포식 판정의 기준값. 플레이어 size가 이 값 이상이면 잡아먹을 수 있다.")]
        [Min(0.01f)] public float size = 1f;
        [Tooltip("size에 곱해서 실제 월드 스케일을 만든다. 스프라이트 원본 크기 보정용.")]
        [Min(0.01f)] public float visualScaleMultiplier = 1f;

        [Header("이동")]
        public AIPatternType pattern = AIPatternType.Straight;
        [Min(0f)] public float moveSpeed = 2f;
        [Tooltip("패턴별 보조 값 — SineWave: 진폭, Wander: 배회 반경, Chase/Flee/Ambush: 감지 반경")]
        [Min(0f)] public float patternParam = 2f;
        [Tooltip("패턴별 보조 값2 — SineWave: 주기(Hz), Ambush: 급습 배속")]
        [Min(0f)] public float patternParam2 = 1f;

        [Header("유영 연출")]
        [Tooltip("선회 속도 (도/초). 작을수록 크고 둔한 물고기처럼 돈다.")]
        [Min(10f)] public float turnRate = 240f;
        [Tooltip("꼬리 흔들림 세기(도). 0이면 흔들지 않는다.")]
        [Range(0f, 25f)] public float swayAmplitude = 7f;
        [Tooltip("꼬리 흔들림 기본 주기(Hz)")]
        [Range(0.1f, 8f)] public float swayFrequency = 2.2f;

        [Header("보상")]
        [Min(0f)] public float currencyReward = 1f;
        [Tooltip("잡아먹었을 때 회복되는 생존 제한시간(초)")]
        [Min(0f)] public float timeReward = 1.5f;

        [Header("도감 보너스")]
        [Tooltip("이 종을 codexMilestone(기본 100)마리 먹을 때마다 붙는 효과")]
        public CodexBonusType codexBonusType = CodexBonusType.Currency;
        [Tooltip("단계당 값. 퍼센트 효과는 0.02 = +2%, 제한시간은 초 단위.")]
        [Min(0f)] public float codexBonusPerTier = 0.02f;

        [Header("기타")]
        [Tooltip("청소기로 끌어당길 수 있는 최대 크기 비율. 플레이어 size * 이 값 이하만 끌려온다.")]
        [Range(0f, 1f)] public float vacuumableSizeRatio = 0.6f;
        public bool isBoss = false;

        public float WorldScale => size * visualScaleMultiplier;

        /// <summary>도감 키. 비어 있으면 에셋 이름을 쓴다.</summary>
        public string CodexKey => string.IsNullOrEmpty(speciesId) ? name : speciesId;

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(speciesId)) speciesId = name;
        }
    }
}
