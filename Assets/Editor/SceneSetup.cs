using System.IO;
using Survival.Core;
using Survival.Enemy;
using Survival.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    private const float SpritePixelsPerUnit = 32f;

    [MenuItem("Survival/Setup Scene")]
    public static void GenerateScene()
    {
        EnsureFolder(SceneFolder);
        EnsureFolder(SpriteFolder);

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

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // NewScene 会卸载未被场景引用的资源（GameConfig 会被判为未使用），必须重新取一次资产引用。
        config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
        backgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundSpritePath);
        playerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlayerSpritePath);
        enemySprite = AssetDatabase.LoadAssetAtPath<Sprite>(EnemySpritePath);

        Camera cam = CreateMainCamera();
        CreateBackground(backgroundSprite);

        GameObject player = CreatePlayer(config, playerSprite, cam);
        PlayerController playerController = player.GetComponent<PlayerController>();
        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();

        EnemySpawner spawner = CreateEnemySpawner(config, player.transform, playerHealth, cam, enemySprite);
        CreateGameManager(config, playerController, playerHealth, spawner);

        CreateBridge();
        CreateBootstrap();

        EditorSceneManager.SaveScene(scene, ScenePath);

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        Debug.Log($"[SceneSetup] Scene saved: {Path.GetFullPath(ScenePath)}");
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

        SetObjectReference(controller, "_config", config);
        SetObjectReference(controller, "_input", input);
        SetObjectReference(controller, "_camera", camera);
        SetObjectReference(health, "_config", config);

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

    private static void CreateGameManager(
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
        SetSerializedBool(manager, "_autoStart", true);
    }

    private static void CreateBridge()
    {
        var go = new GameObject("WebGLBridge");
        go.AddComponent<WebGLBridge>();
    }

    private static void CreateBootstrap()
    {
        var go = new GameObject("Bootstrap");
        go.AddComponent<GameBootstrap>();
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
