#if UNITY_EDITOR
using System.Collections.Generic;
using FishGame.Data;
using UnityEditor;
using UnityEngine;

namespace FishGame.EditorTools
{
    /// <summary>
    /// 기획서 2판 + 통합 맵 + 밸런스 시뮬레이션(Tools/balance_sim.py) 확정 수치로
    /// 물고기 / 구역 / 스킬 / GameDatabase 에셋을 한 번에 만든다.
    ///
    /// 맵 4개가 세로로 이어진 하나의 맵이 되었다.
    /// 통로는 "판 중에 먹어서 커진 현재 크기"로 열리고, 아래로 갈수록 값이 오른다.
    ///
    /// 검증 결과: 25회 시행 전부 완주, 총 플레이타임 4.42 ~ 4.62시간 (중앙 4.51h)
    /// (도감 보너스 + 인게임 성장 + 히든 아이템 8개 포함)
    ///
    /// 메뉴: FishGame ▸ 1. 콘텐츠 에셋 생성
    /// </summary>
    public static class ContentGenerator
    {
        const string SoFolder    = "Assets/_Project/ScriptableObjects";
        const string FishFolder  = SoFolder + "/Fish";
        const string ZoneFolder  = SoFolder + "/Zones";
        const string LegacyMapFolder = SoFolder + "/Maps";
        const string SkillFolder = SoFolder + "/Skills";

        const string ResourcesFolder = "Assets/_Project/Resources";
        public const string DatabasePath = ResourcesFolder + "/GameDatabase.asset";
        const string LegacyDatabasePath  = SoFolder + "/GameDatabase.asset";

        // ═════════════════════════════════════════════════════════
        //  물고기
        // ═════════════════════════════════════════════════════════
        struct FishDef
        {
            public string file, display;
            public float size, money, time, speed, param, param2, turn, sway, swayHz;
            public AIPatternType pattern;
            public Color color;
        }

        static FishDef F(string file, string display, float size, float money, float time,
                         AIPatternType pattern, float speed, float param, Color color,
                         float param2 = 1f, float turn = 260f, float sway = 8f, float swayHz = 2.4f)
            => new FishDef
            {
                file = file, display = display, size = size, money = money, time = time,
                pattern = pattern, speed = speed, param = param, param2 = param2,
                color = color, turn = turn, sway = sway, swayHz = swayHz
            };

        static readonly Color C1 = new Color(0.42f, 0.72f, 0.62f);
        static readonly Color C2 = new Color(0.95f, 0.62f, 0.35f);
        static readonly Color C3 = new Color(0.55f, 0.60f, 0.85f);
        static readonly Color C4 = new Color(0.90f, 0.45f, 0.50f);
        static readonly Color C5 = new Color(0.35f, 0.38f, 0.45f);
        static readonly Color CB = new Color(0.20f, 0.22f, 0.28f);

        // 스폰 가중치 — 작을수록 자주 나온다
        static readonly float[] SpawnWeights = { 34f, 26f, 20f, 13f, 7f };

        // 도감 보너스 — 구역 안에서 크기 순서대로 배정한다.
        // 작은 종일수록 많이 먹게 되므로 효과를 작게, 큰 종은 드무니 크게.
        static readonly (CodexBonusType type, float value)[] CodexByRank =
        {
            (CodexBonusType.Currency,     0.020f),  // 가장 흔한 잡어 — 재화 +2%/단계
            (CodexBonusType.TimeGain,     0.020f),
            (CodexBonusType.MouthPower,   0.018f),
            (CodexBonusType.MoveSpeed,    0.015f),
            (CodexBonusType.SurvivalTime, 0.60f),   // 가장 큰 종 — 제한시간 +0.6초/단계
        };

