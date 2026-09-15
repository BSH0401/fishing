#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FishGame.EditorTools
{
    /// <summary>
    /// 아트 없이도 바로 플레이할 수 있도록 물고기 실루엣 PNG를 절차적으로 만든다.
    /// 나중에 진짜 아트로 교체할 때는 같은 경로의 파일만 덮어쓰면 된다.
    /// </summary>
    public static class PlaceholderArt
    {
        public const string ArtFolder = "Assets/_Project/Art/Generated";
        const int Width = 128;
        const int Height = 64;
        const float PixelsPerUnit = 128f;

        /// <summary>물고기 스프라이트 1장을 만들어 저장하고 Sprite를 돌려준다.</summary>
        public static Sprite CreateFishSprite(string fileName, Color color, float bodyRoundness = 1f)
        {
            EnsureFolder(ArtFolder);
            string path = $"{ArtFolder}/{fileName}.png";

            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var pixels = new Color32[Width * Height];

            // 몸통: 타원 / 꼬리: 삼각형 / 눈: 점
            Vector2 bodyCenter = new Vector2(78f, 32f);
            float rx = 44f * bodyRoundness;
            float ry = 24f;

            Vector2 eye = new Vector2(104f, 38f);

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    Color c = new Color(0, 0, 0, 0);

                    float nx = (x - bodyCenter.x) / rx;
                    float ny = (y - bodyCenter.y) / ry;
                    float body = nx * nx + ny * ny;

                    // 꼬리: x 4~40 사이의 삼각형
                    bool tail = false;
                    if (x >= 4f && x <= 42f)
                    {
                        float t = Mathf.InverseLerp(42f, 4f, x);           // 0(몸통쪽) → 1(끝)
                        float halfHeight = Mathf.Lerp(6f, 22f, t);
                        tail = Mathf.Abs(y - 32f) <= halfHeight;
                    }

                    if (body <= 1f)
                    {
                        // 위쪽을 살짝 어둡게 해서 입체감
                        float shade = Mathf.Lerp(1.12f, 0.82f, Mathf.InverseLerp(0f, Height, y));
                        c = new Color(color.r * shade, color.g * shade, color.b * shade, 1f);

                        // 외곽선
                        if (body > 0.86f)
                            c = Color.Lerp(c, new Color(0f, 0f, 0f, 1f), 0.35f);
                    }
                    else if (tail)
                    {
                        c = new Color(color.r * 0.82f, color.g * 0.82f, color.b * 0.82f, 1f);
                    }

                    if (c.a > 0f && Vector2.Distance(new Vector2(x, y), eye) < 4.2f)
                        c = new Color(0.08f, 0.08f, 0.1f, 1f);

                    pixels[i] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ApplySpriteImportSettings(path);

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>원형 스프라이트 (흡입/전기 범위 표시, 미끼용).</summary>
        public static Sprite CreateCircleSprite(string fileName, Color color, int size = 64)
        {
            EnsureFolder(ArtFolder);
            string path = $"{ArtFolder}/{fileName}.png";

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            float r = size * 0.5f - 1f;
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    // 가장자리 1.5px를 부드럽게 — 계단 현상 방지
                    float a = Mathf.Clamp01((r - d) / 1.5f);
                    pixels[y * size + x] = new Color(color.r, color.g, color.b, color.a * a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ApplySpriteImportSettings(path, size);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>가장자리만 진한 비네트. 제한시간 위급 연출에 쓴다.</summary>
        public static Sprite CreateVignetteSprite(string fileName, int size = 256)
        {
            EnsureFolder(ArtFolder);
            string path = $"{ArtFolder}/{fileName}.png";

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float maxDist = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / maxDist;
                    // 중앙 55%는 완전 투명, 바깥으로 갈수록 급격히 진해진다
                    float a = Mathf.Clamp01((d - 0.55f) / 0.45f);
                    a = a * a;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ApplySpriteImportSettings(path, size);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>단색 사각 스프라이트 (배경, UI 라인, 게이트 표시용).</summary>
        public static Sprite CreateSolidSprite(string fileName, Color color, int size = 16)
        {
            EnsureFolder(ArtFolder);
            string path = $"{ArtFolder}/{fileName}.png";

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            tex.SetPixels32(pixels);
            tex.Apply();

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ApplySpriteImportSettings(path, size);

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void ApplySpriteImportSettings(string path, float ppu = PixelsPerUnit)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
