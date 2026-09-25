using System.Collections.Generic;
using FishGame.Data;
using FishGame.Gameplay;
using UnityEditor;
using UnityEngine;

namespace FishGame.EditorTools
{
    /// <summary>
    /// 직접 그린 그림(Art/Custom, Resources/Icons)을 게임에 꽂는다.
    ///
    ///   물고기 7색   → 구역마다 5종이 서로 다른 색이 되게 20종에 나눠 준다 (희귀종 · 보스는 기존 그림 유지)
    ///   실험체 #7    → 플레이어
    ///   로켓         → 미사일 탄 (오른쪽을 보게 돌린 FX_Missile_Rocket)
    ///   황금 미끼    → 미끼 오브젝트
    ///   아이템 아이콘 → HUD 스킬 칸 (HUDController가 Resources/Icons에서 실행 중에 읽는다)
    ///
    /// 프리팹 · 콘텐츠를 다시 생성해도 이 메뉴만 다시 누르면 된다 (★ 전체 셋업은 마지막에 자동으로 부른다).
    /// </summary>
    public static class ArtApplier
    {
        const string ArtFolder = "Assets/_Project/Art/Custom";
        const string IconFolder = "Assets/_Project/Resources/Icons";

        // 그림 속 몸통이 스프라이트 폭의 몇 %인지 — 예전 임시 그림(128px 중 몸+꼬리 ≈ 118px)과 같은 크기로 보이게
        const float FishWorldWidth = 0.95f;

        /// <summary>종 파일 이름 → 색 그림. 한 구역 안에서는 색이 겹치지 않는다.</summary>
        static readonly Dictionary<string, string> FishArt = new Dictionary<string, string>
        {
            // 1 어항
            { "Fish_M1_Fry",      "Fish_SkyBlue" },
            { "Fish_M1_Minnow",   "Fish_Yellow"  },
            { "Fish_M1_Guppy",    "Fish_Orange"  },
            { "Fish_M1_Angel",    "Fish_Cyan"    },
            { "Fish_M1_Keeper",   "Fish_Pink"    },
            // 2 하수구
            { "Fish_M2_Larva",    "Fish_Lime"    },
            { "Fish_M2_Sludge",   "Fish_SkyBlue" },
            { "Fish_M2_Pipe",     "Fish_Orange"  },
            { "Fish_M2_Rat",      "Fish_Pink"    },
            { "Fish_M2_Grate",    "Fish_Brown"   },
            // 3 강
            { "Fish_M3_Sweet",    "Fish_Cyan"    },
            { "Fish_M3_Crucian",  "Fish_Yellow"  },
            { "Fish_M3_Carp",     "Fish_Orange"  },
            { "Fish_M3_Cat",      "Fish_Brown"   },
            { "Fish_M3_Snake",    "Fish_Lime"    },
            // 4 바다
            { "Fish_M4_Sardine",  "Fish_SkyBlue" },
            { "Fish_M4_Mackerel", "Fish_Cyan"    },
            { "Fish_M4_Tuna",     "Fish_Yellow"  },
            { "Fish_M4_Shark",    "Fish_Brown"   },
            { "Fish_M4_Orca",     "Fish_Pink"    },
        };

        [MenuItem("FishGame/6. 직접 그린 아트 적용", false, 6)]
        public static void ApplyMenu()
        {
            int n = Apply();
            EditorUtility.DisplayDialog("FishGame", $"아트를 적용했습니다 — {n}곳 변경.", "확인");
        }

