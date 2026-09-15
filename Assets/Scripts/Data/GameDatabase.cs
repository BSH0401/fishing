using System.Collections.Generic;
using UnityEngine;

namespace FishGame.Data
{
    /// <summary>
    /// 게임 전체가 참조하는 데이터 루트.
    /// Assets/_Project/Resources/GameDatabase.asset 로 1개만 만든다.
    /// (Resources에 두는 이유: 씬의 인스펙터 참조가 끊겨도 GameManager가 스스로 찾을 수 있게)
    /// </summary>
    [CreateAssetMenu(fileName = "GameDatabase", menuName = "FishGame/Game Database", order = -10)]
    public class GameDatabase : ScriptableObject
    {
        [Header("콘텐츠")]
        [Tooltip("mapIndex 순서대로 넣을 것 (0,1,2,3)")]
        public List<MapData> maps = new List<MapData>();
        public List<SkillNode> skills = new List<SkillNode>();
        [Tooltip("도감·스탯 계산에 쓰는 전체 물고기 목록 (보스 포함). 생성기가 채운다.")]
        public List<FishSpecies> allFish = new List<FishSpecies>();

        [Header("플레이어 기본 스탯 (스킬 0레벨 기준)")]
        [Min(1f)] public float baseSurvivalTime = 30f;
        [Min(0.01f)] public float baseSize = 1f;
        [Min(0.1f)] public float baseMoveSpeed = 6.5f;
        [Tooltip("입 판정 반경 (플레이어 size에 비례)")]
        [Min(0.05f)] public float baseMouthRatio = 0.45f;
        [Tooltip("카메라 기본 시야 (orthographic size)")]
        [Min(1f)] public float baseVision = 6.5f;

        [Header("크기 ↔ 속도 연동")]
        [Tooltip("덧붙인 장갑은 크기와 속도를 함께 올린다. 다만 속도까지 크기와 똑같이 올리면 " +
                 "후반에 조작이 불가능해지므로 지수로 완화한다.\n" +
                 "1 = 기획서 그대로(크기와 동일), 0.5 = 제곱근(권장), 0 = 속도 증가 없음")]
        [Range(0f, 1f)] public float speedScalingExponent = 0.5f;
        [Tooltip("이동 속도 상한 (월드 유닛/초)")]
        [Min(1f)] public float maxMoveSpeed = 26f;

        // ══════════════════════════════════════════════════════════
        //  액티브 스킬 기본값
        // ══════════════════════════════════════════════════════════
        [Header("부스터 (우클릭)")]
        [Tooltip("기본 이동 거리 (플레이어 크기 배수)")]
        [Min(0.1f)] public float boosterDistance = 4.5f;
        [Min(0.02f)] public float boosterDuration = 0.18f;
        [Min(0.1f)] public float boosterCooldown = 3.5f;
        [Tooltip("부스터 중 경로 판정 반경 (플레이어 size 배수)")]
        [Min(0.1f)] public float boosterHitRadiusRatio = 0.7f;
        [Tooltip("부스터 위력 강화 전까지, 부스터로 먹을 수 있는 최대 크기 배수")]
        [Min(1f)] public float boosterEatSizeMultiplier = 1.6f;

        [Header("청소기 (좌클릭, 홀드)")]
        [Min(0.1f)] public float vacuumRadius = 5f;
        [Tooltip("빨아들이는 힘")]
        [Min(0.1f)] public float vacuumPullForce = 16f;
        [Tooltip("플레이어 size × 이 값 이하만 끌려온다")]
        [Range(0.05f, 1f)] public float vacuumSizeRatio = 0.6f;

        [Header("비늘 경화")]
        [Tooltip("방어 성공 후 무적 시간")]
        [Min(0.1f)] public float armorInvulnerability = 1.2f;
        [Tooltip("방어 성공 시 뒤로 밀려나는 힘")]
        [Min(0f)] public float armorKnockback = 12f;
        [Tooltip("방어 횟수 상한 (기획서: 최대 3회)")]
        [Min(1)] public int armorMaxStacks = 3;