        static readonly FishDef[][] MapFish =
        {
            // ── 1구역: 어항 (가중평균 size 0.83 / 재화 0.29) ──
            new[]
            {
                F("Fish_M1_Fry",     "치어",        0.30f, 0.12f, 0.7f, AIPatternType.Straight, 2.0f, 0f,   C1, 1f, 380f, 11f, 3.4f),
                F("Fish_M1_Minnow",  "송사리",      0.55f, 0.21f, 0.9f, AIPatternType.SineWave, 2.4f, 1.1f, C1, 0.7f, 340f, 10f, 3.0f),
                F("Fish_M1_Guppy",   "실험체 구피", 0.95f, 0.34f, 1.1f, AIPatternType.Wander,   2.2f, 4f,   C2, 1f, 300f, 9f, 2.6f),
                F("Fish_M1_Angel",   "수조 엔젤",   1.60f, 0.55f, 1.4f, AIPatternType.Flee,     2.8f, 5f,   C3, 1f, 260f, 8f, 2.2f),
                F("Fish_M1_Keeper",  "수조 포식자", 2.60f, 0.85f, 1.8f, AIPatternType.Chase,    3.0f, 6f,   C4, 1f, 220f, 7f, 2.0f),
            },
            // ── 2구역: 하수구 (2.75 / 1.21) ──
            new[]
            {
                F("Fish_M2_Larva",   "하수 유생",   1.00f, 0.45f, 0.9f, AIPatternType.SineWave, 2.8f, 1.3f, C1, 0.8f, 360f, 11f, 3.2f),
                F("Fish_M2_Sludge",  "슬러지피시",  1.90f, 0.85f, 1.1f, AIPatternType.Wander,   3.0f, 5f,   C3, 1f, 300f, 9f, 2.6f),
                F("Fish_M2_Pipe",    "관벌레고기",  3.20f, 1.40f, 1.3f, AIPatternType.Flee,     3.6f, 6f,   C2, 1f, 270f, 8f, 2.3f),
                F("Fish_M2_Rat",     "하수 쥐치",   5.20f, 2.30f, 1.6f, AIPatternType.Chase,    3.8f, 8f,   C4, 1f, 230f, 7f, 2.0f),
                F("Fish_M2_Grate",   "철망 포식자", 8.50f, 3.60f, 2.0f, AIPatternType.Ambush,   3.2f, 9f,   C5, 3.0f, 190f, 6f, 1.7f),
            },
            // ── 3구역: 강 (7.37 / 4.94) ──
            new[]
            {
                F("Fish_M3_Sweet",   "은어떼",      2.70f,  1.8f, 1.2f, AIPatternType.SineWave, 3.6f, 1.8f, C1, 0.9f, 340f, 10f, 3.0f),
                F("Fish_M3_Crucian", "붕어",        5.00f,  3.4f, 1.4f, AIPatternType.Wander,   3.4f, 6f,   C3, 1f, 290f, 9f, 2.5f),
                F("Fish_M3_Carp",    "잉어",        8.60f,  5.8f, 1.6f, AIPatternType.Flee,     4.0f, 8f,   C2, 1f, 250f, 8f, 2.2f),
                F("Fish_M3_Cat",     "메기",       14.00f,  9.5f, 1.9f, AIPatternType.Chase,    4.4f, 11f,  C4, 1f, 210f, 7f, 1.9f),
                F("Fish_M3_Snake",   "가물치",     23.00f, 15.0f, 2.3f, AIPatternType.Ambush,   3.4f, 10f,  C5, 3.2f, 180f, 6f, 1.6f),
            },
            // ── 4구역: 바다 (17.8 / 19.6) ──
            new[]
            {
                F("Fish_M4_Sardine", "정어리 군집",  6.50f,  7.0f, 1.4f, AIPatternType.SineWave, 4.2f, 2.2f, C1, 1.0f, 330f, 10f, 2.9f),
                F("Fish_M4_Mackerel","고등어",      12.00f, 13.0f, 1.6f, AIPatternType.Wander,   4.4f, 8f,   C3, 1f, 280f, 9f, 2.4f),
                F("Fish_M4_Tuna",    "참다랑어",    21.00f, 23.0f, 1.9f, AIPatternType.Flee,     4.8f, 10f,  C2, 1f, 240f, 8f, 2.1f),
                F("Fish_M4_Shark",   "청상아리",    34.00f, 38.0f, 2.2f, AIPatternType.Chase,    5.4f, 14f,  C4, 1f, 200f, 7f, 1.8f),
                F("Fish_M4_Orca",    "범고래",      55.00f, 62.0f, 2.6f, AIPatternType.Ambush,   4.4f, 13f,  C5, 3.4f, 170f, 6f, 1.5f),
            },
        };

