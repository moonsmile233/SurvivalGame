using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键重建 Assets/Scenes/Game.unity。场景是唯一静态结构来源，
/// 不允许只靠手工拖拽（见 CLAUDE.md 硬约束 2）。
/// </summary>
public static class SceneSetup
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string SceneFolder = "Assets/Scenes";

    [MenuItem("Survival/Setup Scene")]
    public static void GenerateScene()
    {
        EnsureFolder(SceneFolder);

        // 当前场景有未保存改动时先存盘，避免 NewScene 弹保存对话框卡住自动化。
        var active = SceneManager.GetActiveScene();
        if (active.isDirty && !string.IsNullOrEmpty(active.path))
        {
            EditorSceneManager.SaveScene(active);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateMainCamera();
        CreateBridge();
        CreateBootstrap();

        EditorSceneManager.SaveScene(scene, ScenePath);

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        Debug.Log($"[SceneSetup] Scene saved: {Path.GetFullPath(ScenePath)}");
    }

    private static void CreateMainCamera()
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
