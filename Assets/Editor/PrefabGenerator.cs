#if UNITY_EDITOR
using FishGame.Gameplay;
using FishGame.Player;
using FishGame.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FishGame.EditorTools
{
    /// <summary>
    /// 플레이어 / AI 물고기 / UI 프리팹을 코드로 만든다.
    /// 메뉴: FishGame ▸ 2. 프리팹 생성
    /// </summary>
    public static class PrefabGenerator
    {
        public const string PrefabFolder = "Assets/_Project/Prefabs";
        public const string UiPrefabFolder = PrefabFolder + "/UI";

        [MenuItem("FishGame/2. 프리팹 생성", false, 2)]
        public static void GenerateAll()
        {
            PlaceholderArt.EnsureFolder(PrefabFolder);
            PlaceholderArt.EnsureFolder(UiPrefabFolder);

            CreateAIFishPrefab();
            CreateBaitPrefab();
            CreateMissilePrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            CreatePlayerPrefab();
            CreateSkillNodePrefab();
            CreateFloatingTextPrefab();
            CreateMapButtonPrefab();
            CreateLinePrefab();
            CreateCodexRowPrefab();
            CreateLineLabelPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FishGame] 프리팹 생성 완료 → " + PrefabFolder);
        }

        // ── 게임플레이 프리팹 ───────────────────────────────────
        public static GameObject CreateAIFishPrefab()
        {
            var go = new GameObject("AIFish");
            go.layer = Mathf.Max(0, ProjectSetupUtility.EnsureLayer(ProjectSetupUtility.FishLayerName));

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 5;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.42f;
            col.isTrigger = true;

            go.AddComponent<FishBody>();
            go.AddComponent<FishMotor>();
            go.AddComponent<AIFish>();

            return SaveAndDestroy(go, $"{PrefabFolder}/AIFish.prefab");
        }

        public static GameObject CreatePlayerPrefab()
        {
            var go = new GameObject("PlayerFish");
            go.layer = Mathf.Max(0, ProjectSetupUtility.EnsureLayer(ProjectSetupUtility.PlayerLayerName));

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 10;
            sr.sprite = PlaceholderArt.CreateFishSprite("Fish_Player", new Color(0.98f, 0.80f, 0.35f));

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.42f;
            col.isTrigger = true;

            go.AddComponent<FishBody>();
            go.AddComponent<FishMotor>();
            var player = go.AddComponent<PlayerFish>();

            var vacuum = CircleIndicator(go.transform, "VacuumIndicator",
                                         new Color(0.55f, 0.9f, 1f, 0.14f), -2);
            var volt   = CircleIndicator(go.transform, "VoltIndicator",
                                         new Color(1f, 0.95f, 0.4f, 0.30f), -1);
            var armor  = CircleIndicator(go.transform, "ArmorIndicator",
                                         new Color(0.6f, 0.85f, 1f, 0.28f), 9);
            armor.transform.localScale = Vector3.one * 1.35f;
            armor.gameObject.SetActive(true);

            var eatFx = MakeBurst(go.transform, "EatEffect",
                                  new Color(1f, 0.95f, 0.75f), 14, 0.45f, 3.2f);
            var boostFx = MakeBurst(go.transform, "BoosterEffect",
                                    new Color(0.6f, 0.9f, 1f), 20, 0.55f, 2.2f);

            var so = new SerializedObject(player);
            so.FindProperty("eatEffect").objectReferenceValue = eatFx;
            so.FindProperty("boosterEffect").objectReferenceValue = boostFx;
            so.FindProperty("fishLayer").intValue =
                1 << ProjectSetupUtility.EnsureLayer(ProjectSetupUtility.FishLayerName);
            so.FindProperty("vacuumIndicator").objectReferenceValue = vacuum.transform;
            so.FindProperty("voltIndicator").objectReferenceValue = volt.transform;
            so.FindProperty("armorIndicator").objectReferenceValue = armor;
            so.FindProperty("baitPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/Bait.prefab")?.GetComponent<Bait>();
            so.FindProperty("missilePrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/Missile.prefab")?.GetComponent<Missile>();
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveAndDestroy(go, $"{PrefabFolder}/PlayerFish.prefab");
        }

        /// <summary>한 번에 터지는 작은 파티클. 포식·부스터 연출용.</summary>
        static ParticleSystem MakeBurst(Transform parent, string name, Color color,
                                        int count, float lifetime, float speed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = lifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.30f);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;
            // 히트스톱(timeScale 0) 중에도 터지는 게 보이게
            main.useUnscaledTime = true;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.35f;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.sortingOrder = 15;

            return ps;
        }

        static SpriteRenderer CircleIndicator(Transform parent, string name, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderArt.CreateCircleSprite("UI_Circle", Color.white, 64);
            sr.color = color;
            sr.sortingOrder = order;
            go.SetActive(false);
            return sr;
        }

        public static GameObject CreateBaitPrefab()
        {
            var go = new GameObject("Bait");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderArt.CreateCircleSprite("FX_Bait", new Color(1f, 0.85f, 0.30f), 48);
            sr.sortingOrder = 8;
            go.transform.localScale = Vector3.one * 0.8f;

            var bait = go.AddComponent<Bait>();
            var so = new SerializedObject(bait);
            so.FindProperty("spriteRenderer").objectReferenceValue = sr;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(false);
            return SaveAndDestroy(go, $"{PrefabFolder}/Bait.prefab");
        }

        public static GameObject CreateMissilePrefab()
        {
            var go = new GameObject("Missile");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderArt.CreateSolidSprite("FX_Missile", new Color(1f, 0.55f, 0.35f), 8);
            sr.sortingOrder = 12;
            go.transform.localScale = new Vector3(1.1f, 0.32f, 1f);

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.22f;
            trail.startWidth = 0.28f;
            trail.endWidth = 0.02f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.startColor = new Color(1f, 0.7f, 0.4f, 0.7f);
            trail.endColor = new Color(1f, 0.5f, 0.2f, 0f);
            trail.sortingOrder = 11;

            var missile = go.AddComponent<Missile>();
            var so = new SerializedObject(missile);
            so.FindProperty("spriteRenderer").objectReferenceValue = sr;
            so.FindProperty("trail").objectReferenceValue = trail;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.SetActive(false);
            return SaveAndDestroy(go, $"{PrefabFolder}/Missile.prefab");
        }

        // ── UI 프리팹 ───────────────────────────────────────────
        public static GameObject CreateSkillNodePrefab()
        {
            var go = NewUI("SkillNodeButton", new Vector2(104f, 104f));
            var rt = (RectTransform)go.transform;

            // 살 수 있을 때 맥동하는 글로우 (테두리 바깥, 클릭 방해 안 되게 raycast 끔)
            var glowGo = NewUI("Glow", new Vector2(128f, 128f), rt);
            var glow = glowGo.AddComponent<Image>();
            glow.sprite = PlaceholderArt.CreateCircleSprite("UI_Circle", Color.white, 64);
            glow.color = new Color(0.45f, 1f, 0.65f, 0.3f);
            glow.raycastTarget = false;
            glowGo.SetActive(false);

            var frame = go.AddComponent<Image>();
            frame.color = new Color(0.35f, 0.78f, 0.45f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = frame;

            // 레벨 진행도 — 아래에서 위로 차오른다
            var fillGo = NewUI("LevelFill", Vector2.zero, rt);
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(3f, 3f); fillRt.offsetMax = new Vector2(-3f, -3f);
            var fill = fillGo.AddComponent<Image>();
            fill.color = new Color(0.3f, 0.6f, 0.4f, 0.55f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Vertical;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;
            fill.raycastTarget = false;
            fill.sprite = PlaceholderArt.CreateSolidSprite("UI_White", Color.white, 8);

            var iconGo = NewUI("Icon", new Vector2(52f, 52f), rt);
            ((RectTransform)iconGo.transform).anchoredPosition = new Vector2(0f, 12f);
            var icon = iconGo.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.enabled = false;

            var nameGo = NewUI("Name", new Vector2(126f, 34f), rt);
            ((RectTransform)nameGo.transform).anchoredPosition = new Vector2(0f, -24f);
            var nameText = AddText(nameGo, "스킬", 13, TextAlignmentOptions.Center);

            var levelGo = NewUI("Level", new Vector2(104f, 20f), rt);
            ((RectTransform)levelGo.transform).anchoredPosition = new Vector2(0f, -58f);
            var level = AddText(levelGo, "0/10", 14, TextAlignmentOptions.Center);
            level.color = new Color(0.80f, 0.85f, 0.90f);

            var costGo = NewUI("Cost", new Vector2(104f, 20f), rt);
            ((RectTransform)costGo.transform).anchoredPosition = new Vector2(0f, -76f);
            var cost = AddText(costGo, "10", 14, TextAlignmentOptions.Center);
            cost.color = new Color(0.98f, 0.88f, 0.45f);

            var lockGo = NewUI("Lock", new Vector2(26f, 26f), rt);
            ((RectTransform)lockGo.transform).anchoredPosition = new Vector2(36f, 36f);
            var lockImg = lockGo.AddComponent<Image>();
            lockImg.color = new Color(0.08f, 0.09f, 0.11f, 0.9f);
            lockImg.raycastTarget = false;

            var node = go.AddComponent<SkillNodeButton>();
            var so = new SerializedObject(node);
            so.FindProperty("button").objectReferenceValue = button;
            so.FindProperty("iconImage").objectReferenceValue = icon;
            so.FindProperty("frameImage").objectReferenceValue = frame;
            so.FindProperty("fillImage").objectReferenceValue = fill;
            so.FindProperty("glowImage").objectReferenceValue = glow;
            so.FindProperty("nameText").objectReferenceValue = nameText;
            so.FindProperty("levelText").objectReferenceValue = level;
            so.FindProperty("costText").objectReferenceValue = cost;
            so.FindProperty("lockIcon").objectReferenceValue = lockGo;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveAndDestroy(go, $"{UiPrefabFolder}/SkillNodeButton.prefab");
        }

        /// <summary>
        /// 선 위에 붙는 "3/8" 라벨.
        /// SkillTreeUI가 Instantiate(TMP_Text)로 복제하므로 루트에 텍스트가 있어야 한다.
        /// </summary>
        public static GameObject CreateLineLabelPrefab()
        {
            var go = NewUI("LineLabel", new Vector2(60f, 22f));
            var text = AddText(go, "3/8", 14, TextAlignmentOptions.Center);
            text.color = new Color(0.95f, 0.78f, 0.32f);
            text.fontStyle = FontStyles.Bold;
            return SaveAndDestroy(go, $"{UiPrefabFolder}/LineLabel.prefab");
        }

        public static GameObject CreateFloatingTextPrefab()
        {
            var go = NewUI("FloatingText", new Vector2(220f, 34f));
            var text = AddText(go, "+1", 22, TextAlignmentOptions.Center);
            text.color = new Color(0.98f, 0.92f, 0.55f);
            text.fontStyle = FontStyles.Bold;
            return SaveAndDestroy(go, $"{UiPrefabFolder}/FloatingText.prefab");
        }

        public static GameObject CreateMapButtonPrefab()
        {
            var go = NewUI("MapButton", new Vector2(300f, 56f));
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 52f;
            le.minHeight = 52f;

            var labelGo = NewUI("Label", new Vector2(290f, 50f), go.transform);
            var label = AddText(labelGo, "1. 얕은 실험 수조", 20, TextAlignmentOptions.Center);
            label.color = new Color(0.12f, 0.14f, 0.16f);

            return SaveAndDestroy(go, $"{UiPrefabFolder}/MapButton.prefab");
        }

        public static GameObject CreateCodexRowPrefab()
        {
            var go = NewUI("CodexRow", new Vector2(560f, 58f));
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.14f, 0.18f, 0.22f, 0.9f);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 58f;
            le.minHeight = 58f;

            var rt = (RectTransform)go.transform;

            var iconGo = NewUI("Icon", new Vector2(44f, 44f), rt);
            ((RectTransform)iconGo.transform).anchoredPosition = new Vector2(-244f, 0f);
            var icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true;

            var nameGo = NewUI("Name", new Vector2(150f, 26f), rt);
            ((RectTransform)nameGo.transform).anchoredPosition = new Vector2(-130f, 10f);
            var nameText = AddText(nameGo, "물고기", 19, TextAlignmentOptions.Left);

            var countGo = NewUI("Count", new Vector2(150f, 22f), rt);
            ((RectTransform)countGo.transform).anchoredPosition = new Vector2(-130f, -13f);
            var countText = AddText(countGo, "0", 15, TextAlignmentOptions.Left);
            countText.color = new Color(0.72f, 0.78f, 0.84f);

            var bonusGo = NewUI("Bonus", new Vector2(240f, 26f), rt);
            ((RectTransform)bonusGo.transform).anchoredPosition = new Vector2(130f, 10f);
            var bonusText = AddText(bonusGo, "", 16, TextAlignmentOptions.Right);
            bonusText.color = new Color(0.42f, 0.86f, 0.72f);

            // 진행 바
            var barBg = NewUI("BarBg", new Vector2(240f, 8f), rt);
            ((RectTransform)barBg.transform).anchoredPosition = new Vector2(130f, -14f);
            var barBgImg = barBg.AddComponent<Image>();
            barBgImg.color = new Color(0f, 0f, 0f, 0.45f);

            var fillGo = NewUI("Fill", Vector2.zero, (RectTransform)barBg.transform);
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
            var fill = fillGo.AddComponent<Image>();
            fill.color = new Color(0.36f, 0.80f, 0.68f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.sprite = PlaceholderArt.CreateSolidSprite("UI_White", Color.white, 8);

            var row = go.AddComponent<FishGame.UI.CodexRow>();
            var so = new SerializedObject(row);
            so.FindProperty("icon").objectReferenceValue = icon;
            so.FindProperty("nameText").objectReferenceValue = nameText;
            so.FindProperty("countText").objectReferenceValue = countText;
            so.FindProperty("bonusText").objectReferenceValue = bonusText;
            so.FindProperty("progressFill").objectReferenceValue = fill;
            so.FindProperty("background").objectReferenceValue = bg;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveAndDestroy(go, $"{UiPrefabFolder}/CodexRow.prefab");
        }

        public static GameObject CreateLinePrefab()
        {
            var go = NewUI("SkillLine", new Vector2(100f, 5f));
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.2f);
            img.raycastTarget = false;
            return SaveAndDestroy(go, $"{UiPrefabFolder}/SkillLine.prefab");
        }

        // ── 헬퍼 ────────────────────────────────────────────────
        public static GameObject NewUI(string name, Vector2 size, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            // 코드로 만든 RectTransform의 기본 앵커는 좌하단이라 배치가 어긋난다. 중앙으로 통일.
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            return go;
        }

        public static TMP_Text AddText(GameObject go, string content, float fontSize,
                                       TextAlignmentOptions align)
        {
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = align;
            text.color = Color.white;
            text.raycastTarget = false;

            // 한글 폰트를 명시적으로 물린다. 없으면 TMP 기본 폰트(한글 미포함)로 남는다.
            FontSetup.Apply(text);

            if (FontSetup.Font == null && TMP_Settings.defaultFontAsset == null)
                Debug.LogWarning("[FishGame] TMP Essentials가 없습니다. " +
                                 "Window ▸ TextMeshPro ▸ Import TMP Essential Resources 를 먼저 실행하세요.");
            return text;
        }

        static GameObject SaveAndDestroy(GameObject go, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
#endif
