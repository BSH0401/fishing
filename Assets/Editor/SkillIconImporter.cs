using UnityEditor;
using UnityEngine;

namespace FishGame.EditorTools
{
    /// <summary>
    /// Resources/SkillIcons 폴더에 넣은 그림을 자동으로 픽셀아트 스프라이트로 임포트한다.
    /// (Point 필터 · 압축 없음 · 밉맵 없음 — 작은 도트 그림을 칸 크기로 키워도 번지지 않게)
    /// </summary>
    public class SkillIconImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/SkillIcons/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 100f;
        }
    }
}
