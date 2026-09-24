#if UNITY_EDITOR
using FishGame.Core;
using FishGame.Data;
using FishGame.Gameplay;
using FishGame.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FishGame.EditorTools
{
    /// <summary>
    /// MainMenu / Gameplay 씬을 코드로 조립한다.
    /// 메뉴: FishGame ▸ 3. 씬 생성
    /// </summary>
    public static class SceneGenerator
    {
        const string SceneFolder = "Assets/_Project/Scenes";
        const string SoFolder = "Assets/_Project/ScriptableObjects";

        static readonly Color Ink = new Color(0.93f, 0.95f, 0.96f);
        static readonly Color Panel = new Color(0.10f, 0.13f, 0.17f, 0.92f);
        static readonly Color Accent = new Color(0.36f, 0.80f, 0.68f);

        /// <summary>
        /// GameDatabase를 디스크에서 새로 읽는다.
        ///
        /// NewScene(Single)은 어떤 씬도 참조하지 않는 에셋을 메모리에서 내린다.
        /// 그래서 씬을 만들기 전에 읽어 둔 참조를 씬을 만든 뒤에 대입하면, 이미 죽은 참조가 들어가
        /// {fileID: 0}으로 저장됐다 — GameManager·RunManager의 Database가 항상 비어 있던 원인.
        /// 반드시 NewScene 뒤에 이걸로 다시 읽어서 대입한다.
        /// </summary>
        static GameDatabase LoadDatabase() =>
            AssetDatabase.LoadAssetAtPath<GameDatabase>(ContentGenerator.DatabasePath)
            ?? AssetDatabase.LoadAssetAtPath<GameDatabase>($"{SoFolder}/GameDatabase.asset");

        /// <summary>대입이 실제로 들어갔는지 확인한다. 조용히 비는 걸 다시는 놓치지 않으려고.</summary>
        static void VerifyReference(SerializedObject so, string prop, string owner)
        {
            so.Update();
            var p = so.FindProperty(prop);
            if (p == null || p.objectReferenceValue == null)
                Debug.LogError($"[FishGame] {owner}.{prop} 연결에 실패했습니다. 씬을 저장한 뒤 " +
                               "[FishGame ▸ 4. 열린 씬의 Database 참조 복구]를 실행하세요.");
        }

        [MenuItem("FishGame/3. 씬 생성", false, 3)]
        public static void GenerateScenes()
        {
            // 사용자가 열어 둔 씬에 저장 안 한 변경이 있으면 먼저 물어본다 — 새 씬을 만들면 날아간다
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            PlaceholderArt.EnsureFolder(SceneFolder);

            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(ContentGenerator.DatabasePath)
                     ?? AssetDatabase.LoadAssetAtPath<GameDatabase>($"{SoFolder}/GameDatabase.asset");
            if (db == null)
            {
                EditorUtility.DisplayDialog("FishGame",
                    "GameDatabase가 없습니다.\n먼저 [FishGame ▸ 1. 콘텐츠 에셋 생성]을 실행하세요.", "확인");
                return;
            }

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{PrefabGenerator.PrefabFolder}/PlayerFish.prefab");
            var fishPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{PrefabGenerator.PrefabFolder}/AIFish.prefab");
            if (playerPrefab == null || fishPrefab == null)
            {
                EditorUtility.DisplayDialog("FishGame",
                    "프리팹이 없습니다.\n먼저 [FishGame ▸ 2. 프리팹 생성]을 실행하세요.", "확인");
                return;
            }

            BuildGameplayScene(db, playerPrefab, fishPrefab);
            BuildMainMenuScene(db);
            RegisterScenesInBuildSettings();

            EditorSceneManager.OpenScene($"{SceneFolder}/MainMenu.unity");
            Debug.Log("[FishGame] 씬 생성 완료 — MainMenu / Gameplay");
        }

        // ═════════════════════════════════════════════════════════
        //  Gameplay
        // ═════════════════════════════════════════════════════════
        static void BuildGameplayScene(GameDatabase db, GameObject playerPrefab, GameObject fishPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 카메라 ──
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6.5f;   // 런타임엔 CameraFollow가 크기에 맞춰 조정
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.22f, 0.28f);
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            camGo.AddComponent<CameraFollow>();
            camGo.AddComponent<AudioListener>();

            // ── 월드 ──
            // 배경 스프라이트와 보스 게이트 표시는 없앴다.
            // 통합 맵의 물·벽·통로·구조물은 전부 WorldBuilder가 런타임에 만든다.
            var worldGo = new GameObject("World");
            var worldBuilder = worldGo.AddComponent<WorldBuilder>();
            var worldSo = new SerializedObject(worldBuilder);
            var fontProp = worldSo.FindProperty("labelFont");
            if (fontProp != null) fontProp.objectReferenceValue = FontSetup.Font;
            worldSo.ApplyModifiedPropertiesWithoutUndo();

            // ── 스포너 ──
            var spawnerGo = new GameObject("FishSpawner");
            var spawner = spawnerGo.AddComponent<FishSpawner>();
            var spawnerSo = new SerializedObject(spawner);
            spawnerSo.FindProperty("fishPrefab").objectReferenceValue =
                fishPrefab.GetComponent<AIFish>();
            spawnerSo.FindProperty("prewarmCount").intValue = 48;
            spawnerSo.ApplyModifiedPropertiesWithoutUndo();

            // ── 플레이어 ──
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.name = "PlayerFish";
            player.transform.position = Vector3.zero;

            // ── UI ──
            var canvas = CreateCanvas("HUD Canvas", out var canvasRt);
            CreateEventSystem();

            // 위급 비네트 — 다른 HUD보다 뒤에 깔리도록 가장 먼저 만든다
            var vignetteGo = PrefabGenerator.NewUI("DangerVignette", Vector2.zero, canvasRt);
            Stretch((RectTransform)vignetteGo.transform);
            var vignette = vignetteGo.AddComponent<Image>();
            vignette.sprite = PlaceholderArt.CreateVignetteSprite("UI_Vignette", 256);
            vignette.color = new Color(0.95f, 0.15f, 0.12f, 0f);
            vignette.raycastTarget = false;
            vignetteGo.SetActive(false);

            var hudGo = new GameObject("HUDController");
            hudGo.transform.SetParent(canvas.transform, false);
            var hud = hudGo.AddComponent<HUDController>();

            // 좌상단 정보 패널
            var infoPanel = Panel_(canvasRt, "InfoPanel", new Vector2(0f, 1f), new Vector2(0f, 1f),
                                   new Vector2(20f, -20f), new Vector2(320f, 192f));
            var timeText     = Label(infoPanel, "TimeText",     "제한시간 30.0", 26, new Vector2(14f, -14f), 292f, TextAlignmentOptions.Left);
            var timeBarBg    = Bar(infoPanel, "TimeBar", new Vector2(14f, -48f), 292f, out var timeBar);
            var currencyText = Label(infoPanel, "CurrencyText", "재화 0",        22, new Vector2(14f, -70f), 292f, TextAlignmentOptions.Left);
            var sizeText     = Label(infoPanel, "SizeText",     "크기 1.0",      20, new Vector2(14f, -98f), 140f, TextAlignmentOptions.Left);
            var drainText    = Label(infoPanel, "DrainText",    "소모 x1.0",     20, new Vector2(160f, -98f), 146f, TextAlignmentOptions.Right);
            drainText.color = new Color(0.98f, 0.55f, 0.45f);
            var growthBarBg  = Bar(infoPanel, "GrowthBar", new Vector2(14f, -122f), 292f, out var growthBar);
            growthBar.color = new Color(0.98f, 0.72f, 0.32f);
            var mapNameText  = Label(infoPanel, "ZoneNameText", "어항",          18, new Vector2(14f, -138f), 160f, TextAlignmentOptions.Left);
            mapNameText.color = new Color(0.75f, 0.82f, 0.86f);
            var depthText    = Label(infoPanel, "DepthText",    "깊이 0m",       18, new Vector2(160f, -138f), 146f, TextAlignmentOptions.Right);
            depthText.color = new Color(0.62f, 0.78f, 0.92f);
            var depthBarBg   = Bar(infoPanel, "DepthBar", new Vector2(14f, -160f), 292f, out var depthBar);
            depthBar.color = new Color(0.40f, 0.66f, 0.92f);

            // 하단 스킬 슬롯
            var skillRow = PrefabGenerator.NewUI("SkillRow", new Vector2(560f, 84f), canvasRt);
            var skillRowRt = (RectTransform)skillRow.transform;
            skillRowRt.anchorMin = skillRowRt.anchorMax = new Vector2(0.5f, 0f);
            skillRowRt.pivot = new Vector2(0.5f, 0f);
            skillRowRt.anchoredPosition = new Vector2(0f, 24f);
            var layout = skillRow.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var boosterSlot = SkillSlot(skillRowRt, "BoosterSlot", "부스터\n우클릭", out var boosterFill);
            var vacuumSlot  = SkillSlot(skillRowRt, "VacuumSlot",  "청소기\n좌클릭", out var vacuumFill);
            var baitSlot    = SkillSlot(skillRowRt, "BaitSlot",    "미끼\n자동",    out var baitFill);
            var voltSlot    = SkillSlot(skillRowRt, "VoltSlot",    "볼트\n자동",    out var voltFill);
            var missileSlot = SkillSlot(skillRowRt, "MissileSlot", "미사일\n자동",  out var missileFill);

            // 비늘 경화 표시
            var armorGroup = Panel_(canvasRt, "ArmorGroup", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                    new Vector2(0f, 116f), new Vector2(180f, 38f));
            ((RectTransform)armorGroup.transform).pivot = new Vector2(0.5f, 0f);
            var armorText = Label((RectTransform)armorGroup.transform, "Text", "비늘 1/1", 20,
                                  new Vector2(0f, -6f), 170f, TextAlignmentOptions.Center);
            armorText.color = new Color(0.66f, 0.86f, 1f);

            // 보스 안내
            var bossHint = Label(canvasRt, "GateHint", "아래 통로: 크기 1.0 / 3.1", 20,
                                 Vector2.zero, 700f, TextAlignmentOptions.Center);
            var bossHintRt = (RectTransform)bossHint.transform;
            bossHintRt.anchorMin = bossHintRt.anchorMax = new Vector2(0.5f, 1f);
            bossHintRt.pivot = new Vector2(0.5f, 1f);
            bossHintRt.anchoredPosition = new Vector2(0f, -24f);
            bossHint.color = new Color(0.98f, 0.82f, 0.45f);

            var bossBanner = Panel_(canvasRt, "BossBanner", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                    Vector2.zero, new Vector2(560f, 110f));
            ((RectTransform)bossBanner.transform).pivot = new Vector2(0.5f, 0.5f);
            var bannerText = Label((RectTransform)bossBanner.transform, "Text", "보스 등장!", 44,
                                   new Vector2(0f, -32f), 540f, TextAlignmentOptions.Center);
            bannerText.color = new Color(0.98f, 0.45f, 0.42f);
            bossBanner.SetActive(false);

            // 떠오르는 획득 텍스트
            var floatRoot = PrefabGenerator.NewUI("FloatingTextRoot", Vector2.zero, canvasRt);
            Stretch((RectTransform)floatRoot.transform);
            var floating = floatRoot.AddComponent<FloatingTextSpawner>();
            var floatSo = new SerializedObject(floating);
            floatSo.FindProperty("prefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabGenerator.UiPrefabFolder}/FloatingText.prefab")
                    ?.GetComponent<TMP_Text>();
            floatSo.FindProperty("canvasRoot").objectReferenceValue = floatRoot.transform;
            floatSo.ApplyModifiedPropertiesWithoutUndo();

            // 결과 패널
            var resultRoot = Panel_(canvasRt, "ResultPanel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                    Vector2.zero, new Vector2(620f, 480f));
            ((RectTransform)resultRoot.transform).pivot = new Vector2(0.5f, 0.5f);
            var rRt = (RectTransform)resultRoot.transform;
            var titleT   = Label(rRt, "Title",    "플레이 종료", 44, new Vector2(0f, -32f), 580f, TextAlignmentOptions.Center);
            var reasonT  = Label(rRt, "Reason",   "",            20, new Vector2(0f, -92f), 560f, TextAlignmentOptions.Center);
            var moneyT   = Label(rRt, "Currency", "0",           60, new Vector2(0f, -140f), 580f, TextAlignmentOptions.Center);
            moneyT.color = new Color(0.98f, 0.86f, 0.42f);
            var penaltyT = Label(rRt, "Penalty",  "",            20, new Vector2(0f, -212f), 580f, TextAlignmentOptions.Center);
            penaltyT.color = new Color(0.95f, 0.48f, 0.45f);
            var statsT   = Label(rRt, "Stats",    "",            22, new Vector2(0f, -248f), 580f, TextAlignmentOptions.Center);
            statsT.rectTransform.sizeDelta = new Vector2(580f, 70f);

            var unlockBanner = Panel_(rRt, "UnlockBanner", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                      new Vector2(0f, -324f), new Vector2(520f, 46f));
            ((RectTransform)unlockBanner.transform).pivot = new Vector2(0.5f, 1f);
            unlockBanner.GetComponent<Image>().color = new Color(0.20f, 0.45f, 0.35f, 0.95f);
            var unlockT = Label((RectTransform)unlockBanner.transform, "Text", "새 맵 해금!", 22,
                                new Vector2(0f, -10f), 500f, TextAlignmentOptions.Center);

            var retryBtn = TextButton(rRt, "RetryButton", "다시 하기",
                                      new Vector2(-140f, -410f), new Vector2(240f, 54f));
            var menuBtn  = TextButton(rRt, "MainMenuButton", "메인으로",
                                      new Vector2(140f, -410f), new Vector2(240f, 54f));

            // 주의: 컴포넌트는 항상 활성인 Canvas에 붙인다.
            // 패널 자신에 붙이면 SetActive(false) 때문에 Start()가 실행되지 않아
            // OnRunEnded 구독이 누락되고 결과창이 영영 안 뜬다.
            var result = canvas.gameObject.AddComponent<ResultPanel>();
            var resSo = new SerializedObject(result);
            resSo.FindProperty("root").objectReferenceValue = resultRoot;
            resSo.FindProperty("titleText").objectReferenceValue = titleT;
            resSo.FindProperty("reasonText").objectReferenceValue = reasonT;
            resSo.FindProperty("currencyText").objectReferenceValue = moneyT;
            resSo.FindProperty("penaltyText").objectReferenceValue = penaltyT;
            resSo.FindProperty("statsText").objectReferenceValue = statsT;
            resSo.FindProperty("unlockBanner").objectReferenceValue = unlockBanner;
            resSo.FindProperty("unlockText").objectReferenceValue = unlockT;
            resSo.FindProperty("mainMenuButton").objectReferenceValue = menuBtn;
            resSo.FindProperty("retryButton").objectReferenceValue = retryBtn;
            resSo.ApplyModifiedPropertiesWithoutUndo();
            resultRoot.SetActive(false);

            // 일시정지
            var pauseRoot = Panel_(canvasRt, "PausePanel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                   Vector2.zero, new Vector2(420f, 260f));
            ((RectTransform)pauseRoot.transform).pivot = new Vector2(0.5f, 0.5f);
            Label((RectTransform)pauseRoot.transform, "Title", "일시정지", 36,
                  new Vector2(0f, -30f), 400f, TextAlignmentOptions.Center);
            var resumeBtn = TextButton((RectTransform)pauseRoot.transform, "ResumeButton", "계속하기",
                                       new Vector2(0f, -110f), new Vector2(280f, 52f));
            var giveUpBtn = TextButton((RectTransform)pauseRoot.transform, "GiveUpButton", "포기하고 나가기",
                                       new Vector2(0f, -175f), new Vector2(280f, 52f));
            var pause = canvas.gameObject.AddComponent<PauseMenu>();
            var pauseSo = new SerializedObject(pause);
            pauseSo.FindProperty("root").objectReferenceValue = pauseRoot;
            pauseSo.FindProperty("resumeButton").objectReferenceValue = resumeBtn;
            pauseSo.FindProperty("giveUpButton").objectReferenceValue = giveUpBtn;
            pauseSo.ApplyModifiedPropertiesWithoutUndo();
            pauseRoot.SetActive(false);

            // ── RunManager 배선 ──
            var runGo = new GameObject("RunManager");
            var run = runGo.AddComponent<RunManager>();
            var runSo = new SerializedObject(run);
            runSo.FindProperty("player").objectReferenceValue = player.GetComponent<FishGame.Player.PlayerFish>();
            runSo.FindProperty("spawner").objectReferenceValue = spawner;
            runSo.FindProperty("worldBuilder").objectReferenceValue = worldBuilder;
            runSo.FindProperty("worldCamera").objectReferenceValue = cam;
            runSo.FindProperty("fallbackDatabase").objectReferenceValue = LoadDatabase();
            runSo.FindProperty("fallbackStartZone").intValue = 0;
            runSo.ApplyModifiedPropertiesWithoutUndo();
            VerifyReference(runSo, "fallbackDatabase", "RunManager");

            // ── HUD 배선 ──
            var hudSo = new SerializedObject(hud);
            hudSo.FindProperty("timeText").objectReferenceValue = timeText;
            hudSo.FindProperty("currencyText").objectReferenceValue = currencyText;
            hudSo.FindProperty("sizeText").objectReferenceValue = sizeText;
            hudSo.FindProperty("mapNameText").objectReferenceValue = mapNameText;
            hudSo.FindProperty("depthText").objectReferenceValue = depthText;
            hudSo.FindProperty("depthBar").objectReferenceValue = depthBar;
            hudSo.FindProperty("drainText").objectReferenceValue = drainText;
            hudSo.FindProperty("timeBar").objectReferenceValue = timeBar;
            hudSo.FindProperty("growthBar").objectReferenceValue = growthBar;
            hudSo.FindProperty("boosterSlot").objectReferenceValue = boosterSlot;
            hudSo.FindProperty("boosterFill").objectReferenceValue = boosterFill;
            hudSo.FindProperty("vacuumSlot").objectReferenceValue = vacuumSlot;
            hudSo.FindProperty("vacuumFill").objectReferenceValue = vacuumFill;
            hudSo.FindProperty("baitSlot").objectReferenceValue = baitSlot;
            hudSo.FindProperty("baitFill").objectReferenceValue = baitFill;
            hudSo.FindProperty("voltSlot").objectReferenceValue = voltSlot;
            hudSo.FindProperty("voltFill").objectReferenceValue = voltFill;
            hudSo.FindProperty("missileSlot").objectReferenceValue = missileSlot;
            hudSo.FindProperty("missileFill").objectReferenceValue = missileFill;
            hudSo.FindProperty("dangerVignette").objectReferenceValue = vignette;
            hudSo.FindProperty("armorGroup").objectReferenceValue = armorGroup;
            hudSo.FindProperty("armorText").objectReferenceValue = armorText;
            hudSo.FindProperty("bossBanner").objectReferenceValue = bossBanner;
            hudSo.FindProperty("bossHintText").objectReferenceValue = bossHint;
            hudSo.FindProperty("floatingText").objectReferenceValue = floating;
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            _ = timeBarBg; _ = growthBarBg; _ = depthBarBg;
            EditorSceneManager.SaveScene(scene, $"{SceneFolder}/Gameplay.unity");
        }

        // ═════════════════════════════════════════════════════════
        //  MainMenu
        // ═════════════════════════════════════════════════════════
        static void BuildMainMenuScene(GameDatabase db)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.10f, 0.13f);
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            camGo.AddComponent<AudioListener>();

            // GameManager (DontDestroyOnLoad로 씬을 넘어 살아남는다)
            var gmGo = new GameObject("GameManager");
            var gm = gmGo.AddComponent<GameManager>();
            var gmSo = new SerializedObject(gm);
            gmSo.FindProperty("database").objectReferenceValue = LoadDatabase();
            gmSo.FindProperty("mainMenuScene").stringValue = "MainMenu";
            gmSo.FindProperty("gameplayScene").stringValue = "Gameplay";
            gmSo.ApplyModifiedPropertiesWithoutUndo();
            VerifyReference(gmSo, "database", "GameManager");

            var canvas = CreateCanvas("Menu Canvas", out var canvasRt);
            CreateEventSystem();

            // 상단 타이틀 + 재화
            var title = Label(canvasRt, "Title", "실험체 #7", 46, Vector2.zero, 600f, TextAlignmentOptions.Left);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.anchoredPosition = new Vector2(36f, -28f);

            var currency = Label(canvasRt, "Currency", "0", 40, Vector2.zero, 400f, TextAlignmentOptions.Right);
            var curRt = currency.rectTransform;
            curRt.anchorMin = curRt.anchorMax = new Vector2(1f, 1f);
            curRt.pivot = new Vector2(1f, 1f);
            curRt.anchoredPosition = new Vector2(-36f, -32f);
            currency.color = new Color(0.98f, 0.86f, 0.42f);

            // ── 스킬트리 (스크롤 영역) ──
            var scrollGo = PrefabGenerator.NewUI("SkillTreeScroll", Vector2.zero, canvasRt);
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = new Vector2(0f, 0f);
            scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.offsetMin = new Vector2(36f, 140f);
            scrollRt.offsetMax = new Vector2(-380f, -116f);
            var scrollBg = scrollGo.AddComponent<Image>();
            scrollBg.color = new Color(0.09f, 0.12f, 0.15f, 0.85f);
            scrollGo.AddComponent<RectMask2D>();
            var scrollRect = scrollGo.AddComponent<ScrollRect>();
            scrollRect.horizontal = true;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.viewport = scrollRt;
            // 휠은 스크롤이 아니라 줌으로 쓴다 (SkillTreeZoomArea). 패닝은 드래그.
            scrollRect.scrollSensitivity = 0f;
            var zoomArea = scrollGo.AddComponent<SkillTreeZoomArea>();

            var content = PrefabGenerator.NewUI("Content", new Vector2(2100f, 1500f), scrollRt);  // SkillTreeUI가 런타임에 트리 크기로 다시 잡는다
            var contentRt = (RectTransform)content.transform;
            contentRt.anchorMin = contentRt.anchorMax = new Vector2(0.5f, 0.5f);
            contentRt.pivot = new Vector2(0.5f, 0.5f);
            scrollRect.content = contentRt;

            var lineRoot = PrefabGenerator.NewUI("Lines", Vector2.zero, contentRt);
            var nodeRoot = PrefabGenerator.NewUI("Nodes", Vector2.zero, contentRt);

            // 상세 패널
            var detail = Panel_(canvasRt, "SkillDetail", new Vector2(1f, 1f), new Vector2(1f, 1f),
                                new Vector2(-36f, -100f), new Vector2(320f, 400f));
            var detailRt = (RectTransform)detail.transform;
            detailRt.pivot = new Vector2(1f, 1f);

            var dName = Label(detailRt, "Name", "스킬", 25, new Vector2(16f, -14f), 288f, TextAlignmentOptions.Left);
            var dCategory = Label(detailRt, "Category", "", 15, new Vector2(16f, -46f), 288f, TextAlignmentOptions.Left);
            dCategory.color = new Color(0.62f, 0.68f, 0.74f);

            var dDesc = Label(detailRt, "Desc", "", 16, new Vector2(16f, -74f), 288f, TextAlignmentOptions.TopLeft);
            dDesc.rectTransform.sizeDelta = new Vector2(288f, 108f);
            dDesc.color = new Color(0.80f, 0.85f, 0.89f);

            var dEffect = Label(detailRt, "Effect", "", 18, new Vector2(16f, -190f), 288f, TextAlignmentOptions.Left);
            dEffect.color = Accent;

            var dReq = Label(detailRt, "Requirements", "", 15, new Vector2(16f, -222f), 288f, TextAlignmentOptions.TopLeft);
            dReq.rectTransform.sizeDelta = new Vector2(288f, 110f);

            var dCost = Label(detailRt, "Cost", "", 16, new Vector2(16f, -342f), 288f, TextAlignmentOptions.Left);

            // 범례 — 윗줄: 선 색 / 아랫줄: 도형 (SkillTreeUI가 런타임에 채운다)
            var legend = Panel_(canvasRt, "Legend", new Vector2(0f, 0f), new Vector2(0f, 0f),
                                new Vector2(384f, 36f), new Vector2(1160f, 62f));
            ((RectTransform)legend.transform).pivot = new Vector2(0f, 0f);
            var legendText = Label((RectTransform)legend.transform, "Text",
                "선  <color=#66FFDB>━ 양쪽 찍음</color>   <color=#FFC257>━ 열린 길</color>   " +
                "<color=#6F8C96>━ 잠김</color>      이어진 칸 중 하나를 찍으면 열립니다" +
                "      <color=#8A9199>휠 확대·축소 · 드래그 이동</color>",
                15, new Vector2(12f, -4f), 1150f, TextAlignmentOptions.Left);

            var legendRow = PrefabGenerator.NewUI("Shapes", new Vector2(1150f, 26f), (RectTransform)legend.transform);
            var legendRowRt = (RectTransform)legendRow.transform;
            legendRowRt.anchorMin = legendRowRt.anchorMax = new Vector2(0f, 1f);
            legendRowRt.pivot = new Vector2(0f, 1f);
            legendRowRt.anchoredPosition = new Vector2(12f, -30f);
            var legendLayout = legendRow.AddComponent<HorizontalLayoutGroup>();
            legendLayout.spacing = 14f;
            legendLayout.childAlignment = TextAnchor.MiddleLeft;
            legendLayout.childControlWidth = true;
            legendLayout.childControlHeight = true;
            legendLayout.childForceExpandWidth = false;
            legendLayout.childForceExpandHeight = false;

            var hint = Label(canvasRt, "TreeHint", "", 17, Vector2.zero, 520f, TextAlignmentOptions.Left);
            var hintRt = hint.rectTransform;
            hintRt.anchorMin = hintRt.anchorMax = new Vector2(0f, 1f);
            hintRt.pivot = new Vector2(0f, 1f);
            hintRt.anchoredPosition = new Vector2(266f, -118f);

            // 줌 / 전체보기 버튼
            var zoomOut = TextButton(canvasRt, "ZoomOut", "−", Vector2.zero, new Vector2(44f, 40f));
            var zoomOutRt = (RectTransform)zoomOut.transform;
            zoomOutRt.anchorMin = zoomOutRt.anchorMax = new Vector2(0f, 1f);
            zoomOutRt.pivot = new Vector2(0f, 1f);
            zoomOutRt.anchoredPosition = new Vector2(36f, -116f);

            var zoomIn = TextButton(canvasRt, "ZoomIn", "+", Vector2.zero, new Vector2(44f, 40f));
            var zoomInRt = (RectTransform)zoomIn.transform;
            zoomInRt.anchorMin = zoomInRt.anchorMax = new Vector2(0f, 1f);
            zoomInRt.pivot = new Vector2(0f, 1f);
            zoomInRt.anchoredPosition = new Vector2(86f, -116f);

            var fitBtn = TextButton(canvasRt, "FitButton", "전체 보기", Vector2.zero, new Vector2(110f, 40f));
            var fitRt = (RectTransform)fitBtn.transform;
            fitRt.anchorMin = fitRt.anchorMax = new Vector2(0f, 1f);
            fitRt.pivot = new Vector2(0f, 1f);
            fitRt.anchoredPosition = new Vector2(136f, -116f);

            var treeUi = canvas.gameObject.AddComponent<SkillTreeUI>();
            var treeSo = new SerializedObject(treeUi);
            treeSo.FindProperty("positionScale").floatValue = 3f;
            treeSo.FindProperty("legendContainer").objectReferenceValue = legendRowRt;
            treeSo.FindProperty("scrollContent").objectReferenceValue = contentRt;
            treeSo.FindProperty("scrollRect").objectReferenceValue = scrollRect;
            treeSo.FindProperty("zoomInButton").objectReferenceValue = zoomIn;
            treeSo.FindProperty("zoomOutButton").objectReferenceValue = zoomOut;
            treeSo.FindProperty("fitButton").objectReferenceValue = fitBtn;
            treeSo.FindProperty("nodeContainer").objectReferenceValue = nodeRoot.transform;
            treeSo.FindProperty("lineContainer").objectReferenceValue = lineRoot.transform;
            treeSo.FindProperty("nodePrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabGenerator.UiPrefabFolder}/SkillNodeButton.prefab")
                    ?.GetComponent<SkillNodeButton>();
            treeSo.FindProperty("linePrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabGenerator.UiPrefabFolder}/SkillLine.prefab")
                    ?.GetComponent<Image>();
            treeSo.FindProperty("lineLabelPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabGenerator.UiPrefabFolder}/LineLabel.prefab")
                    ?.GetComponent<TMP_Text>();
            treeSo.FindProperty("detailPanel").objectReferenceValue = detail;
            treeSo.FindProperty("detailName").objectReferenceValue = dName;
            treeSo.FindProperty("detailCategory").objectReferenceValue = dCategory;
            treeSo.FindProperty("detailDescription").objectReferenceValue = dDesc;
            treeSo.FindProperty("detailEffect").objectReferenceValue = dEffect;
            treeSo.FindProperty("detailRequirements").objectReferenceValue = dReq;
            treeSo.FindProperty("detailCost").objectReferenceValue = dCost;
            treeSo.FindProperty("currencyText").objectReferenceValue = currency;
            treeSo.FindProperty("hintText").objectReferenceValue = hint;
            treeSo.ApplyModifiedPropertiesWithoutUndo();

            var zoomSo = new SerializedObject(zoomArea);
            zoomSo.FindProperty("tree").objectReferenceValue = treeUi;
            zoomSo.ApplyModifiedPropertiesWithoutUndo();

            _ = legendText;

            // ── 우측 하단: 스탯 + 맵 선택 + 시작 ──
            var side = Panel_(canvasRt, "SidePanel", new Vector2(1f, 0f), new Vector2(1f, 0f),
                              new Vector2(-36f, 36f), new Vector2(320f, 480f));
            var sideRt = (RectTransform)side.transform;
            sideRt.pivot = new Vector2(1f, 0f);

            var loadout = Label(sideRt, "Loadout", "", 18, new Vector2(16f, -16f), 288f, TextAlignmentOptions.TopLeft);
            loadout.rectTransform.sizeDelta = new Vector2(288f, 110f);

            var mapListRoot = PrefabGenerator.NewUI("MapList", new Vector2(288f, 180f), sideRt);
            var mapListRt = (RectTransform)mapListRoot.transform;
            mapListRt.anchorMin = mapListRt.anchorMax = new Vector2(0f, 1f);
            mapListRt.pivot = new Vector2(0f, 1f);
            mapListRt.anchoredPosition = new Vector2(16f, -134f);
            var vLayout = mapListRoot.AddComponent<VerticalLayoutGroup>();
            vLayout.spacing = 6f;
            vLayout.childForceExpandHeight = false;
            vLayout.childForceExpandWidth = true;

            var playBtn = TextButton(sideRt, "PlayButton", "탈출 시작",
                                     new Vector2(0f, -330f), new Vector2(288f, 60f));
            playBtn.GetComponent<Image>().color = Accent;

            var stats = Label(sideRt, "Stats", "", 16, new Vector2(16f, -400f), 288f, TextAlignmentOptions.TopLeft);
            stats.rectTransform.sizeDelta = new Vector2(288f, 70f);
            stats.color = new Color(0.68f, 0.74f, 0.79f);

            var zoneUi = canvas.gameObject.AddComponent<ZoneSelectUI>();
            var zoneSo = new SerializedObject(zoneUi);
            zoneSo.FindProperty("container").objectReferenceValue = mapListRoot.transform;
            zoneSo.FindProperty("buttonPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabGenerator.UiPrefabFolder}/MapButton.prefab")
                    ?.GetComponent<Button>();
            zoneSo.ApplyModifiedPropertiesWithoutUndo();

            // 세이브 초기화
            var resetBtn = TextButton(canvasRt, "ResetButton", "세이브 초기화",
                                      new Vector2(0f, 0f), new Vector2(160f, 40f));
            var resetRt = (RectTransform)resetBtn.transform;
            resetRt.anchorMin = resetRt.anchorMax = new Vector2(0f, 0f);
            resetRt.pivot = new Vector2(0f, 0f);
            resetRt.anchoredPosition = new Vector2(36f, 36f);
            resetBtn.GetComponent<Image>().color = new Color(0.35f, 0.22f, 0.24f);

            var confirm = Panel_(canvasRt, "ResetConfirm", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 Vector2.zero, new Vector2(420f, 200f));
            ((RectTransform)confirm.transform).pivot = new Vector2(0.5f, 0.5f);
            Label((RectTransform)confirm.transform, "Text", "모든 진행도가 사라집니다.\n초기화할까요?", 22,
                  new Vector2(0f, -36f), 400f, TextAlignmentOptions.Center)
                .rectTransform.sizeDelta = new Vector2(400f, 70f);
            var yes = TextButton((RectTransform)confirm.transform, "Yes", "초기화",
                                 new Vector2(-100f, -150f), new Vector2(170f, 46f));
            var no  = TextButton((RectTransform)confirm.transform, "No", "취소",
                                 new Vector2(100f, -150f), new Vector2(170f, 46f));
            confirm.SetActive(false);

            // ── 물고기 도감 ──
            var codexBtn = TextButton(canvasRt, "CodexButton", "물고기 도감",
                                      new Vector2(0f, 0f), new Vector2(160f, 40f));
            var codexBtnRt = (RectTransform)codexBtn.transform;
            codexBtnRt.anchorMin = codexBtnRt.anchorMax = new Vector2(0f, 0f);
            codexBtnRt.pivot = new Vector2(0f, 0f);
            codexBtnRt.anchoredPosition = new Vector2(208f, 36f);
            codexBtn.GetComponent<Image>().color = new Color(0.22f, 0.32f, 0.30f);

            var codexPanel = Panel_(canvasRt, "CodexPanel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                    Vector2.zero, new Vector2(660f, 720f));
            var codexRt = (RectTransform)codexPanel.transform;
            codexRt.pivot = new Vector2(0.5f, 0.5f);

            Label(codexRt, "Title", "물고기 도감", 32, new Vector2(24f, -20f), 400f, TextAlignmentOptions.Left);
            var codexSummary = Label(codexRt, "Summary", "", 17,
                                     new Vector2(24f, -64f), 600f, TextAlignmentOptions.TopLeft);
            codexSummary.rectTransform.sizeDelta = new Vector2(600f, 52f);
            codexSummary.color = new Color(0.74f, 0.80f, 0.86f);

            var codexScroll = PrefabGenerator.NewUI("CodexScroll", Vector2.zero, codexRt);
            var codexScrollRt = (RectTransform)codexScroll.transform;
            codexScrollRt.anchorMin = new Vector2(0f, 0f);
            codexScrollRt.anchorMax = new Vector2(1f, 1f);
            codexScrollRt.offsetMin = new Vector2(20f, 70f);
            codexScrollRt.offsetMax = new Vector2(-20f, -124f);
            codexScroll.AddComponent<RectMask2D>();
            var codexRect = codexScroll.AddComponent<ScrollRect>();
            codexRect.horizontal = false;
            codexRect.vertical = true;
            codexRect.movementType = ScrollRect.MovementType.Elastic;
            codexRect.scrollSensitivity = 32f;

            var codexContent = PrefabGenerator.NewUI("Content", new Vector2(600f, 100f), codexScrollRt);
            var codexContentRt = (RectTransform)codexContent.transform;
            codexContentRt.anchorMin = new Vector2(0f, 1f);
            codexContentRt.anchorMax = new Vector2(1f, 1f);
            codexContentRt.pivot = new Vector2(0.5f, 1f);
            codexContentRt.anchoredPosition = Vector2.zero;
            codexRect.content = codexContentRt;

            var codexLayout = codexContent.AddComponent<VerticalLayoutGroup>();
            codexLayout.spacing = 6f;
            codexLayout.padding = new RectOffset(4, 4, 4, 4);
            codexLayout.childForceExpandHeight = false;
            codexLayout.childForceExpandWidth = true;
            var codexFitter = codexContent.AddComponent<ContentSizeFitter>();
            codexFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var codexClose = TextButton(codexRt, "CloseButton", "닫기",
                                        new Vector2(0f, -680f), new Vector2(200f, 46f));
            codexPanel.SetActive(false);

            var codexUi = canvas.gameObject.AddComponent<CodexUI>();
            var codexSo = new SerializedObject(codexUi);
            codexSo.FindProperty("root").objectReferenceValue = codexPanel;
            codexSo.FindProperty("openButton").objectReferenceValue = codexBtn;
            codexSo.FindProperty("closeButton").objectReferenceValue = codexClose;
            codexSo.FindProperty("listContainer").objectReferenceValue = codexContent.transform;
            codexSo.FindProperty("rowPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabGenerator.UiPrefabFolder}/CodexRow.prefab")
                    ?.GetComponent<CodexRow>();
            codexSo.FindProperty("summaryText").objectReferenceValue = codexSummary;
            codexSo.ApplyModifiedPropertiesWithoutUndo();

            var menuUi = canvas.gameObject.AddComponent<MainMenuUI>();
            var menuSo = new SerializedObject(menuUi);
            menuSo.FindProperty("playButton").objectReferenceValue = playBtn;
            menuSo.FindProperty("resetSaveButton").objectReferenceValue = resetBtn;
            menuSo.FindProperty("currencyText").objectReferenceValue = currency;
            menuSo.FindProperty("statsText").objectReferenceValue = stats;
            menuSo.FindProperty("loadoutText").objectReferenceValue = loadout;
            menuSo.FindProperty("resetConfirmPanel").objectReferenceValue = confirm;
            menuSo.FindProperty("resetConfirmYes").objectReferenceValue = yes;
            menuSo.FindProperty("resetConfirmNo").objectReferenceValue = no;
            menuSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, $"{SceneFolder}/MainMenu.unity");
        }

        // ═════════════════════════════════════════════════════════
        //  UI 헬퍼
        // ═════════════════════════════════════════════════════════
        static Canvas CreateCanvas(string name, out RectTransform rt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            rt = (RectTransform)go.transform;
            return canvas;
        }

        static void CreateEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        static GameObject Panel_(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                 Vector2 position, Vector2 size)
        {
            var go = PrefabGenerator.NewUI(name, size, parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(anchorMin.x, anchorMax.y);
            rt.anchoredPosition = position;
            var img = go.AddComponent<Image>();
            img.color = Panel;
            return go;
        }

        // Panel_()은 GameObject를 돌려주므로 GameObject로도 받을 수 있게 오버로드를 둔다.
        static TMP_Text Label(GameObject parent, string name, string content, float size,
                              Vector2 position, float width, TextAlignmentOptions align)
            => Label((RectTransform)parent.transform, name, content, size, position, width, align);

        static GameObject Bar(GameObject parent, string name, Vector2 position, float width, out Image fill)
            => Bar((RectTransform)parent.transform, name, position, width, out fill);

        static TMP_Text Label(RectTransform parent, string name, string content, float size,
                              Vector2 position, float width, TextAlignmentOptions align)
        {
            var go = PrefabGenerator.NewUI(name, new Vector2(width, size * 1.5f), parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = position;
            var text = PrefabGenerator.AddText(go, content, size, align);
            text.color = Ink;
            return text;
        }

        static GameObject Bar(RectTransform parent, string name, Vector2 position, float width,
                              out Image fill)
        {
            var bg = PrefabGenerator.NewUI(name, new Vector2(width, 12f), parent);
            var bgRt = (RectTransform)bg.transform;
            bgRt.anchorMin = bgRt.anchorMax = new Vector2(0f, 1f);
            bgRt.pivot = new Vector2(0f, 1f);
            bgRt.anchoredPosition = position;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.45f);

            var fillGo = PrefabGenerator.NewUI("Fill", Vector2.zero, bgRt);
            Stretch((RectTransform)fillGo.transform);
            fill = fillGo.AddComponent<Image>();
            fill.color = Accent;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 1f;
            fill.sprite = PlaceholderArt.CreateSolidSprite("UI_White", Color.white, 8);
            return bg;
        }

        static GameObject SkillSlot(RectTransform parent, string name, string key, out Image fill)
        {
            var go = PrefabGenerator.NewUI(name, new Vector2(72f, 72f), parent);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.15f, 0.19f, 0.9f);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 72f;
            le.preferredHeight = 72f;

            var fillGo = PrefabGenerator.NewUI("Cooldown", Vector2.zero, (RectTransform)go.transform);
            Stretch((RectTransform)fillGo.transform);
            fill = fillGo.AddComponent<Image>();
            fill.color = new Color(0f, 0f, 0f, 0.65f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = 2;
            fill.fillAmount = 0f;
            fill.raycastTarget = false;
            fill.sprite = PlaceholderArt.CreateSolidSprite("UI_White", Color.white, 8);

            var labelGo = PrefabGenerator.NewUI("Key", new Vector2(76f, 44f), (RectTransform)go.transform);
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = Vector2.zero;
            var text = PrefabGenerator.AddText(labelGo, key, 14f, TextAlignmentOptions.Center);
            text.color = Ink;

            return go;
        }

        static Button TextButton(RectTransform parent, string name, string label,
                                 Vector2 position, Vector2 size)
        {
            var go = PrefabGenerator.NewUI(name, size, parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = position;

            var img = go.AddComponent<Image>();
            img.color = new Color(0.22f, 0.28f, 0.34f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;

            var labelGo = PrefabGenerator.NewUI("Label", size, rt);
            Stretch((RectTransform)labelGo.transform);
            var text = PrefabGenerator.AddText(labelGo, label, 22f, TextAlignmentOptions.Center);
            text.color = new Color(0.97f, 0.98f, 0.99f);

            return button;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void RegisterScenesInBuildSettings()
        {
            var scenes = new[]
            {
                new EditorBuildSettingsScene($"{SceneFolder}/MainMenu.unity", true),
                new EditorBuildSettingsScene($"{SceneFolder}/Gameplay.unity", true),
            };
            EditorBuildSettings.scenes = scenes;
        }
    }
}
#endif