        // ── 희귀 대형어 ───────────────────────────────────────────
        // 예전 1~3맵 보스였던 것들. 맵이 하나로 합쳐지면서 "길을 막는 보스" 자리가 없어졌지만,
        // 구역마다 가끔 나타나는 위협적인 대어로 남겨 두는 편이 낫다.
        // 잡으면 값이 크고, 못 잡으면 피해 다녀야 하는 긴장을 준다.
        static readonly FishDef[] Rares =
        {
            F("Fish_Rare1", "관리 로봇 · 게이트키퍼",  2.95f,  4.0f, 2.2f, AIPatternType.Wander, 2.4f, 6f,  CB, 1f, 200f, 5f, 1.6f),
            F("Fish_Rare2", "배수관 지킴이",           7.70f, 16.0f, 2.6f, AIPatternType.Chase,  3.2f, 12f, CB, 1f, 180f, 5f, 1.5f),
            F("Fish_Rare3", "강의 주인 · 늙은 가물치",20.00f, 70.0f, 3.0f, AIPatternType.Ambush, 3.8f, 14f, CB, 3.4f, 160f, 4f, 1.4f),
        };

        // ── 보스 ──────────────────────────────────────────────────
        // 이제 단 하나. 바다 한가운데 고리 구조물에 갇혀 있고,
        // 부스터로 들이받아야 풀려난다. 잡으면 게임 클리어.
        static readonly FishDef Boss =
            F("Fish_Boss_Leviathan", "심해 리바이어던", 43.00f, 260.0f, 0f,
              AIPatternType.Chase, 4.6f, 20f, CB, 1f, 150f, 4f, 1.3f);

        // ═════════════════════════════════════════════════════════
        //  구역 — 재화 배율은 balance_sim.py의 확정값
        // ═════════════════════════════════════════════════════════
        struct ZoneDef
        {
            public string file, display, desc;
            /// <summary>재화 배율 (balance_sim.py 확정값)</summary>
            public float currencyMult;
            /// <summary>세로 길이</summary>
            public float height;
            /// <summary>실루엣: (정규화 깊이 t, 반폭) 쌍. t=0 천장, t=1 바닥.</summary>
            public (float t, float hw)[] profile;
            /// <summary>아래 통로: 반폭 / 길이 / 열리는 데 필요한 크기</summary>
            public float exitHalfWidth, exitHeight, exitRequiredSize;
            public bool hasExit;
            public int population;
            public Color water;
            /// <summary>장애물: (모양, 구역 안 깊이 t, 반폭 대비 좌우 비율, 크기, 회전)</summary>
            public (ObstacleShape shape, float t, float side, float size, float rot)[] obstacles;
        }

        static (float, float)[] P(params (float, float)[] pts) => pts;

        static (ObstacleShape, float, float, float, float)[] O(
            params (ObstacleShape, float, float, float, float)[] items) => items;