        [Header("황금 미끼 (자동)")]
        [Min(0.5f)] public float baitInterval = 8f;
        [Min(0.5f)] public float baitLifetime = 6f;
        [Tooltip("미끼가 물고기를 끌어당기는 반경")]
        [Min(0.5f)] public float baitRadius = 8f;
        [Tooltip("미끼로 향할 때의 속도 배수")]
        [Min(1f)] public float baitLureSpeedMultiplier = 1.35f;

        [Header("10만 볼트 (자동)")]
        [Min(0.5f)] public float voltInterval = 10f;
        [Min(0.5f)] public float voltRadius = 6f;
        [Tooltip("일반 물고기 마비 시간")]
        [Min(0f)] public float voltStunNormal = 2f;
        [Tooltip("보스 마비 시간")]
        [Min(0f)] public float voltStunBoss = 0.1f;

        [Header("미사일 (자동)")]
        [Min(0.5f)] public float missileInterval = 6f;
        [Min(1f)] public float missileSpeed = 14f;
        [Min(0.5f)] public float missileLifetime = 3f;
        [Tooltip("명중 판정 반경")]
        [Min(0.1f)] public float missileHitRadius = 1.1f;
        [Tooltip("미사일이 죽일 수 있는 최대 크기 (플레이어 size 배수). 보스는 제외.")]
        [Min(1f)] public float missileMaxTargetSizeMultiplier = 3f;
        [Tooltip("여러 발일 때 부채꼴 총 각도(도)")]
        [Min(0f)] public float missileSpreadAngle = 24f;

        // ══════════════════════════════════════════════════════════
        //  규칙
        // ══════════════════════════════════════════════════════════
        [Header("규칙")]
        [Tooltip("포식 판정 여유. 플레이어 size * 이 값 >= 상대 size 면 먹을 수 있다.")]
        [Range(0.5f, 1.5f)] public float eatSizeTolerance = 1.0f;
        [Tooltip("사망(피포식) 시 잃는 재화 비율")]
        [Range(0f, 1f)] public float deathCurrencyPenalty = 0.3f;

        [Header("인게임 성장 — 먹을수록 커진다")]
        [Tooltip("끄면 한 판 동안 크기가 고정된다 (스킬트리로만 커짐)")]
        public bool inRunGrowthEnabled = true;
        [Tooltip("질량 보존식으로 자란다: 새 크기 = √(내 크기² + 먹이 크기² × 효율)\n" +
                 "덕분에 커질수록 잔챙이로는 잘 안 크는 자연스러운 감속이 생긴다.")]
        [Range(0.01f, 1f)] public float growthMassEfficiency = 0.15f;
        [Tooltip("한 판에서 커질 수 있는 최대 배율 (판 시작 크기 대비)")]
        [Min(1f)] public float growthMaxMultiplier = 2.5f;

        [Header("제한시간 압박")]
        [Tooltip("경과 60초마다 초당 제한시간 소모가 이만큼 늘어난다.\n" +
                 "0이면 강화가 쌓였을 때 한 판이 무한정 길어져 플레이 루프가 무너진다.")]
        [Range(0f, 6f)] public float timeDrainAccelerationPer60s = 2.2f;
        [Min(1f)] public float maxTimeDrainMultiplier = 10f;

        [Tooltip("보스 게이트를 '스킬트리로 올린 기본 크기'로만 판정한다.")]
        public bool bossGateUsesBaseSize = true;

        [Header("물고기 도감")]
        [Tooltip("한 종을 이만큼 먹을 때마다 그 종에서 얻는 재화가 늘어난다.")]
        [Min(1)] public int codexMilestone = 100;
        [Tooltip("도감을 모두 채웠을 때가 너무 강해지지 않도록 단계 수에 상한을 둔다.")]
        [Min(1)] public int codexMaxTiers = 10;

        Dictionary<string, SkillNode> _skillById;

        public SkillNode GetSkill(string id)
        {
            if (_skillById == null || _skillById.Count != skills.Count)
            {
                _skillById = new Dictionary<string, SkillNode>(skills.Count);
                foreach (var s in skills)
                    if (s != null && !string.IsNullOrEmpty(s.id)) _skillById[s.id] = s;
            }
            return _skillById.TryGetValue(id, out var node) ? node : null;
        }

        public MapData GetMap(int index)
        {
            foreach (var m in maps)
                if (m != null && m.mapIndex == index) return m;
            return null;
        }

        public int MapCount => maps.Count;
    }
}
