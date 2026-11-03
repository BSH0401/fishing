using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FishGame.EditorTools
{
    /// <summary>
    /// Resources/UI 의 도트 UI를 9-slice 스프라이트로 임포트한다 (Point 필터 · 압축 없음).
    /// 테두리(9-slice 경계)는 그림마다 장식이 늘어나지 않는 폭으로 정해 둔다 — 새 그림을 넣으면 여기에 추가.
    /// </summary>
    public class UiSpriteImporter : AssetPostprocessor
    {
        // 왼쪽, 아래, 오른쪽, 위 (픽셀)
        static readonly Dictionary<string, Vector4> Borders = new Dictionary<string, Vector4>
        {
            { "FishButton", new Vector4(6, 5, 6, 5) },
            { "FishIndex",  new Vector4(26, 26, 26, 26) },
            { "SquareUI",   new Vector4(38, 40, 70, 22) },   // 왼쪽 위 배터리 · 오른쪽 위 계기판 · 오른쪽 아래 플라스크
            { "BlueLine",   Vector4.zero },
            { "OrangeLine", Vector4.zero },
        };

        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/UI/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.spritePixelsPerUnit = 100f;
            if (Borders.TryGetValue(Path.GetFileNameWithoutExtension(assetPath), out var b))
                importer.spriteBorder = b;
        }
    }
}