        // 실루엣은 기획 PPT의 그림을 그대로 옮긴 것이다.
        //   어항   사발 — 넓게 시작해 배수구로 좁아진다
        //   하수구 상자 — 곧은 벽
        //   강     렌즈 — 좁게 들어가 넓어졌다가 다시 좁아진다
        //   바다   개활 — 입구만 좁고 그 아래는 전부 트여 있다
        static readonly ZoneDef[] Zones =
        {
            new ZoneDef {
                file = "Zone_1_Tank", display = "어항", currencyMult = 0.1474f,
                height = 46f,
                profile = P((0f, 22f), (0.45f, 21f), (0.80f, 14f), (1f, 7f)),
                hasExit = true, exitHalfWidth = 7f, exitHeight = 16f, exitRequiredSize = 3.1f,
                population = 26, water = new Color(0.55f, 0.80f, 0.72f),
                // 어항 장식품 — 성 하나, 집 하나. 사발이 좁아지기 전 넓은 구간에 둔다.
                obstacles = O((ObstacleShape.Box, 0.34f,  0.46f, 7.5f,  0f),
                              (ObstacleShape.Box, 0.52f, -0.50f, 6.0f, 12f)),
                desc = "탈출은 여기서 시작된다. 바닥의 배수구가 유일한 출구다." },

            new ZoneDef {
                file = "Zone_2_Sewer", display = "하수구", currencyMult = 0.0655f,
                height = 110f,
                profile = P((0f, 30f), (0.12f, 52f), (0.88f, 52f), (1f, 34f)),
                hasExit = true, exitHalfWidth = 15f, exitHeight = 22f, exitRequiredSize = 8.1f,
                population = 30, water = new Color(0.42f, 0.55f, 0.52f),
                // 쇠창살 원형 둘 — PPT 그림대로 가운데 높이에 좌우 대칭으로.
                obstacles = O((ObstacleShape.Disc, 0.42f, -0.42f, 17f, 0f),
                              (ObstacleShape.Disc, 0.42f,  0.42f, 17f, 0f)),
                desc = "탁한 물살과 쇠창살. 여기서 살아남으면 강 냄새가 난다." },

            new ZoneDef {
                file = "Zone_3_River", display = "강", currencyMult = 0.0271f,
                height = 260f,
                profile = P((0f, 22f), (0.35f, 120f), (0.70f, 142f), (1f, 34f)),
                hasExit = true, exitHalfWidth = 32f, exitHeight = 40f, exitRequiredSize = 21f,
                population = 34, water = new Color(0.35f, 0.66f, 0.78f),
                // 자갈 무더기 — 위쪽 중앙에 큰 것 하나, 아래쪽에 작은 것 둘.
                obstacles = O((ObstacleShape.Triangle, 0.22f,  0.00f, 34f, 0f),
                              (ObstacleShape.Triangle, 0.55f, -0.55f, 26f, 0f),
                              (ObstacleShape.Triangle, 0.66f,  0.52f, 22f, 0f)),
                desc = "물살이 세다. 여기서부터는 이빨을 가진 것들이 많다." },

            new ZoneDef {
                file = "Zone_4_Ocean", display = "바다", currencyMult = 0.0124f,
                height = 520f,
                profile = P((0f, 60f), (0.18f, 250f), (0.45f, 310f), (1f, 300f)),
                hasExit = false, exitHalfWidth = 30f, exitHeight = 0f, exitRequiredSize = 0f,
                population = 38, water = new Color(0.16f, 0.34f, 0.55f),
                // 암초 — 보스 구조물(t=0.46 중앙)을 피해 사방에 흩어 둔다.
                obstacles = O((ObstacleShape.Triangle, 0.26f, -0.58f, 62f, 0f),
                              (ObstacleShape.Triangle, 0.30f,  0.62f, 54f, 0f),
                              (ObstacleShape.Triangle, 0.68f, -0.44f, 70f, 0f),
                              (ObstacleShape.Triangle, 0.78f,  0.50f, 58f, 0f)),
                desc = "실험실에서 가장 먼 곳. 여기서는 무엇이든 나를 삼킬 수 있다." },
        };

        /// <summary>구역마다 숨겨 둘 히든 아이템 수 (먹으면 영구 재화 +5%).</summary>
        const int HiddenPerZone = 2;

        // ═════════════════════════════════════════════════════════
        //  스킬 — balance_sim.py의 NODES와 1:1 대응
        // ═════════════════════════════════════════════════════════
        struct Req { public string id; public int level; }
        static Req R(string id, int level) => new Req { id = id, level = level };

        struct SkillDef
        {
            public string id, display, tooltip, desc;
            public SkillEffectType effect;
            public float value, costMultiplier;
            public int maxLevel;
            public Vector2Int grid;
            public Req[] prereq;
        }

        static SkillDef S(string id, string display, SkillEffectType effect, float value, int maxLevel,
                          float costMult, int gx, int gy, string tooltip, string desc, params Req[] prereq)
            => new SkillDef
            {
                id = id, display = display, effect = effect, value = value, maxLevel = maxLevel,
                costMultiplier = costMult, grid = new Vector2Int(gx, gy),
                tooltip = tooltip, desc = desc, prereq = prereq
            };

