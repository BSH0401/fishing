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
        [Tooltip("위에서 아래 순서로 넣을 것 (0 어항 → 3 바다). 하나의 통합 맵을 이룬다.")]
        public List<ZoneData> zones = new List<ZoneData>();
        [Tooltip("강화 종류 목록 (배터리·장갑·치아…). 스탯 계산은 이 목록을 돈다.")]
        public List<SkillNode> skills = new List<SkillNode>();
        [Tooltip("스킬트리 도면의 칸들. 생성기가 기획 도면에서 채운다.")]
        public List<SkillTreeSlot> skillTree = new List<SkillTreeSlot>();
        [Tooltip("skillTree에서 시작 칸의 인덱스")]
        public int skillTreeRoot = 0;
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

        [Header("상한 — 스킬을 다 찍어도 게임이 깨지지 않게")]
        [Tooltip("플레이어 크기의 절대 상한. 스킬로 올린 기본 크기와 판 중 성장 모두에 적용된다.\n" +
                 "보스(43)를 먹을 수 있고, 강→바다 통로를 지날 수 있는 선에서 정한다.")]
        [Min(1f)] public float maxPlayerSize = 56f;
        [Tooltip("치아 교정(입 크기) 배율 상한. 입 판정이 몸보다 지나치게 커지는 걸 막는다.\n" +
                 "2.6 = 치아 교정 47칸(×2.54)에 도감 보너스가 조금 더 얹힐 여유")]
        [Min(1f)] public float maxMouthMultiplier = 2.6f;

        [Header("액티브 스킬 ↔ 크기")]
        [Tooltip("청소기·볼트·미끼·미사일의 범위를 플레이어 크기에 비례시킨다.\n" +
                 "맵이 아래로 갈수록 몇십 배 커지므로, 고정 범위면 후반에 스킬이 몸 안에 파묻힌다.")]
        public bool activeRangeScalesWithSize = true;

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
        [Tooltip("마비 시간 상한 = 볼트 쿨타임 × 이 값.\n" +
                 "1 이상이면 다음 볼트 전에 풀리지 않아 주변이 영구 마비된다.")]
        [Range(0.1f, 1f)] public float voltStunCapRatio = 0.6f;

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

        [Header("깊이 ↔ 가치")]
        [Tooltip("모든 재화에 곱해지는 전역 배율. 총 플레이타임을 맞추는 유일한 손잡이다.\n" +
                 "Tools/tune.py 가 잡아준 값을 넣는다.")]
        [Min(0.0001f)] public float globalValueScale = 1f;

        [Tooltip("맵 바닥에서 얻는 재화가 맵 천장보다 몇 배인지.\n" +
                 "깊이에 따라 매끄럽게(지수적으로) 올라가므로 구역 경계에서 값이 튀지 않는다.\n" +
                 "8 = 바다 바닥이 어항 수면보다 8배 값지다.")]
        [Min(1f)] public float depthRichness = 8f;

        [Header("통합 맵 / 통로")]
        [Tooltip("통로는 '판 중에 먹어서 커진 현재 크기'로 열린다.\n" +
                 "체크를 풀면 스킬트리로 올린 기본 크기로만 판정한다(예전 보스 게이트 방식).")]
        public bool gateUsesCurrentSize = true;
        [Tooltip("한 판에서 새로 도달한 구역을 영구 기록할지. 시작 구역 선택 범위가 된다.")]
        public bool recordDeepestZone = true;

        [Header("히든 아이템")]
        [Tooltip("하나 먹을 때마다 재화 획득에 더해지는 비율 (가산). 0.05 = +5%")]
        [Range(0f, 0.5f)] public float hiddenItemCurrencyBonus = 0.05f;

        [Header("스킬 비용 곡선")]
        [Tooltip("N번째 칸의 가격 = round(성장률 ^ (N-1)). 정체되면 +1.\n" +
                 "Tools/tune.py가 클리어 4.5시간에 맞춘 값.")]
        [Min(1.001f)] public float skillCostGrowth = 1.10f;
        [Tooltip("이 칸 수부터는 아래 '후반 성장률'로 오른다. 칸이 231개라 한 가지 성장률로는 " +
                 "클리어 시간과 풀트리 달성을 동시에 맞출 수 없다.")]
        [Min(1)] public int skillCostSoftcap = 100;
        [Tooltip("softcap 이후 성장률. 1이면 가격이 사실상 고정된다 (+1씩).")]
        [Min(1f)] public float skillCostGrowthLate = 1.0f;

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

        Dictionary<string, int> _slotById;

        /// <summary>칸 id → 인덱스. 없으면 -1.</summary>
        public int SlotIndex(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            if (_slotById == null || _slotById.Count != skillTree.Count)
            {
                _slotById = new Dictionary<string, int>(skillTree.Count);
                for (int i = 0; i < skillTree.Count; i++)
                    if (skillTree[i] != null && !string.IsNullOrEmpty(skillTree[i].id))
                        _slotById[skillTree[i].id] = i;
            }
            return _slotById.TryGetValue(id, out int idx) ? idx : -1;
        }

        public SkillTreeSlot GetSlot(int index) =>
            index >= 0 && index < skillTree.Count ? skillTree[index] : null;

        /// <summary>그 강화 종류의 칸이 트리에 몇 개 있는가 (= 최대 레벨).</summary>
        public int SlotCountOf(SkillNode node)
        {
            int n = 0;
            foreach (var s in skillTree) if (s != null && s.skill == node) n++;
            return n;
        }

        /// <summary>시작 칸을 뺀 찍을 수 있는 칸 수.</summary>
        public int PurchasableSlotCount
        {
            get
            {
                int n = 0;
                foreach (var s in skillTree) if (s != null && !s.IsRoot) n++;
                return n;
            }
        }

        void OnValidate()
        {
            _slotById = null;
            _skillById = null;
        }

        public ZoneData GetZone(int index)
        {
            foreach (var z in zones)
                if (z != null && z.zoneIndex == index) return z;
            return index >= 0 && index < zones.Count ? zones[index] : null;
        }

        public int ZoneCount => zones.Count;

        /// <summary>최하단 존(바다)의 인덱스.</summary>
        public int DeepestZoneIndex => Mathf.Max(0, zones.Count - 1);
    }
}
