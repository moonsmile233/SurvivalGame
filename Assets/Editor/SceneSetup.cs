using System.IO;
using Survival.Core;
using Survival.Enemy;
using Survival.Player;
using Survival.UI;
using Survival.World;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 一键重建 Assets/Scenes/Game.unity，并补齐 GameConfig / 占位 Sprite。
/// 场景是唯一静态结构来源，不允许只靠手工拖拽（见 CLAUDE.md 硬约束 2）。
/// 敌人是动态物体，场景里不预摆（M1 ticket 第 1 节）。
/// </summary>
public static class SceneSetup
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string SceneFolder = "Assets/Scenes";
    private const string ConfigFolder = "Assets/Configs";
    private const string ConfigPath = "Assets/Configs/cfg_GameConfig.asset";
    private const string EnemyDefinitionPath = "Assets/Configs/def_Enemy_Chaser.asset";
    private const string DifficultyEasyPath = "Assets/Configs/dif_Easy.asset";
    private const string DifficultyNormalPath = "Assets/Configs/dif_Normal.asset";
    private const string DifficultyHardPath = "Assets/Configs/dif_Hard.asset";
    private const string SpriteFolder = "Assets/Sprites";
    private const string BackgroundSpritePath = "Assets/Sprites/spr_background_grid.png";
    private const string PlayerSpritePath = "Assets/Sprites/spr_player.png";
    private const string EnemySpritePath = "Assets/Sprites/spr_enemy.png";
    private const string UiSpritePath = "Assets/Sprites/ui_white.png";
    private const string FontAssetPath = FontAssetBuilder.OutputPath;

    private const float SpritePixelsPerUnit = 32f;

    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
    private static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
    private static readonly Vector2 TopRight = new Vector2(1f, 1f);
    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

    /// <summary>UI Image 需要非空 Sprite 才能使用 Filled 类型，生成时从这里取。</summary>
    private static Sprite s_uiSprite;

    /// <summary>项目内置 CJK 子集字体（Assets/Fonts/NotoSansSC-UI-SDF.asset，Static SDF）。</summary>
    private static TMP_FontAsset s_uiFont;

    [MenuItem("Survival/Setup Scene")]
    private static void GenerateSceneMenu()
    {
        GenerateScene();
    }

    /// <summary>重建场景。返回 false 表示前置资源有问题（如字体生成失败），调用方不应继续。</summary>
    public static bool GenerateScene()
    {
        EnsureFolder(SceneFolder);
        EnsureFolder(SpriteFolder);

        // 字体资产由 FontAssetBuilder 用脚本生成（不再手动点 Font Asset Creator）。
        if (!FontAssetBuilder.EnsureFontAsset())
        {
            Debug.LogError($"[SceneSetup] 字体资产生成失败（{FontAssetPath}），已中止。");
            return false;
        }

        // 当前场景有未保存改动时先存盘，避免 NewScene 弹保存对话框卡住自动化。
        var active = SceneManager.GetActiveScene();
        if (active.isDirty && !string.IsNullOrEmpty(active.path))
        {
            EditorSceneManager.SaveScene(active);
        }

        GameConfig config = EnsureConfig();
        EnsureDifficultyProfiles(config);
        Sprite backgroundSprite = EnsureSprite(BackgroundSpritePath, CreateGridTexture(), true);
        Sprite playerSprite = EnsureSprite(PlayerSpritePath, CreatePlayerTexture(), false);
        Sprite enemySprite = EnsureSprite(EnemySpritePath, CreateEnemyTexture(), false);
        EnsureSprite(UiSpritePath, CreateWhiteTexture(), false);
        EnsureEnemyDefinition(enemySprite);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // NewScene 会卸载未被场景引用的资源（GameConfig 会被判为未使用），必须重新取一次资产引用。
        config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
        backgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundSpritePath);
        playerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlayerSpritePath);
        enemySprite = AssetDatabase.LoadAssetAtPath<Sprite>(EnemySpritePath);
        s_uiSprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiSpritePath);
        s_uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        EnemyDefinition enemyDefinition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(EnemyDefinitionPath);

        Camera cam = CreateMainCamera();
        CreateBackground(config, backgroundSprite);

        GameObject player = CreatePlayer(config, playerSprite);
        PlayerController playerController = player.GetComponent<PlayerController>();
        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
        TouchInput touchInput = player.GetComponent<TouchInput>();

        // 大地图（36×20）大于视野，相机在 LateUpdate 跟随并钳在地图内（M4-B0）。
        var cameraFollow = cam.gameObject.AddComponent<CameraFollow>();
        SetObjectReference(cameraFollow, "_config", config);
        SetObjectReference(cameraFollow, "_target", player.transform);
        SetObjectReference(cameraFollow, "_camera", cam);

        EnemySpawner spawner = CreateEnemySpawner(config, player.transform, playerHealth, cam, enemyDefinition);
        GameManager manager = CreateGameManager(config, playerController, playerHealth, spawner);

        // Ready 状态禁止移动：PlayerController 需要读 GameManager.State（M3 第 1.2 节）。
        SetObjectReference(playerController, "_gameManager", manager);

        // 桥只负责协议；v1.2 起结算面板不再读桥（结算归 Unity），顺序不再有依赖。
        WebGLBridge bridge = CreateBridge(manager);
        CreateHud(manager, playerHealth, config, touchInput);
        CreateBootstrap(bridge);

        EditorSceneManager.SaveScene(scene, ScenePath);

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        Debug.Log($"[SceneSetup] Scene saved: {Path.GetFullPath(ScenePath)}");
        return true;
    }

    private static Camera CreateMainCamera()
    {
        var go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        go.transform.position = new Vector3(0f, 0f, -10f);

        var camera = go.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0x1E / 255f, 0x1E / 255f, 0x2E / 255f, 1f);
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 1000f;

        go.AddComponent<AudioListener>();
        return camera;
    }

    private static void CreateBackground(GameConfig config, Sprite sprite)
    {
        var go = new GameObject("Background");
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.tileMode = SpriteTileMode.Continuous;
        // 背景和地图同尺寸：走到边缘能看到网格结束、外面是相机 clear color（不是空气墙）。
        renderer.size = new Vector2(config.MapWidth, config.MapHeight);
        renderer.sortingOrder = -10;
    }

    private static GameObject CreatePlayer(GameConfig config, Sprite sprite)
    {
        var go = new GameObject("Player");
        go.transform.position = Vector3.zero;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 5;

        var body = go.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;

        var collider = go.AddComponent<CircleCollider2D>();
        collider.radius = 0.4f;

        // 键盘 + 触控同时挂在玩家上，由 Router 选当前有输入的那个（M4 第 1.1 节）；
        // PlayerController 只认抽象基类，不做平台判断。
        var keyboard = go.AddComponent<KeyboardInput>();
        var touch = go.AddComponent<TouchInput>();
        var router = go.AddComponent<PlayerInputRouter>();
        var controller = go.AddComponent<PlayerController>();
        var health = go.AddComponent<PlayerHealth>();
        var view = go.AddComponent<PlayerView>();

        SetObjectReference(router, "_keyboard", keyboard);
        SetObjectReference(router, "_touch", touch);
        SetObjectReference(controller, "_config", config);
        SetObjectReference(controller, "_input", router);
        SetObjectReference(health, "_config", config);
        SetObjectReference(view, "_health", health);
        SetObjectReference(view, "_config", config);

        return go;
    }

    private static EnemySpawner CreateEnemySpawner(
        GameConfig config,
        Transform playerTarget,
        PlayerHealth playerHealth,
        Camera camera,
        EnemyDefinition definition)
    {
        var go = new GameObject("EnemySpawner");
        var spawner = go.AddComponent<EnemySpawner>();

        SetObjectReference(spawner, "_config", config);
        SetObjectReference(spawner, "_playerTarget", playerTarget);
        SetObjectReference(spawner, "_playerHealth", playerHealth);
        SetObjectReference(spawner, "_camera", camera);
        SetObjectReferenceArray(spawner, "_enemyDefinitions", new Object[] { definition });

        return spawner;
    }

    private static GameManager CreateGameManager(
        GameConfig config,
        PlayerController playerController,
        PlayerHealth playerHealth,
        EnemySpawner spawner)
    {
        var go = new GameObject("GameManager");
        var manager = go.AddComponent<GameManager>();

        SetObjectReference(manager, "_config", config);
        SetObjectReference(manager, "_playerController", playerController);
        SetObjectReference(manager, "_playerHealth", playerHealth);
        SetObjectReference(manager, "_enemySpawner", spawner);

        // v1.2 起默认停在 Unity 菜单，由玩家在画布内开局；`_autoStart` 只留给编辑器自测。
        SetSerializedBool(manager, "_autoStart", false);

        // 桌面 Esc / P 暂停（M4 §1.7）；和 GameManager 同物体，注入引用，不自己建状态。
        var hotkey = go.AddComponent<PauseHotkey>();
        SetObjectReference(hotkey, "_gameManager", manager);

        // 独立运行失焦暂停（v1.2）：WebGL/Standalone 生效，Editor 内自跳过。
        var focusPause = go.AddComponent<ApplicationFocusPause>();
        SetObjectReference(focusPause, "_gameManager", manager);

        return manager;
    }

    private static WebGLBridge CreateBridge(GameManager manager)
    {
        var go = new GameObject("WebGLBridge");
        var bridge = go.AddComponent<WebGLBridge>();
        SetObjectReference(bridge, "_gameManager", manager);
        return bridge;
    }

    private static void CreateBootstrap(WebGLBridge bridge)
    {
        var go = new GameObject("Bootstrap");
        var bootstrap = go.AddComponent<GameBootstrap>();
        SetObjectReference(bootstrap, "_bridge", bridge);
    }

    // ---------- HUD / 面板（ticket M2 第 1 节，全部代码生成） ----------

    private static void CreateHud(
        GameManager manager,
        PlayerHealth playerHealth,
        GameConfig config,
        TouchInput touchInput)
    {
        var eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<EventSystem>();
        eventSystemGo.AddComponent<StandaloneInputModule>();

        var canvasGo = new GameObject("HUD Canvas", typeof(RectTransform));
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // 手机窄屏（Screen.width < 1000 设备像素）时切到 640×360 参考分辨率，HUD 与面板一起放大（M4 第 1.2 节）。
        var layout = canvasGo.AddComponent<HudCanvasScaler>();
        SetObjectReference(layout, "_scaler", scaler);

        var canvasRect = (RectTransform)canvasGo.transform;

        // ---- 常驻 HUD：顶行 HP 左 / 时间中 / 分数右；暂停挪到右上（拇指可达，也不和分数抢顶行） ----
        RectTransform hud = CreateRect("HUD", canvasRect);
        Stretch(hud);

        Image hpBarBg = CreateImage("HPBar", hud, new Color(0.10f, 0.10f, 0.16f, 0.85f));
        SetAnchored(hpBarBg.rectTransform, TopLeft, TopLeft, new Vector2(16f, -16f), new Vector2(200f, 18f));

        Image hpFill = CreateImage("Fill", hpBarBg.transform, new Color(0.30f, 0.85f, 0.45f, 1f));
        Stretch(hpFill.rectTransform, 2f);
        hpFill.type = Image.Type.Filled;
        hpFill.fillMethod = Image.FillMethod.Horizontal;
        hpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        hpFill.fillAmount = 1f;

        TMP_Text hpText = CreateText("HPText", hud, $"{UiStrings.HealthLabel} 100/100", 22f,
            TextAlignmentOptions.TopLeft);
        SetAnchored(hpText.rectTransform, TopLeft, TopLeft, new Vector2(16f, -38f), new Vector2(220f, 30f));

        TMP_Text timeText = CreateText("TimeText", hud, $"{UiStrings.TimeLabel} 01:00", 34f,
            TextAlignmentOptions.Top, true);
        SetAnchored(timeText.rectTransform, TopCenter, TopCenter, new Vector2(0f, -14f), new Vector2(260f, 44f));

        // 暂停在右上；高 80：在 640×360 参考下，390 手机约 46 CSS px、320 约 37 CSS px，满足触控目标 ≥36（M4.7 响应式矩阵）。
        Button pauseButton = CreateButton("PauseButton", hud, UiStrings.PauseButton, TopRight, TopRight,
            new Vector2(-16f, -12f), new Vector2(140f, 80f), 26f);

        TMP_Text scoreText = CreateText("ScoreText", hud, $"{UiStrings.ScoreLabel} 0", 24f,
            TextAlignmentOptions.TopRight, true);
        SetAnchored(scoreText.rectTransform, TopRight, TopRight, new Vector2(-16f, -102f), new Vector2(220f, 30f));

        // ---- 主菜单（v1.2）：不透明底 + 三档难度卡 + 开始；不显示最高分/昵称/全屏 ----
        RectTransform menuPanel = CreatePanel("MenuPanel", canvasRect);
        Image menuBackdrop = CreateImage("Backdrop", menuPanel, new Color(0.05f, 0.05f, 0.09f, 1f));
        Stretch(menuBackdrop.rectTransform);

        TMP_Text menuTitle = CreateText("Title", menuPanel, UiStrings.MenuTitle, 54f,
            TextAlignmentOptions.Center, true);
        SetBand(menuTitle.rectTransform, 0.10f, 0.90f, 0.86f, 60f);

        TMP_Text menuSubtitle = CreateText("Subtitle", menuPanel, UiStrings.MenuSubtitle, 24f,
            TextAlignmentOptions.Center);
        SetBand(menuSubtitle.rectTransform, 0.10f, 0.90f, 0.74f, 32f);

        RectTransform cardsRow = CreateRect("DifficultyCards", menuPanel);
        cardsRow.anchorMin = new Vector2(0.12f, 0.36f);
        cardsRow.anchorMax = new Vector2(0.88f, 0.62f);
        cardsRow.pivot = new Vector2(0.5f, 0.5f);
        cardsRow.offsetMin = Vector2.zero;
        cardsRow.offsetMax = Vector2.zero;

        Button easyCard = CreateMenuCard(cardsRow, 0.01f, 1f / 3f - 0.01f,
            UiStrings.DifficultyEasy, UiStrings.TagEasy, MultiplierText(config, Difficulty.Easy),
            out Image easyBackground);
        Button normalCard = CreateMenuCard(cardsRow, 1f / 3f + 0.01f, 2f / 3f - 0.01f,
            UiStrings.DifficultyNormal, UiStrings.TagNormal, MultiplierText(config, Difficulty.Normal),
            out Image normalBackground);
        Button hardCard = CreateMenuCard(cardsRow, 2f / 3f + 0.01f, 1f - 0.01f,
            UiStrings.DifficultyHard, UiStrings.TagHard, MultiplierText(config, Difficulty.Hard),
            out Image hardBackground);

        Button menuStartButton = CreateButtonBand("StartButton", menuPanel, 0.27f, 0.73f, 0.20f, 88f,
            UiStrings.StartGameButton, 32f);

        // ---- 暂停菜单（默认隐藏）：继续 / 重新开始 / 返回菜单；3-2-1 由 StartCountdownPanel 显示 ----
        RectTransform pausePanel = CreatePanel("PausePanel", canvasRect);
        CreateDim(pausePanel);

        TMP_Text pauseTitle = CreateText("Title", pausePanel, UiStrings.PausedTitle, 60f,
            TextAlignmentOptions.Center, true);
        SetAnchored(pauseTitle.rectTransform, new Vector2(0.5f, 0.68f), Center, Vector2.zero,
            new Vector2(700f, 90f));
        // 标题是静态文案；PausePanel 不持有引用（不需要运行时改字）。

        // 三按钮同一水平带，宽度按百分比切分：640×360 参考下约 187 单位，仍可点（≥44 CSS px）。
        Button resumeButton = CreateButtonBand("ResumeButton", pausePanel, 0.055f, 0.348f, 0.30f, 84f,
            UiStrings.ResumeButton, 22f);
        Button pauseRestartButton = CreateButtonBand("RestartButton", pausePanel, 0.351f, 0.649f, 0.30f, 84f,
            UiStrings.RestartButton, 22f);
        Button pauseMenuButton = CreateButtonBand("MenuButton", pausePanel, 0.652f, 0.945f, 0.30f, 84f,
            UiStrings.BackToMenuButton, 22f);

        // ---- 结算（默认隐藏；v1.2 起 WebGL 里也显示，结算归 Unity） ----
        RectTransform resultPanel = CreatePanel("ResultPanel", canvasRect);
        CreateDim(resultPanel);

        TMP_Text resultTitle = CreateText("TitleText", resultPanel, UiStrings.VictoryTitle, 56f,
            TextAlignmentOptions.Center, true);
        SetBand(resultTitle.rectTransform, 0.10f, 0.90f, 0.80f, 72f);

        TMP_Text resultScore = CreateText("ScoreText", resultPanel, $"{UiStrings.RoundScoreLabel} 0", 36f,
            TextAlignmentOptions.Center, true);
        SetBand(resultScore.rectTransform, 0.10f, 0.90f, 0.62f, 48f);

        TMP_Text resultInfo = CreateText("InfoText", resultPanel,
            $"{UiStrings.DifficultyLabel} {UiStrings.DifficultyNormal}", 22f,
            TextAlignmentOptions.Center);
        SetBand(resultInfo.rectTransform, 0.06f, 0.94f, 0.49f, 60f);
        // 窄窗口（390 直立）允许折成两行；16:9 下仍是一行。
        resultInfo.textWrappingMode = TextWrappingModes.Normal;

        Button againButton = CreateButtonBand("AgainButton", resultPanel, 0.16f, 0.48f, 0.23f, 76f,
            UiStrings.PlayAgainButton, 24f);
        Button resultMenuButton = CreateButtonBand("MenuButton", resultPanel, 0.52f, 0.84f, 0.23f, 76f,
            UiStrings.BackToMenuButton, 24f);

        // ---- 统一 3-2-1（默认隐藏）：菜单开始 / 暂停继续 / 重开 / 再来一局共用 ----
        RectTransform countdownPanel = CreatePanel("StartCountdownPanel", canvasRect);
        CreateDim(countdownPanel);

        TMP_Text countdownText = CreateText("CountdownText", countdownPanel, "3", 120f,
            TextAlignmentOptions.Center, true);
        SetAnchored(countdownText.rectTransform, Center, Center, Vector2.zero, new Vector2(300f, 160f));

        menuPanel.gameObject.SetActive(false);
        pausePanel.gameObject.SetActive(false);
        resultPanel.gameObject.SetActive(false);
        countdownPanel.gameObject.SetActive(false);

        // ---- 触控浮动摇杆（M4.7，只做可视化；输入仍由 TouchInput 决定） ----
        // 放成第一个 sibling，保证在 HUD 与菜单/暂停/结算/倒计时面板下层；初始隐藏，由 View 在触摸时显示。
        Image joystickBase = CreateImage("Joystick", canvasRect, new Color(1f, 1f, 1f, 0f));
        SetAnchored(joystickBase.rectTransform, Center, Center, Vector2.zero, new Vector2(160f, 160f));
        joystickBase.rectTransform.SetAsFirstSibling();
        joystickBase.gameObject.SetActive(false);

        Image joystickKnob = CreateImage("Knob", joystickBase.rectTransform, new Color(1f, 1f, 1f, 0f));
        SetAnchored(joystickKnob.rectTransform, Center, Center, Vector2.zero, new Vector2(58f, 58f));

        // ---- 注入引用（控制器挂在常驻的 Canvas 上，面板自身可以 inactive） ----
        var hudView = canvasGo.AddComponent<HudView>();
        SetObjectReference(hudView, "_gameManager", manager);
        SetObjectReference(hudView, "_playerHealth", playerHealth);
        SetObjectReference(hudView, "_hudRoot", hud.gameObject);
        SetObjectReference(hudView, "_hpFill", hpFill);
        SetObjectReference(hudView, "_hpText", hpText);
        SetObjectReference(hudView, "_timeText", timeText);
        SetObjectReference(hudView, "_scoreText", scoreText);
        SetObjectReference(hudView, "_pauseButton", pauseButton);

        var menuController = canvasGo.AddComponent<MenuPanel>();
        SetObjectReference(menuController, "_gameManager", manager);
        SetObjectReference(menuController, "_panel", menuPanel.gameObject);
        SetObjectReference(menuController, "_startButton", menuStartButton);
        SetObjectReferenceArray(menuController, "_difficultyButtons", new Object[] { easyCard, normalCard, hardCard });
        SetObjectReferenceArray(menuController, "_difficultyBackgrounds",
            new Object[] { easyBackground, normalBackground, hardBackground });

        var pauseController = canvasGo.AddComponent<PausePanel>();
        SetObjectReference(pauseController, "_gameManager", manager);
        SetObjectReference(pauseController, "_panel", pausePanel.gameObject);
        SetObjectReference(pauseController, "_resumeButton", resumeButton);
        SetObjectReference(pauseController, "_restartButton", pauseRestartButton);
        SetObjectReference(pauseController, "_menuButton", pauseMenuButton);

        var resultController = canvasGo.AddComponent<ResultPanel>();
        SetObjectReference(resultController, "_gameManager", manager);
        SetObjectReference(resultController, "_panel", resultPanel.gameObject);
        SetObjectReference(resultController, "_titleText", resultTitle);
        SetObjectReference(resultController, "_scoreText", resultScore);
        SetObjectReference(resultController, "_infoText", resultInfo);
        SetObjectReference(resultController, "_againButton", againButton);
        SetObjectReference(resultController, "_menuButton", resultMenuButton);

        var countdownController = canvasGo.AddComponent<StartCountdownPanel>();
        SetObjectReference(countdownController, "_gameManager", manager);
        SetObjectReference(countdownController, "_panel", countdownPanel.gameObject);
        SetObjectReference(countdownController, "_countdownText", countdownText);

        var joystickView = canvasGo.AddComponent<TouchJoystickView>();
        SetObjectReference(joystickView, "_touchInput", touchInput);
        SetObjectReference(joystickView, "_gameManager", manager);
        SetObjectReference(joystickView, "_canvas", canvas);
        SetObjectReference(joystickView, "_canvasRect", canvasRect);
        SetObjectReference(joystickView, "_base", joystickBase.rectTransform);
        SetObjectReference(joystickView, "_knob", joystickKnob.rectTransform);
        SetObjectReference(joystickView, "_baseImage", joystickBase);
        SetObjectReference(joystickView, "_knobImage", joystickKnob);
    }

    /// <summary>菜单卡/结算信息展示的倍率文本；读真实资产，避免数值改了文案不跟。</summary>
    private static string MultiplierText(GameConfig config, Difficulty difficulty)
    {
        DifficultyProfile profile = config != null ? config.GetProfile(difficulty) : null;
        float multiplier = profile != null ? profile.ScoreMultiplier : 1f;
        return $"{UiStrings.TimesSign}{multiplier:0.0}";
    }

    /// <summary>横向铺满父节点的一段（x 用百分比锚点），纵向固定高度并居中在 yAnchor。</summary>
    private static void SetBand(RectTransform rect, float xMin, float xMax, float yAnchor, float height)
    {
        rect.anchorMin = new Vector2(xMin, yAnchor);
        rect.anchorMax = new Vector2(xMax, yAnchor);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, height);
    }

    private static Button CreateButtonBand(string name, Transform parent, float xMin, float xMax, float yAnchor,
        float height, string label, float labelFontSize)
    {
        RectTransform rect = CreateRect(name, parent);
        SetBand(rect, xMin, xMax, yAnchor, height);
        return BuildButton(rect, label, labelFontSize);
    }

    /// <summary>
    /// 菜单难度卡：整卡是按钮，内含名称/短标签/倍率三行文本。
    /// 背景 Image 同时是 Button.targetGraphic，MenuPanel 直接改它的 color 表示选中态。
    /// </summary>
    private static Button CreateMenuCard(Transform parent, float xMin, float xMax, string title, string tag,
        string multiplier, out Image background)
    {
        RectTransform rect = CreateRect("Card", parent);
        rect.anchorMin = new Vector2(xMin, 0f);
        rect.anchorMax = new Vector2(xMax, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        background = rect.gameObject.AddComponent<Image>();
        background.sprite = s_uiSprite;
        background.color = new Color(0.13f, 0.13f, 0.20f, 1f);
        background.raycastTarget = true;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.ColorTint;

        TMP_Text titleText = CreateText("Title", rect, title, 30f, TextAlignmentOptions.Center, true);
        SetBand(titleText.rectTransform, 0.02f, 0.98f, 0.76f, 30f);

        TMP_Text tagText = CreateText("Tag", rect, tag, 18f, TextAlignmentOptions.Center);
        SetBand(tagText.rectTransform, 0.02f, 0.98f, 0.47f, 22f);

        TMP_Text multiplierText = CreateText("Multiplier", rect, multiplier, 18f, TextAlignmentOptions.Center);
        SetBand(multiplierText.rectTransform, 0.02f, 0.98f, 0.20f, 22f);

        return button;
    }

    private static RectTransform CreatePanel(string name, Transform parent)
    {
        RectTransform rect = CreateRect(name, parent);
        Stretch(rect);
        return rect;
    }

    private static void CreateDim(Transform parent)
    {
        // 0.72：冻结的敌人/玩家仍能看见，但不会压在倒计时数字或结算信息行上（P3 2026-11-01）。
        Image dim = CreateImage("Dim", parent, new Color(0f, 0f, 0f, 0.72f));
        Stretch(dim.rectTransform);

        // 半透明遮罩吞掉点击，避免穿透到下层按钮。
        dim.raycastTarget = true;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void Stretch(RectTransform rect, float padding = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    private static void SetAnchored(
        RectTransform rect,
        Vector2 anchor,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        RectTransform rect = CreateRect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = s_uiSprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        string content,
        float fontSize,
        TextAlignmentOptions alignment,
        bool bold = false)
    {
        RectTransform rect = CreateRect(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = GetUiFontAsset();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.text = content;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        return text;
    }

    private static Button CreateButton(
        string name,
        Transform parent,
        string label,
        Vector2 anchor,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 size,
        float labelFontSize)
    {
        RectTransform rect = CreateRect(name, parent);
        SetAnchored(rect, anchor, pivot, anchoredPosition, size);
        return BuildButton(rect, label, labelFontSize);
    }

    /// <summary>给已定好 RectTransform 的节点补 Image + Button + 居中标签（固定/百分比两种入口共用）。</summary>
    private static Button BuildButton(RectTransform rect, string label, float labelFontSize)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = s_uiSprite;
        image.color = new Color(0.24f, 0.24f, 0.36f, 1f);
        image.raycastTarget = true;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        TMP_Text text = CreateText("Label", rect, label, labelFontSize, TextAlignmentOptions.Center, true);
        Stretch(text.rectTransform);

        return button;
    }

    /// <summary>项目内置 CJK 子集字体资产（ticket M2 第 0 节；禁止 legacy Text / LegacyRuntime.ttf）。</summary>
    private static TMP_FontAsset GetUiFontAsset()
    {
        return s_uiFont;
    }

    // ---------- 资源 ----------

    private static GameConfig EnsureConfig()
    {
        EnsureFolder(ConfigFolder);

        var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
        if (config != null)
        {
            return config;
        }

        config = ScriptableObject.CreateInstance<GameConfig>();
        AssetDatabase.CreateAsset(config, ConfigPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SceneSetup] Created config: {ConfigPath}");
        return config;
    }

    /// <summary>
    /// 三档难度资产（M6 §7.2）。**只有不存在时才创建并写默认值**：已存在就保留 Inspector 里的调参，
    /// 否则每次重建场景都会把平衡改回默认。数组每次重新挂到 GameConfig（幂等）。
    /// </summary>
    private static void EnsureDifficultyProfiles(GameConfig config)
    {
        EnsureFolder(ConfigFolder);

        var profiles = new DifficultyProfile[3];
        profiles[(int)Difficulty.Easy] = EnsureDifficultyProfile(
            DifficultyEasyPath, Difficulty.Easy, speed: 0.90f, spawn: 1.20f, damage: 0.80f, limit: 14, pressure: 1.10f, score: 1.0f);
        profiles[(int)Difficulty.Normal] = EnsureDifficultyProfile(
            DifficultyNormalPath, Difficulty.Normal, speed: 1.00f, spawn: 1.00f, damage: 1.00f, limit: 20, pressure: 1.15f, score: 1.3f);
        profiles[(int)Difficulty.Hard] = EnsureDifficultyProfile(
            DifficultyHardPath, Difficulty.Hard, speed: 1.10f, spawn: 0.85f, damage: 1.15f, limit: 20, pressure: 1.20f, score: 1.6f);

        SetObjectReferenceArray(config, "_difficulties", profiles);
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
    }

    private static DifficultyProfile EnsureDifficultyProfile(
        string path,
        Difficulty id,
        float speed,
        float spawn,
        float damage,
        int limit,
        float pressure,
        float score)
    {
        var profile = AssetDatabase.LoadAssetAtPath<DifficultyProfile>(path);
        if (profile != null)
        {
            return profile;
        }

        profile = ScriptableObject.CreateInstance<DifficultyProfile>();
        AssetDatabase.CreateAsset(profile, path);

        // 字段是 private + [SerializeField]，用 SerializedObject 写初始值（与 SetObjectReference 同一套做法）。
        var serialized = new SerializedObject(profile);
        serialized.FindProperty("_id").intValue = (int)id;
        serialized.FindProperty("_enemySpeedMultiplier").floatValue = speed;
        serialized.FindProperty("_spawnIntervalMultiplier").floatValue = spawn;
        serialized.FindProperty("_damageMultiplier").floatValue = damage;
        serialized.FindProperty("_enemyLimit").intValue = limit;
        serialized.FindProperty("_pressureMaxMultiplier").floatValue = pressure;
        serialized.FindProperty("_scoreMultiplier").floatValue = score;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"[SceneSetup] Created difficulty profile: {path}");
        return profile;
    }

    /// <summary>
    /// 敌人定义资产（玩法增强 4.1 的接缝）。M4 只有一个基础追击定义，不存在时用占位精灵创建；
    /// 速度/伤害/碰撞半径用资产字段默认值，换素材只需替换 _sprite。
    /// </summary>
    private static void EnsureEnemyDefinition(Sprite enemySprite)
    {
        EnsureFolder(ConfigFolder);

        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(EnemyDefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            AssetDatabase.CreateAsset(definition, EnemyDefinitionPath);
            Debug.Log($"[SceneSetup] Created enemy definition: {EnemyDefinitionPath}");
        }

        // 每次重建都重指一次精灵：换素材时不用手工改资产。
        SetObjectReference(definition, "_sprite", enemySprite);
        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();
    }

    /// <summary>文件不存在时写入 PNG 并配置为 Sprite；返回 Sprite 资源。</summary>
    private static Sprite EnsureSprite(string path, Texture2D texture, bool repeat)
    {
        try
        {
            if (!File.Exists(path))
            {
                File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                ConfigureSpriteImporter(path, repeat);
                Debug.Log($"[SceneSetup] Created sprite: {path}");
            }
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void ConfigureSpriteImporter(string path, bool repeat)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[SceneSetup] Not a texture: {path}");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = SpritePixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;

        // Tiled 绘制模式要求 Full Rect，否则 SpriteRenderer 会回退成 Simple。
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
    }

    // ---------- 占位贴图（M1 不做美术） ----------

    private static Texture2D CreateGridTexture()
    {
        const int size = 32;
        var background = new Color32(0x1E, 0x1E, 0x2E, 0xFF);
        var line = new Color32(0x2E, 0x2E, 0x48, 0xFF);
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                pixels[y * size + x] = (x == 0 || y == 0) ? line : background;
            }
        }

        return BuildTexture(size, pixels);
    }

    private static Texture2D CreatePlayerTexture()
    {
        const int size = 32;
        var body = new Color32(0x4C, 0xC9, 0xF0, 0xFF);
        var highlight = new Color32(0xBD, 0xEF, 0xFF, 0xFF);
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - 16f;
                float dy = y + 0.5f - 16f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                float hx = x + 0.5f - 12f;
                float hy = y + 0.5f - 20f;
                float highlightDistance = Mathf.Sqrt(hx * hx + hy * hy);

                if (highlightDistance <= 4f)
                {
                    pixels[y * size + x] = highlight;
                }
                else if (distance <= 13.5f)
                {
                    pixels[y * size + x] = body;
                }
                else
                {
                    pixels[y * size + x] = new Color32(0, 0, 0, 0);
                }
            }
        }

        return BuildTexture(size, pixels);
    }

    private static Texture2D CreateWhiteTexture()
    {
        const int size = 4;
        var pixels = new Color32[size * size];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        }

        return BuildTexture(size, pixels);
    }

    private static Texture2D CreateEnemyTexture()
    {
        const int size = 32;
        var body = new Color32(0xF7, 0x25, 0x85, 0xFF);
        var core = new Color32(0xFF, 0x8F, 0xC7, 0xFF);
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - 16f;
                float dy = y + 0.5f - 16f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                if (distance <= 6.5f)
                {
                    pixels[y * size + x] = core;
                }
                else if (distance <= 13.5f)
                {
                    pixels[y * size + x] = body;
                }
                else
                {
                    pixels[y * size + x] = new Color32(0, 0, 0, 0);
                }
            }
        }

        return BuildTexture(size, pixels);
    }

    private static Texture2D BuildTexture(int size, Color32[] pixels)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    // ---------- 工具 ----------

    private static void SetObjectReference(Object target, string fieldName, Object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogError($"[SceneSetup] Field not found: {target.GetType().Name}.{fieldName}");
            return;
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetObjectReferenceArray(Object target, string fieldName, Object[] values)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogError($"[SceneSetup] Field not found: {target.GetType().Name}.{fieldName}");
            return;
        }

        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetSerializedBool(Object target, string fieldName, bool value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogError($"[SceneSetup] Field not found: {target.GetType().Name}.{fieldName}");
            return;
        }

        property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
        Debug.Log($"[SceneSetup] Created folder: {folder}");
    }
}