        static readonly SkillDef[] Skills =
        {
            // ── 액티브 해금 ──────────────────────────────────────
            S("unlock_booster", "부스터", SkillEffectType.UnlockBooster, 1, 1, 3f, 0, -3,
              "우클릭을 눌러 빨리 달리고 경로의 적을 먹습니다.",
              "앞으로 짧게 대쉬합니다. 대쉬 경로에 있는 물고기는 크기와 상관없이 먹힙니다."),
            S("unlock_vacuum", "청소기", SkillEffectType.UnlockVacuum, 1, 1, 5f, 2, -3,
              "물고기를 빨아들입니다.",
              "좌클릭을 누르고 있는 동안 범위 내 소형 물고기를 끌어당깁니다."),
            S("unlock_armor", "비늘 경화", SkillEffectType.UnlockScaleArmor, 1, 1, 7f, 4, -3,
              "적의 공격을 1회 막습니다.",
              "먹힐 뻔한 순간을 1회 막고 짧게 무적이 됩니다."),
            S("unlock_bait", "황금 미끼", SkillEffectType.UnlockGoldenBait, 1, 1, 9f, 6, -3,
              "황금 미끼를 뿌립니다.",
              "일정 시간마다 자동으로 미끼를 뿌립니다. 주변 물고기가 미끼로 달려듭니다."),
            S("unlock_volt", "10만 볼트", SkillEffectType.UnlockVolt, 1, 1, 12f, 8, -3,
              "전기 쇼크를 일으킵니다.",
              "일정 시간마다 자동으로 주변에 전기 충격을 일으켜 마비시킵니다. (일반 2초 / 보스 0.1초)"),
            S("unlock_missile", "미사일", SkillEffectType.UnlockMissile, 1, 1, 16f, 10, -3,
              "미사일을 발사합니다.",
              "일정 시간마다 바라보는 방향으로 발사합니다. 맞은 적은 즉시 먹힌 것으로 처리됩니다."),

            // ── 액티브 강화 ──────────────────────────────────────
            S("booster_range", "부스터 거리 강화", SkillEffectType.BoosterRange, 0.30f, 5, 1f, 0, -4,
              "", "부스터 이동 거리 +30%.", R("unlock_booster", 1)),
            S("booster_power", "부스터 위력 강화", SkillEffectType.BoosterPower, 1, 1, 14f, 0, -5,
              "", "부스터를 사용할 때 자기보다 더 큰 물고기도 먹을 수 있습니다.", R("unlock_booster", 1)),
            S("vacuum_range", "청소기 범위 강화", SkillEffectType.VacuumRange, 0.50f, 5, 1f, 2, -4,
              "", "청소기 효과 범위 +50%.", R("unlock_vacuum", 1)),
            S("armor_stack", "비늘 경화 강화", SkillEffectType.ScaleArmorStack, 1, 2, 6f, 4, -4,
              "", "방어 횟수 +1 (최대 3회).", R("unlock_armor", 1)),
            S("bait_range", "황금 미끼 범위 강화", SkillEffectType.BaitRange, 0.50f, 4, 1f, 6, -4,
              "", "미끼 효과 범위 +50%.", R("unlock_bait", 1)),
            S("bait_count", "황금 미끼 추가", SkillEffectType.BaitCount, 1, 3, 5f, 6, -5,
              "", "미끼 개수 +1.", R("unlock_bait", 1)),
            S("volt_power", "10만 볼트 강화", SkillEffectType.VoltPower, 0.50f, 5, 1f, 8, -4,
              "", "전기 충격 범위와 마비 시간 +50%.", R("unlock_volt", 1)),
            S("missile_power", "미사일 발사 강화", SkillEffectType.MissilePower, 0.50f, 5, 3f, 10, -4,
              "", "미사일 범위 +50%, 개수 +1.", R("unlock_missile", 1)),

            // ── 기본 강화 ────────────────────────────────────────
            S("battery", "커다란 배터리", SkillEffectType.SurvivalTime, 1f, 20, 1f, 0, 1,
              "", "물고기 생존 제한시간 1초 증가."),
            S("teeth", "치아 교정", SkillEffectType.MouthPower, 0.10f, 8, 1.2f, 2, 0,
              "", "입 크기 및 흡입력 10% 증가."),
            S("camera", "카메라 장착", SkillEffectType.Vision, 0.10f, 8, 1f, 4, 0,
              "", "시야 10% 증가."),
            S("cell", "물고기 전지", SkillEffectType.TimeGain, 0.05f, 12, 1f, 2, 1,
              "", "물고기를 먹을 때 얻는 제한시간 5% 증가."),
            S("acid", "위액 산성도 증가", SkillEffectType.CurrencyGain, 0.10f, 20, 1.5f, 4, 1,
              "", "물고기를 먹을 때 얻는 재화량 10% 증가."),

            // ── 덧붙인 장갑 (진행의 축) ──────────────────────────
            S("armor_1", "덧붙인 장갑 I", SkillEffectType.BodyScale, 0.10f, 12, 3f, 0, 3,
              "", "몸 크기 및 이동속도 10% 증가."),
            S("armor_2", "덧붙인 장갑 II", SkillEffectType.BodyScale, 0.10f, 10, 5f, 3, 3,
              "", "몸 크기 및 이동속도 10% 증가.",
              R("battery", 8), R("teeth", 5)),
            S("armor_3", "덧붙인 장갑 III", SkillEffectType.BodyScale, 0.10f, 10, 8f, 6, 3,
              "", "몸 크기 및 이동속도 10% 증가.",
              R("acid", 8), R("cell", 5), R("unlock_vacuum", 1)),
            S("armor_4", "덧붙인 장갑 IV", SkillEffectType.BodyScale, 0.10f, 4, 12f, 9, 3,
              "", "몸 크기 및 이동속도 10% 증가.",
              R("camera", 5), R("teeth", 8), R("unlock_volt", 1), R("unlock_missile", 1)),
        };

