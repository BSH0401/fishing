using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishGame.Data
{
    /// <summary>
    /// 스킬트리 도면 위의 칸 하나.
    ///
    /// 트리는 기획 도면(draw.io "물고기게임 스킬트리")을 그대로 옮긴 그래프다.
    ///   · 도형 하나 = 칸 하나 = 한 번만 찍는다.
    ///   · 도형 종류가 곧 강화 종류다 (□ 배터리, ○ 장갑, ◇ 치아 …). 같은 종류를 k칸 찍으면 그 강화가 k레벨.
    ///   · 선으로 이어진 이웃 칸 중 하나라도 찍혀 있어야 열린다. 시작 칸은 처음부터 찍혀 있다.
    /// </summary>
    [Serializable]
    public class SkillTreeSlot
    {
        [Tooltip("세이브 키. 한번 정하면 바꾸지 말 것.")]
        public string id;

        [Tooltip("이 칸을 찍으면 오르는 강화. 시작 칸은 비어 있다. (시작 칸이 아닌데 비어 있으면 그 칸은 무시된다)")]
        public SkillNode skill;

        [Tooltip("도면 좌표 (시작 칸 = 0,0 / 위쪽이 +y). UI에서 배율을 곱해 배치한다.")]
        public Vector2 position;

        [Tooltip("선으로 이어진 칸들의 인덱스 (GameDatabase.skillTree 기준, 양방향으로 들어 있다)")]
        public List<int> links = new List<int>();

        [Tooltip("시작 칸. 처음부터 찍혀 있는 것으로 본다.")]
        public bool isRoot;

        public bool IsRoot => isRoot;
    }
}
