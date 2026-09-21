using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishGame.Data
{
    /// <summary>존에서 특정 물고기가 등장하는 비중/개체수 설정.</summary>
    [Serializable]
    public class SpawnEntry
    {
        public FishSpecies species;
        [Tooltip("가중치. 클수록 자주 뽑힌다.")]
        [Min(0f)] public float weight = 1f;
        [Tooltip("이 종이 필드에 동시에 존재할 수 있는 최대 마리 수 (0 = 무제한)")]
        [Min(0)] public int maxAlive = 0;
    }

    /// <summary>장애물 모양. 기획 PPT의 각 구역 그림에 대응한다.</summary>
    public enum ObstacleShape
    {
        /// <summary>네모 — 어항 장식품(성·집)</summary>
        Box,
        /// <summary>원 — 하수구의 쇠창살 원형</summary>
        Disc,
        /// <summary>삼각형 — 강의 자갈 무더기, 바다의 암초</summary>
        Triangle,
    }

    /// <summary>
    /// 구역 안에 놓이는 충돌 장애물.
    ///
    /// 위치를 절대 좌표가 아니라 "구역 안 깊이 + 그 깊이 반폭 대비 비율"로 잡는다.
    /// 구역의 높이나 폭을 고쳐도 장애물이 벽을 뚫고 나가지 않게 하려는 것이다.
    /// </summary>
    [Serializable]
    public class ZoneObstacle
    {
        public ObstacleShape shape = ObstacleShape.Box;
        [Tooltip("구역 안 깊이. 0 = 천장, 1 = 바닥")]
        [Range(0f, 1f)] public float depthT = 0.5f;
        [Tooltip("그 깊이의 반폭 대비 좌우 위치. -1 = 왼쪽 벽, 0 = 중앙, 1 = 오른쪽 벽")]
        [Range(-1f, 1f)] public float sideRatio = 0f;
        [Tooltip("한 변 또는 지름 (월드 유닛)")]
        [Min(0.5f)] public float size = 8f;
        [Tooltip("회전 (도)")]
        public float rotation = 0f;
    }

    /// <summary>
    /// 존의 좌우 폭을 정의하는 제어점.
    /// t = 0 이 존의 천장, t = 1 이 존의 바닥이다.
    /// </summary>
    [Serializable]
    public class WidthPoint
    {
        [Range(0f, 1f)] public float t;
        [Tooltip("중심선에서 벽까지의 거리 (월드 유닛)")]
        [Min(0.5f)] public float halfWidth = 20f;

        public WidthPoint() { }
        public WidthPoint(float t, float halfWidth) { this.t = t; this.halfWidth = halfWidth; }
    }

    /// <summary>
    /// 세로로 이어 붙인 통합 맵의 한 구간.
    ///
    /// 기존 MapData(맵 4개가 서로 분리)를 대체한다. 이제 존 4개가 위에서 아래로
    /// 하나의 연속된 물속 공간을 이루고, 존 바닥의 좁은 통로로 다음 존에 내려간다.
    ///
    ///   어항 (사발)  →  하수구 (상자)  →  강 (렌즈)  →  바다 (개활)
    ///
    /// 통로는 "판 중에 먹어서 커진 현재 크기"가 exitRequiredSize 이상일 때 열린다.
    /// 즉 한 판의 목표는 '이번엔 어디까지 내려가느냐'가 된다.
    /// </summary>
    [CreateAssetMenu(fileName = "Zone_", menuName = "FishGame/Zone Data", order = 1)]
    public class ZoneData : ScriptableObject
    {
        [Header("식별")]
        [Tooltip("0 = 어항(가장 위). 아래로 갈수록 커진다.")]
        [Min(0)] public int zoneIndex = 0;
        public string displayName = "어항";
        [TextArea(2, 4)] public string description = "";

        // ══════════════════════════════════════════════════════════
        //  형상 — PPT의 실루엣을 폭 프로파일로 표현한다
        // ══════════════════════════════════════════════════════════
        [Header("형상")]
        [Tooltip("이 존의 세로 길이 (월드 유닛)")]
        [Min(10f)] public float height = 60f;

        [Tooltip("위(t=0)에서 아래(t=1)로 가면서 반폭이 어떻게 변하는지.\n" +
                 "어항은 넓다가 배수구로 좁아지고, 강은 가운데가 불룩한 렌즈 모양이다.")]
        public List<WidthPoint> widthProfile = new List<WidthPoint>
        {
            new WidthPoint(0f, 22f),
            new WidthPoint(1f, 22f),
        };

        [Tooltip("벽 실루엣을 만들 때 세로로 몇 등분할지. 클수록 곡선이 매끈하다.")]
        [Range(4, 96)] public int outlineSegments = 28;

        // ══════════════════════════════════════════════════════════
        //  아래로 내려가는 통로
        // ══════════════════════════════════════════════════════════
        [Header("통로 (아래 존으로)")]
        [Tooltip("최하단 존(바다)은 체크 해제한다.")]
        public bool hasExit = true;
        [Tooltip("통로의 반폭. 플레이어가 통과해야 하므로 너무 좁으면 안 된다.")]
        [Min(1f)] public float exitHalfWidth = 4f;
        [Tooltip("통로의 세로 길이")]
        [Min(2f)] public float exitHeight = 14f;
        [Tooltip("통로 중심의 가로 위치 (0 = 존 중앙)")]
        public float exitOffsetX = 0f;

        [Tooltip("통로가 열리는 데 필요한 크기.\n" +
                 "스킬트리로 올린 기본 크기가 아니라 '판 중에 먹어서 커진 현재 크기'로 판정한다.")]
        [Min(0.1f)] public float exitRequiredSize = 3.1f;

        // ══════════════════════════════════════════════════════════
        //  연출
        // ══════════════════════════════════════════════════════════
        [Header("연출")]
        public Color waterColor = new Color(0.55f, 0.80f, 0.72f, 1f);
        public Sprite background;
        [Tooltip("벽 색. 구조물은 배경과 확실히 구분되도록 테두리를 어둡게 한다.")]
        public Color wallColor = new Color(0.10f, 0.11f, 0.14f, 1f);

        // ══════════════════════════════════════════════════════════
        //  스폰
        // ══════════════════════════════════════════════════════════
        [Header("스폰")]
        public List<SpawnEntry> spawnTable = new List<SpawnEntry>();
        [Tooltip("이 존에 유지할 물고기 마리 수")]
        [Min(1)] public int targetPopulation = 26;
        [Min(0.02f)] public float spawnInterval = 0.35f;
        [Tooltip("화면 밖 이 거리 이상 떨어진 곳에서 스폰")]
        [Min(0f)] public float spawnMarginFromPlayer = 8f;

        // ══════════════════════════════════════════════════════════
        //  가치
        // ══════════════════════════════════════════════════════════
        [Header("가치")]
        [Tooltip("이 구역만 따로 손볼 때 쓰는 보조 배율. 기본 1.\n\n" +
                 "깊이에 따른 가치 상승은 GameDatabase.depthRichness 가 맵 전체에 걸쳐\n" +
                 "연속적으로 계산하므로, 여기는 특정 구역이 유독 후하거나 박할 때만 건드린다.")]
        [Min(0.001f)] public float currencyMultiplier = 1f;

        // ══════════════════════════════════════════════════════════
        //  히든 아이템 — 먹으면 영구 재화 +5%, 한 번 먹으면 다시 안 나온다
        // ══════════════════════════════════════════════════════════
        // ══════════════════════════════════════════════════════════
        //  장애물 — 헤엄쳐 지나갈 수 없는 구조물
        // ══════════════════════════════════════════════════════════
        [Header("장애물")]
        [Tooltip("구역 안에 놓이는 충돌 구조물. 통로 근처에 놓이면 길이 막히므로\n" +
                 "WorldBuilder가 자동으로 안쪽으로 밀어 넣고, 출구를 막는 것은 건너뛴다.")]
        public List<ZoneObstacle> obstacles = new List<ZoneObstacle>();

        [Header("히든 아이템")]
        [Min(0)] public int hiddenItemCount = 2;

        // ══════════════════════════════════════════════════════════
        //  보스 — 바다에만 있다
        // ══════════════════════════════════════════════════════════
        [Header("보스 (최하단 존만)")]
        [Tooltip("이 존에 보스를 둘 경우 지정. 나머지는 비워둔다.")]
        public FishSpecies boss;
        [Tooltip("체크하면 구조물에 부스터로 돌진해야 보스가 풀려난다.")]
        public bool bossFromStructure = false;
        [Tooltip("보스 구조물의 위치 (존 안의 로컬 좌표, y는 천장 기준 아래로 +)")]
        public Vector2 bossStructureLocalPos = new Vector2(0f, 30f);
        [Min(1f)] public float bossStructureRadius = 5f;

        // ══════════════════════════════════════════════════════════
        //  폭 계산
        // ══════════════════════════════════════════════════════════
        /// <summary>존 안의 정규화 깊이 t(0=천장, 1=바닥)에서의 반폭.</summary>
        public float HalfWidthAt(float t)
        {
            if (widthProfile == null || widthProfile.Count == 0) return 20f;
            if (widthProfile.Count == 1) return widthProfile[0].halfWidth;

            t = Mathf.Clamp01(t);

            // 제어점은 t 오름차순이라고 가정한다 (생성기가 그렇게 만든다).
            for (int i = 0; i < widthProfile.Count - 1; i++)
            {
                var a = widthProfile[i];
                var b = widthProfile[i + 1];
                if (t < a.t || t > b.t) continue;

                float span = b.t - a.t;
                if (span <= 0.0001f) return b.halfWidth;

                // 선형이 아니라 부드럽게 이어야 사발·렌즈 모양이 자연스럽다.
                float k = Mathf.SmoothStep(0f, 1f, (t - a.t) / span);
                return Mathf.Lerp(a.halfWidth, b.halfWidth, k);
            }

            return t <= widthProfile[0].t
                ? widthProfile[0].halfWidth
                : widthProfile[widthProfile.Count - 1].halfWidth;
        }

        /// <summary>이 존에서 가장 넓은 지점의 반폭. 카메라 한계·스폰 범위에 쓴다.</summary>
        public float MaxHalfWidth
        {
            get
            {
                float max = 1f;
                if (widthProfile != null)
                    foreach (var p in widthProfile)
                        if (p != null && p.halfWidth > max) max = p.halfWidth;
                return max;
            }
        }

        void OnValidate()
        {
            if (widthProfile != null && widthProfile.Count > 0)
            {
                widthProfile.Sort((a, b) => a.t.CompareTo(b.t));
                if (exitHalfWidth > HalfWidthAt(1f))
                    exitHalfWidth = Mathf.Max(1f, HalfWidthAt(1f));
            }
            if (!hasExit) exitRequiredSize = 0f;
        }
    }
}