        // ═════════════════════════════════════════════════════════
        [MenuItem("FishGame/1. 콘텐츠 에셋 생성", false, 1)]
        public static GameDatabase Generate()
        {
            PlaceholderArt.EnsureFolder(FishFolder);
            PlaceholderArt.EnsureFolder(ZoneFolder);
            PlaceholderArt.EnsureFolder(SkillFolder);
            PlaceholderArt.EnsureFolder(ResourcesFolder);

            if (AssetDatabase.LoadAssetAtPath<GameDatabase>(LegacyDatabasePath) != null &&
                AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath) == null)
            {
                string moveError = AssetDatabase.MoveAsset(LegacyDatabasePath, DatabasePath);
                if (!string.IsNullOrEmpty(moveError))
                    Debug.LogWarning($"[FishGame] GameDatabase 이동 실패: {moveError}");
            }

            DeleteObsoleteSkills();
            DeleteLegacyMaps();

            var fishByFile = new Dictionary<string, FishSpecies>();
            var allFish = new List<FishSpecies>();

            for (int m = 0; m < MapFish.Length; m++)
            {
                for (int i = 0; i < MapFish[m].Length; i++)
                {
                    var bonus = CodexByRank[Mathf.Min(i, CodexByRank.Length - 1)];
                    var asset = CreateFish(MapFish[m][i], false, bonus.type, bonus.value);
                    fishByFile[MapFish[m][i].file] = asset;
                    allFish.Add(asset);
                }
            }
            foreach (var def in Rares)
            {
                // 희귀 대형어 — 보스가 아니므로 잡아도 판이 끝나지 않는다.
                // 100마리를 먹을 일은 없으니 도감 보너스는 두지 않는다.
                var asset = CreateFish(def, false, CodexBonusType.None, 0f);
                fishByFile[def.file] = asset;
                allFish.Add(asset);
            }
            {
                var bossAsset = CreateFish(Boss, true, CodexBonusType.None, 0f);
                fishByFile[Boss.file] = bossAsset;
                allFish.Add(bossAsset);
            }

            // 스킬 — 선행 연결은 2패스
            var skillById = new Dictionary<string, SkillNode>();
            foreach (var def in Skills) skillById[def.id] = CreateSkill(def);
            foreach (var def in Skills)
            {
                var node = skillById[def.id];
                node.prerequisites.Clear();
                foreach (var r in def.prereq)
                {
                    if (!skillById.TryGetValue(r.id, out var pre)) continue;
                    node.prerequisites.Add(new SkillRequirement { node = pre, level = r.level });
                }
                EditorUtility.SetDirty(node);
            }

            var zones = new List<ZoneData>();
            for (int i = 0; i < Zones.Length; i++) zones.Add(CreateZone(i, fishByFile));

            var db = LoadOrCreate<GameDatabase>(DatabasePath);
            db.zones = zones;
            db.allFish = allFish;
            db.skills = new List<SkillNode>();
            foreach (var def in Skills) db.skills.Add(skillById[def.id]);

            // 기본 스탯 — balance_sim.py와 동일하게 유지할 것
            db.baseSurvivalTime = 30f;
            db.baseSize = 1f;
            db.baseMoveSpeed = 6.5f;
            db.baseMouthRatio = 0.45f;
            db.baseVision = 6.5f;
            db.speedScalingExponent = 0.5f;
            db.maxMoveSpeed = 24f;

            // 상한 — 스킬을 다 찍어도 게임이 깨지지 않게
            db.maxPlayerSize = 56f;          // 보스 43 < 상한 < 강→바다 통로 통과 한계 57.6
            db.maxMouthMultiplier = 2.6f;    // 치아 8레벨(×2.14) + 도감 보너스 여유
            db.voltStunCapRatio = 0.6f;      // 마비 ≤ 쿨타임의 60%
            db.activeRangeScalesWithSize = true;

