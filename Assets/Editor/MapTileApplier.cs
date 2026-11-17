using FishGame.Data;
using UnityEditor;
using UnityEngine;

namespace FishGame.EditorTools
{
    /// <summary>
    /// 동심원 맵 바닥 타일을 구역에 연결한다 (R14). 구글 드라이브 FishGame/이미지의 64px 물 타일.
    ///
    ///   Art/Custom/Tiles/Tile_Zone1_Tank.png    → 1 어항   (밝은 하늘색, 빛줄기)
    ///   Art/Custom/Tiles/Tile_Zone2_Sewer.png   → 2 하수구 (어두운 초록, 이끼)
    ///   Art/Custom/Tiles/Tile_Zone3_River.png   → 3 강     (청록, 물결)
    ///   Art/Custom/Tiles/Tile_Zone4_Ocean.png   → 4 바다   (짙은 파랑, 파도)
    ///
    /// 비어 있는 칸만 컴파일 직후 자동으로 채운다. 메뉴(FishGame ▸ 9. 맵 타일 적용)는 이 표대로 다시 맞춘다.
    /// 타일을 바꾸려면 같은 이름으로 그림만 덮어쓰면 된다.
    /// </summary>
    [InitializeOnLoad]
    public static class MapTileApplier
    {
        public const string Folder = "Assets/_Project/Art/Custom/Tiles";
        static readonly string[] Files = { "Tile_Zone1_Tank", "Tile_Zone2_Sewer", "Tile_Zone3_River", "Tile_Zone4_Ocean" };

        static MapTileApplier()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                int n = Apply(overwrite: false);
                if (n > 0) Debug.Log($"[FishGame] 맵 바닥 타일 {n}곳 연결 (비어 있던 칸만)");
            };
        }

        [MenuItem("FishGame/9. 맵 타일 적용", false, 9)]
        static void ApplyMenu() => Debug.Log($"[FishGame] 맵 바닥 타일 적용 — {Apply(overwrite: true)}곳 변경");

        public static int Apply(bool overwrite)
        {
            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:ZoneData"))
            {
                var zone = AssetDatabase.LoadAssetAtPath<ZoneData>(AssetDatabase.GUIDToAssetPath(guid));
                if (zone == null || zone.zoneIndex < 0 || zone.zoneIndex >= Files.Length) continue;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/{Files[zone.zoneIndex]}.png");
                if (tex == null) continue;
                if (zone.floorTile != null && !(overwrite && zone.floorTile != tex)) continue;
                zone.floorTile = tex;
                EditorUtility.SetDirty(zone);
                changed++;
            }
            if (changed > 0) AssetDatabase.SaveAssets();
            return changed;
        }
    }

    /// <summary>바닥 타일 임포트 — 반복(Repeat) · 또렷한 도트(Point) · 압축 없음.</summary>
    public class MapTileImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(MapTileApplier.Folder + "/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
        }
    }
}
