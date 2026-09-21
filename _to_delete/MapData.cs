using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishGame.Data
{
    /// <summary>맵에서 특정 물고기가 등장하는 비중/개체수 설정.</summary>
    [Serializable]
    public class SpawnEntry
    {
        public FishSpecies species;
        [Tooltip("가중치. 클수록 자주 뽑힌다.")]
        [Min(0f)] public float weight = 1f;
        [Tooltip("이 종이 필드에 동시에 존재할 수 있는 최대 마리 수 (0 = 무제한)")]
        [Min(0)] public int maxAlive = 0;
    }

    /// <summary>
    /// 맵 1개의 정의. 기획서 기준 총 4개.
    /// 맵이 올라갈수록 물고기 종류가 바뀌고 커지며, 보스를 잡으면 다음 맵이 해금된다.
    /// </summary>
    [CreateAssetMenu(fileName = "Map_", menuName = "FishGame/Map Data", order = 1)]
    public class MapData : ScriptableObject
    {
        [Header("식별")]
        [Tooltip("0부터 시작. 세이브의 해금 인덱스와 대응된다.")]
        [Min(0)] public int mapIndex = 0;
        public string displayName = "얕은 실험 수조";
        [TextArea(2, 4)] public string description = "";

        [Header("배경 / 영역")]
        public Sprite background;
        public Color waterColor = new Color(0.55f, 0.80f, 0.72f, 1f);
        [Tooltip("플레이어와 물고기가 돌아다닐 수 있는 사각 영역 (월드 좌표, 중심 0,0 기준)")]
        public Vector2 boundsSize = new Vector2(40f, 22f);

        [Header("스폰")]
        public List<SpawnEntry> spawnTable = new List<SpawnEntry>();
        [Tooltip("필드에 유지할 물고기 총 마리 수")]
        [Min(1)] public int targetPopulation = 25;
        [Tooltip("한 번에 스폰하는 간격(초)")]
        [Min(0.02f)] public float spawnInterval = 0.35f;
        [Tooltip("화면 밖 이 거리 이상 떨어진 곳에서 스폰")]
        [Min(0f)] public float spawnMarginFromPlayer = 8f;

        [Header("보스")]
        [Tooltip("이 맵의 다음 맵으로 가는 길을 막는 보스. 없으면(최종 맵) 비워둔다.")]
        public FishSpecies boss;
        [Tooltip("보스가 등장하기 위해 플레이어가 갖춰야 할 최소 크기 (미만이면 보스 게이트가 잠김)")]
        [Min(0f)] public float bossGateMinSize = 6f;
        [Tooltip("보스 게이트 위치 (맵 좌표)")]
        public Vector2 bossGatePosition = new Vector2(16f, 0f);

        [Header("진행")]
        [Tooltip("이 맵을 해금하기 위해 클리어해야 하는 이전 맵의 인덱스. mapIndex==0이면 무시된다.")]
        [Min(0)] public int requiresClearOfMapIndex = 0;
        [Tooltip("이 맵에서 얻는 재화에 곱해지는 배율. 상위 맵일수록 크게.")]
        [Min(0.01f)] public float currencyMultiplier = 1f;

        public Rect WorldBounds =>
            new Rect(-boundsSize.x * 0.5f, -boundsSize.y * 0.5f, boundsSize.x, boundsSize.y);
    }
}
