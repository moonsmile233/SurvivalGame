using System.IO;
using Survival.Core;
using Survival.Enemy;
using Survival.Player;
using Survival.UI;
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
        Sprite backgroundSprite = EnsureSprite(BackgroundSpritePath, CreateGridTexture(), true);
        Sprite playerSprite = EnsureSprite(PlayerSpritePath, CreatePlayerTexture(), false);
        Sprite enemySprite = EnsureSprite(EnemySpritePath, CreateEnemyTexture(), false);
        EnsureSprite(UiSpritePath, CreateWhiteTexture(), false);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // NewScene 会卸载未被场景引用的资源（GameConfig 会被判为未使用），必须重新取一次资产引用。
        config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
        backgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundSpritePath);
        playerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlayerSpritePath);
        enemySprite = AssetDatabase.LoadAssetAtPath<Sprite>(EnemySpritePath);
        s_uiSprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiSpritePath);
        s_uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

        Camera cam = CreateMainCamera();
        CreateBackground(backgroundSprite);

        GameObject player = CreatePlayer(config, playerSprite, cam);
        PlayerController playerController = player.GetComponent<PlayerController>();
        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();

        EnemySpawner spawner = CreateEnemySpawner(config, player.transform, playerHealth, cam, enemySprite);
        GameManager manager = CreateGameManager(config, playerController, playerHealth, spawner);

        // Ready 状态禁止移动：PlayerController 需要读 GameManager.State（M3 第 1.2 节）。
        SetObjectReference(playerController, "_gameManager", manager);

        CreateHud(manager, playerHealth);

        WebGLBridge bridge = CreateBridge(manager);
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

    private static void CreateBackground(Sprite sprite)
    {
        var go = new GameObject("Background");
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.tileMode = SpriteTileMode.Continuous;
        renderer.size = new Vector2(60f, 30f);
        renderer.sortingOrder = -10;
    }

    private static GameObject CreatePlayer(GameConfig config, Sprite sprite, Camera camera)
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

        var input = go.AddComponent<PlayerInputController>();
        var controller = go.AddComponent<PlayerController>();
        var health = go.AddComponent<PlayerHealth>();
        var view = go.AddComponent<PlayerView>();

        SetObjectReference(controller, "_config", config);
        SetObjectReference(controller, "_input", input);
        SetObjectReference(controller, "_camera", camera);
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
        Sprite enemySprite)
    {
        var go = new GameObject("EnemySpawner");
        var spawner = go.AddComponent<EnemySpawner>();

        SetObjectReference(spawner, "_config", config);
        SetObjectReference(spawner, "_playerTarget", playerTarget);
        SetObjectReference(spawner, "_playerHealth", playerHealth);
        SetObjectReference(spawner, "_camera", camera);
        SetObjectReference(spawner, "_enemySprite", enemySprite);

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

        // M3 起由 H5 的 StartGame 驱动，不再 Play 即开局（docs/遗留问题.md 第 2 条）。
        SetSerializedBool(manager, "_autoStart", false);

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

    private static void CreateHud(GameManager manager, PlayerHealth playerHealth)
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

        var canvasRect = (RectTransform)canvasGo.transform;

        // ---- 常驻 HUD ----
        RectTransform hud = CreateRect("HUD", canvasRect);
        Stretch(hud);

        Image hpBarBg = CreateImage("HPBar", hud, new Color(0.10f, 0.10f, 0.16f, 0.85f));
        SetAnchored(hpBarBg.rectTransform, TopLeft, TopLeft, new Vector2(24f, -24f), new Vector2(240f, 20f));

        Image hpFill = CreateImage("Fill", hpBarBg.transform, new Color(0.30f, 0.85f, 0.45f, 1f));
        Stretch(hpFill.rectTransform, 2f);
        hpFill.type = Image.Type.Filled;
        hpFill.fillMethod = Image.FillMethod.Horizontal;
        hpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        hpFill.fillAmount = 1f;

        TMP_Text hpText = CreateText("HPText", hud, $"{UiStrings.HealthLabel} 100/100", 24f,
            TextAlignmentOptions.TopLeft);
        SetAnchored(hpText.rectTransform, TopLeft, TopLeft, new Vector2(24f, -50f), new Vector2(260f, 32f));

        TMP_Text timeText = CreateText("TimeText", hud, $"{UiStrings.TimeLabel} 00:60", 36f,
            TextAlignmentOptions.Top, true);
        SetAnchored(timeText.rectTransform, TopCenter, TopCenter, new Vector2(0f, -20f), new Vector2(320f, 48f));

        TMP_Text scoreText = CreateText("ScoreText", hud, $"{UiStrings.ScoreLabel} 0", 28f,
            TextAlignmentOptions.TopRight, true);
        SetAnchored(scoreText.rectTransform, TopRight, TopRight, new Vector2(-160f, -22f), new Vector2(260f, 40f));

        Button pauseButton = CreateButton("PauseButton", hud, UiStrings.PauseButton, TopRight, TopRight,
            new Vector2(-24f, -24f), new Vector2(120f, 48f), 24f);

        // ---- 暂停面板（默认隐藏） ----
        RectTransform pausePanel = CreatePanel("PausePanel", canvasRect);
        CreateDim(pausePanel);

        TMP_Text pauseTitle = CreateText("Title", pausePanel, UiStrings.PausedTitle, 64f,
            TextAlignmentOptions.Center, true);
        SetAnchored(pauseTitle.rectTransform, Center, Center, new Vector2(0f, 100f), new Vector2(700f, 96f));

        Button resumeButton = CreateButton("ResumeButton", pausePanel, UiStrings.ResumeButton, Center, Center,
            new Vector2(0f, -60f), new Vector2(220f, 56f), 28f);

        // ---- 结算面板（默认隐藏） ----
        RectTransform resultPanel = CreatePanel("ResultPanel", canvasRect);
        CreateDim(resultPanel);

        TMP_Text resultTitle = CreateText("TitleText", resultPanel, UiStrings.VictoryTitle, 72f,
            TextAlignmentOptions.Center, true);
        SetAnchored(resultTitle.rectTransform, Center, Center, new Vector2(0f, 160f), new Vector2(700f, 110f));

        TMP_Text resultScore = CreateText("ScoreText", resultPanel, $"{UiStrings.FinalScoreLabel} 0", 40f,
            TextAlignmentOptions.Center, true);
        SetAnchored(resultScore.rectTransform, Center, Center, new Vector2(0f, 50f), new Vector2(600f, 60f));

        TMP_Text resultBreakdown = CreateText("BreakdownText", resultPanel, $"{UiStrings.SurvivalLabel} 0", 28f,
            TextAlignmentOptions.Top);
        SetAnchored(resultBreakdown.rectTransform, Center, Center, new Vector2(0f, -70f), new Vector2(600f, 150f));

        Button restartButton = CreateButton("RestartButton", resultPanel, UiStrings.RestartButton, Center, Center,
            new Vector2(0f, -220f), new Vector2(260f, 60f), 28f);

        pausePanel.gameObject.SetActive(false);
        resultPanel.gameObject.SetActive(false);

        // ---- 注入引用（控制器挂在常驻的 Canvas 上，面板自身可以 inactive） ----
        var hudView = canvasGo.AddComponent<HudView>();
        SetObjectReference(hudView, "_gameManager", manager);
        SetObjectReference(hudView, "_playerHealth", playerHealth);
        SetObjectReference(hudView, "_hpFill", hpFill);
        SetObjectReference(hudView, "_hpText", hpText);
        SetObjectReference(hudView, "_timeText", timeText);
        SetObjectReference(hudView, "_scoreText", scoreText);
        SetObjectReference(hudView, "_pauseButton", pauseButton);

        var pauseController = canvasGo.AddComponent<PausePanel>();
        SetObjectReference(pauseController, "_gameManager", manager);
        SetObjectReference(pauseController, "_panel", pausePanel.gameObject);
        SetObjectReference(pauseController, "_resumeButton", resumeButton);

        var resultController = canvasGo.AddComponent<ResultPanel>();
        SetObjectReference(resultController, "_gameManager", manager);
        SetObjectReference(resultController, "_panel", resultPanel.gameObject);
        SetObjectReference(resultController, "_titleText", resultTitle);
        SetObjectReference(resultController, "_scoreText", resultScore);
        SetObjectReference(resultController, "_breakdownText", resultBreakdown);
        SetObjectReference(resultController, "_restartButton", restartButton);
    }

    private static RectTransform CreatePanel(string name, Transform parent)
    {
        RectTransform rect = CreateRect(name, parent);
        Stretch(rect);
        return rect;
    }

    private static void CreateDim(Transform parent)
    {
        Image dim = CreateImage("Dim", parent, new Color(0f, 0f, 0f, 0.6f));
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
