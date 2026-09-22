#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FishGame.EditorTools
{
    /// <summary>레이어 추가, 물리 설정 등 프로젝트 전역 셋업.</summary>
    public static class ProjectSetupUtility
    {
        public const string FishLayerName = "Fish";
        public const string PlayerLayerName = "Player";
        /// <summary>벽·통로 게이트·장애물이 쓰는 레이어. WorldBuilder/ZoneGate/Obstacle이 이름으로 찾는다.</summary>
        public const string WallLayerName = "Wall";

        /// <summary>레이어를 추가하고 인덱스를 돌려준다. 이미 있으면 그 인덱스.</summary>
        public static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) return existing;

            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");

            // 0~7은 유니티 예약. 8부터 빈 칸을 찾는다.
            for (int i = 8; i < layers.arraySize; i++)
            {
                var element = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(element.stringValue))
                {
                    element.stringValue = layerName;
                    tagManager.ApplyModifiedProperties();
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[FishGame] 레이어 '{layerName}' 를 슬롯 {i}에 추가했습니다.");
                    return i;
                }
            }

            Debug.LogError($"[FishGame] 빈 레이어 슬롯이 없어 '{layerName}' 를 추가하지 못했습니다. " +
                           "Project Settings ▸ Tags and Layers 에서 직접 비워주세요.");
            return -1;
        }

        [MenuItem("FishGame/0. 프로젝트 셋업 (레이어 · 물리)", false, 0)]
        public static void SetupProject()
        {
            int fish = EnsureLayer(FishLayerName);
            int player = EnsureLayer(PlayerLayerName);
            // 예전엔 이 레이어를 만들지 않아서 벽·게이트·장애물이 전부 Default에 있었다
            int wall = EnsureLayer(WallLayerName);

            // 2D 중력 제거 — 물속이라 중력이 필요 없다
            Physics2D.gravity = Vector2.zero;

            // 물고기끼리는 물리 충돌하지 않게 (트리거 판정만 사용)
            if (fish >= 0) Physics2D.IgnoreLayerCollision(fish, fish, true);
            if (fish >= 0 && player >= 0) Physics2D.IgnoreLayerCollision(fish, player, true);
            // AI 물고기는 조향으로 벽을 피한다. 거대한 벽 콜라이더와 트리거 판정을 매 프레임 할 이유가 없다.
            if (fish >= 0 && wall >= 0) Physics2D.IgnoreLayerCollision(fish, wall, true);
            // 플레이어 ↔ 벽은 반드시 충돌해야 한다 (닫힌 통로·장애물이 실제로 막아야 하므로)
            if (player >= 0 && wall >= 0) Physics2D.IgnoreLayerCollision(player, wall, false);

            // 물리 설정은 ProjectSettings 에셋이라 명시적으로 저장해야 재시작 후에도 남는다
            AssetDatabase.SaveAssets();

            Debug.Log("[FishGame] 프로젝트 셋업 완료 — 레이어(Fish/Player/Wall), 2D 중력 0, 충돌 매트릭스");
        }
    }
}
#endif