        public static int Apply()
        {
            int changed = 0;

            // ── 임포트 설정 (월드 크기에 맞는 PPU) ──
            foreach (var name in new[] { "Fish_Lime", "Fish_Brown", "Fish_Orange", "Fish_Cyan",
                                         "Fish_Yellow", "Fish_Pink", "Fish_SkyBlue", "Player_Subject7" })
                Import($"{ArtFolder}/{name}.png", FishWorldWidth);
            Import($"{ArtFolder}/FX_Missile_Rocket.png", 1.2f);            // 미사일 길이 ≈ 예전 탄(1.1)
            foreach (var icon in new[] { "Item_Missile", "Item_Booster", "Item_ScaleArmor", "Item_GoldenBait", "Item_Volt" })
                Import($"{IconFolder}/{icon}.png", 1f);

            // ── 물고기 종 ──
            foreach (var guid in AssetDatabase.FindAssets("t:FishSpecies"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var sp = AssetDatabase.LoadAssetAtPath<FishSpecies>(path);
                if (sp == null || !FishArt.TryGetValue(sp.name, out var art)) continue;
                var sprite = Load($"{ArtFolder}/{art}.png");
                if (sprite == null || sp.sprite == sprite) continue;
                sp.sprite = sprite;
                sp.spriteFacesLeft = false;
                sp.tint = Color.white;
                EditorUtility.SetDirty(sp);
                changed++;
            }

            // ── 프리팹 ──
            changed += EditPrefab($"{PrefabGenerator.PrefabFolder}/PlayerFish.prefab", root =>
            {
                var sr = FindBodyRenderer(root);
                var sprite = Load($"{ArtFolder}/Player_Subject7.png");
                if (sr == null || sprite == null || sr.sprite == sprite) return false;
                sr.sprite = sprite;
                sr.color = Color.white;
                return true;
            });

            changed += EditPrefab($"{PrefabGenerator.PrefabFolder}/Missile.prefab", root =>
            {
                var sr = root.GetComponent<SpriteRenderer>();
                var sprite = Load($"{ArtFolder}/FX_Missile_Rocket.png");
                if (sr == null || sprite == null) return false;
                bool dirty = sr.sprite != sprite || root.transform.localScale != Vector3.one;
                sr.sprite = sprite;
                sr.color = Color.white;
                root.transform.localScale = Vector3.one;   // 예전 네모 탄은 가로로 늘려 썼다 — 그림은 비율 그대로
                return dirty;
            });

            changed += EditPrefab($"{PrefabGenerator.PrefabFolder}/Bait.prefab", root =>
            {
                var sr = root.GetComponent<SpriteRenderer>();
                var sprite = Load($"{IconFolder}/Item_GoldenBait.png");
                if (sr == null || sprite == null) return false;
                bool dirty = sr.sprite != sprite;
                sr.sprite = sprite;
                sr.color = Color.white;
                // 낚시 미끼 그림이라 빙글빙글 돌면 어색하다 — 위아래로만 흔들리게
                var bait = root.GetComponent<Bait>();
                if (bait != null)
                {
                    var so = new SerializedObject(bait);
                    var spin = so.FindProperty("spinSpeed");
                    if (spin != null && !Mathf.Approximately(spin.floatValue, 0f)) { spin.floatValue = 0f; dirty = true; }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                return dirty;
            });

            AssetDatabase.SaveAssets();
            Debug.Log($"[FishGame] 직접 그린 아트 적용 — {changed}곳 변경");
            return changed;
        }

        // ── 헬퍼 ────────────────────────────────────────────────
        /// <summary>스프라이트로 임포트하고, 그림 폭이 worldWidth 유닛이 되게 PPU를 맞춘다.</summary>
        static void Import(string path, float worldWidth)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { Debug.LogWarning($"[ArtApplier] 그림이 없습니다: {path}"); return; }

            importer.GetSourceTextureWidthAndHeight(out int w, out int h);
            float ppu = Mathf.Max(1f, w / Mathf.Max(0.01f, worldWidth));

            bool changed = importer.textureType != TextureImporterType.Sprite ||
                           importer.spriteImportMode != SpriteImportMode.Single ||
                           !Mathf.Approximately(importer.spritePixelsPerUnit, ppu) ||
                           importer.mipmapEnabled || !importer.alphaIsTransparency ||
                           importer.textureCompression != TextureImporterCompression.Uncompressed;
            if (!changed) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;   // 작은 그림이라 압축하면 뭉개진다
            importer.SaveAndReimport();
        }

        static Sprite Load(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        static int EditPrefab(string path, System.Func<GameObject, bool> edit)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning($"[ArtApplier] 프리팹이 없습니다: {path} — [FishGame ▸ 2. 프리팹 생성] 뒤에 다시 실행하세요.");
                return 0;
            }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!edit(root)) return 0;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return 1;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>플레이어 몸 그림 — 범위 표시(원) 같은 자식 말고 FishBody가 쓰는 렌더러.</summary>
        static SpriteRenderer FindBodyRenderer(GameObject root)
        {
            var body = root.GetComponent<FishBody>();
            if (body != null && body.Renderer != null) return body.Renderer;
            return root.GetComponent<SpriteRenderer>();
        }
    }
}