            db.timeDrainAccelerationPer60s = 2.2f;
            db.maxTimeDrainMultiplier = 10f;
            db.deathCurrencyPenalty = 0.30f;

            // 통로는 '판 중에 커진 현재 크기'로 열린다 — 한 판의 목표가 되도록
            db.gateUsesCurrentSize = true;
            db.recordDeepestZone = true;
            db.hiddenItemCurrencyBonus = 0.05f;

            // 깊이 ↔ 가치 — 맵 전체에 걸친 연속 곡선.
            // 구역 배율은 구간 페이싱용이고, 매끄러운 상승은 이 곡선이 맡는다.
            db.globalValueScale = 1f;
            db.depthRichness = 3f;
            db.eatSizeTolerance = 1.0f;

            db.codexMilestone = 100;
            db.codexMaxTiers = 10;

            db.inRunGrowthEnabled = true;
            db.growthMassEfficiency = 0.15f;
            db.growthMaxMultiplier = 2.5f;

            EnsureSoundBank();

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int fishCount = MapFish.Length * 5 + Rares.Length + 1;   // 잡어 + 희귀 대형어 + 보스 1
            Debug.Log($"[FishGame] 콘텐츠 생성 완료 — 물고기 {fishCount}종, " +
                      $"구역 {Zones.Length}개(통합 맵), 스킬 {Skills.Length}개\n{DatabasePath}");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
            return AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
        }

        /// <summary>
        /// 효과음 뼈대를 만든다. 클립은 비어 있어도 게임이 정상 동작하며,
        /// 나중에 .wav를 인스펙터에 끌어다 넣기만 하면 소리가 난다.
        /// </summary>
        static void EnsureSoundBank()
        {
            const string path = ResourcesFolder + "/SoundBank.asset";
            if (AssetDatabase.LoadAssetAtPath<SoundBank>(path) != null) return;

            var bank = ScriptableObject.CreateInstance<SoundBank>();
            AssetDatabase.CreateAsset(bank, path);
            Debug.Log($"[FishGame] SoundBank 생성 — 클립은 비어 있습니다. " +
                      $"{path} 에 .wav를 넣으면 소리가 납니다.");
        }

        /// <summary>
        /// 맵 4개 시절의 MapData 에셋 폴더를 통째로 지운다.
        /// 남겨두면 GameDatabase가 끊긴 참조를 들고 있게 되고, 인스펙터에서 헷갈린다.
        /// </summary>
        static void DeleteLegacyMaps()
        {
            if (!AssetDatabase.IsValidFolder(LegacyMapFolder)) return;
            AssetDatabase.DeleteAsset(LegacyMapFolder);
            Debug.Log("[FishGame] 구버전 맵 에셋 폴더를 삭제했습니다 (맵 4개 → 통합 맵 1개).");
        }

        /// <summary>기획서 1판의 옛 스킬 에셋을 지운다 (id가 바뀌어 더는 쓰이지 않는다).</summary>
        static void DeleteObsoleteSkills()
        {
            var keep = new HashSet<string>();
            foreach (var d in Skills) keep.Add($"Skill_{d.id}");

            foreach (var guid in AssetDatabase.FindAssets("t:SkillNode", new[] { SkillFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!keep.Contains(name))
                {
                    AssetDatabase.DeleteAsset(path);
                    Debug.Log($"[FishGame] 구버전 스킬 삭제: {name}");
                }
            }

            // 옛 물고기 에셋도 정리
            var keepFish = new HashSet<string>();
            foreach (var arr in MapFish) foreach (var d in arr) keepFish.Add(d.file);
            foreach (var d in Rares) keepFish.Add(d.file);
            keepFish.Add(Boss.file);

            foreach (var guid in AssetDatabase.FindAssets("t:FishSpecies", new[] { FishFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!keepFish.Contains(name)) AssetDatabase.DeleteAsset(path);
            }
        }

        static FishSpecies CreateFish(FishDef def, bool isBoss,
                                     CodexBonusType codexType = CodexBonusType.Currency,
                                     float codexValue = 0.02f)
        {
            var asset = LoadOrCreate<FishSpecies>($"{FishFolder}/{def.file}.asset");
            asset.speciesId = def.file;
            asset.displayName = def.display;
            asset.size = def.size;
            asset.visualScaleMultiplier = 1f;
            asset.pattern = def.pattern;
            asset.moveSpeed = def.speed;
            asset.patternParam = def.param;
            asset.patternParam2 = def.param2;
            asset.turnRate = def.turn;
            asset.swayAmplitude = def.sway;
            asset.swayFrequency = def.swayHz;
            asset.currencyReward = def.money;
            asset.timeReward = def.time;
            asset.isBoss = isBoss;
            asset.tint = Color.white;
            asset.vacuumableSizeRatio = isBoss ? 0f : 0.6f;
            asset.codexBonusType = codexType;
            asset.codexBonusPerTier = codexValue;

            if (asset.sprite == null)
                asset.sprite = PlaceholderArt.CreateFishSprite(def.file, def.color, isBoss ? 0.86f : 1f);

            EditorUtility.SetDirty(asset);
            return asset;
        }

        static SkillNode CreateSkill(SkillDef def)
        {
            var asset = LoadOrCreate<SkillNode>($"{SkillFolder}/Skill_{def.id}.asset");
            asset.id = def.id;
            asset.displayName = def.display;
            asset.tooltip = def.tooltip;
            asset.description = def.desc;
            asset.effectType = def.effect;
            asset.valuePerLevel = def.value;
            asset.maxLevel = def.maxLevel;
            asset.costMultiplier = def.costMultiplier;
            asset.gridPosition = def.grid;
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static ZoneData CreateZone(int index, Dictionary<string, FishSpecies> fish)
        {
            var def = Zones[index];
            var asset = LoadOrCreate<ZoneData>($"{ZoneFolder}/{def.file}.asset");

            asset.zoneIndex = index;
            asset.displayName = def.display;
            asset.description = def.desc;
            asset.waterColor = def.water;

            // ── 실루엣 ──
            asset.height = def.height;
            asset.widthProfile = new List<WidthPoint>();
            foreach (var (t, hw) in def.profile) asset.widthProfile.Add(new WidthPoint(t, hw));
            asset.outlineSegments = 32;

            // ── 통로 ──
            asset.hasExit = def.hasExit;
            asset.exitHalfWidth = def.exitHalfWidth;
            asset.exitHeight = def.exitHeight;
            asset.exitRequiredSize = def.exitRequiredSize;
            asset.exitOffsetX = 0f;

            // ── 스폰 ──
            float maxHalf = 1f;
            foreach (var (_, hw) in def.profile) maxHalf = Mathf.Max(maxHalf, hw);
            asset.targetPopulation = def.population;
            asset.spawnInterval = 0.28f;
            asset.spawnMarginFromPlayer = Mathf.Max(9f, maxHalf * 0.32f);

            // ── 가치 ──
            asset.currencyMultiplier = def.currencyMult;

            asset.hiddenItemCount = HiddenPerZone;

            // ── 장애물 ──
            asset.obstacles = new List<ZoneObstacle>();
            if (def.obstacles != null)
            {
                foreach (var (shape, t, side, size, rot) in def.obstacles)
                {
                    asset.obstacles.Add(new ZoneObstacle
                    {
                        shape = shape,
                        depthT = t,
                        sideRatio = side,
                        size = size,
                        rotation = rot,
                    });
                }
            }

            // ── 보스 — 최하단(바다)에만 ──
            bool isLast = index == Zones.Length - 1;
            asset.boss = isLast ? fish[Boss.file] : null;
            asset.bossFromStructure = isLast;
            asset.bossStructureRadius = isLast ? 26f : 5f;
            // 존 천장에서 아래로 이만큼 내려간 곳에 구조물을 둔다
            asset.bossStructureLocalPos = new Vector2(0f, def.height * 0.46f);

            asset.spawnTable = new List<SpawnEntry>();
            for (int i = 0; i < MapFish[index].Length; i++)
            {
                asset.spawnTable.Add(new SpawnEntry
                {
                    species = fish[MapFish[index][i].file],
                    weight = SpawnWeights[i],
                    maxAlive = i >= 3 ? 5 : 0,
                });
            }

            // 희귀 대형어 — 어항·하수구·강에만, 한 마리씩 아주 드물게
            if (index < Rares.Length)
            {
                asset.spawnTable.Add(new SpawnEntry
                {
                    species = fish[Rares[index].file],
                    weight = 2.5f,
                    maxAlive = 1,
                });
            }

            EditorUtility.SetDirty(asset);
            return asset;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
#endif
