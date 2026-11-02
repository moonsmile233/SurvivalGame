using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// WebGL 构建入口。菜单 `Survival/Build WebGL` 或 batchmode：
/// Unity.exe -batchmode -quit -projectPath ... -executeMethod BuildScript.BuildWebGL
/// </summary>
public static class BuildScript
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string CompanyName = "Demo";
    private const string ProductName = "SurvivalGame";
    private const string BundleVersion = "1.2.0";

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string WebGLOutputDir => Path.Combine(ProjectRoot, "web", "unity");

    [MenuItem("Survival/Build WebGL")]
    public static void BuildWebGL()
    {
        int exitCode = 0;

        try
        {
            exitCode = RunBuild() ? 0 : 1;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[BuildScript] Build threw: {exception}");
            exitCode = 1;
        }

        // 只有 batchmode 才结束进程；在编辑器里点菜单时不应把编辑器关掉。
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(exitCode);
        }
        else if (exitCode != 0)
        {
            Debug.LogError("[BuildScript] Build FAILED (editor stays open, see log above).");
        }
    }

    private static bool RunBuild()
    {
        // 每次构建都重烤字体图集：改文案后忘记重烤会变成 tofu 方块，这里杜绝该风险。
        if (!FontAssetBuilder.TryRebuild(out string fontError))
        {
            Debug.LogError($"[BuildScript] 字体资产重建失败，已中止构建: {fontError}");
            return false;
        }

        // 场景始终由 SceneSetup 现生成一次：保证「代码即场景」，不会出现场景里残留旧组件/旧引用。
        Debug.Log("[BuildScript] Regenerating scene via SceneSetup.");
        if (!SceneSetup.GenerateScene())
        {
            Debug.LogError("[BuildScript] 场景生成失败，已中止构建。");
            return false;
        }

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        PlayerSettings.companyName = CompanyName;
        PlayerSettings.productName = ProductName;
        PlayerSettings.bundleVersion = BundleVersion;
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;

        PrepareOutputDirectory();

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = WebGLOutputDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };

        Debug.Log($"[BuildScript] Building WebGL to {WebGLOutputDir}");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[BuildScript] Build result: {summary.result}, errors: {summary.totalErrors}");
            return false;
        }

        Debug.Log($"[BuildScript] Build succeeded: {summary.totalSize / (1024 * 1024)} MB, {summary.totalTime}");
        WriteBuildInfo();
        return true;
    }

    private static void PrepareOutputDirectory()
    {
        Directory.CreateDirectory(WebGLOutputDir);

        foreach (string entry in Directory.GetFileSystemEntries(WebGLOutputDir))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    /// <summary>扫描实际产物生成 build-info.json（不同 Unity 版本文件名不同，不能硬编码）。</summary>
    private static void WriteBuildInfo()
    {
        string[] files = Directory.GetFiles(WebGLOutputDir, "*", SearchOption.AllDirectories)
            .Select(path => path.Replace('\\', '/'))
            .Select(path => path.Substring(WebGLOutputDir.Replace('\\', '/').Length).TrimStart('/'))
            .ToArray();

        string loader = Pick(files, ".loader.js", ".loader.js.");
        string data = Pick(files, ".data", ".data.");
        string framework = Pick(files, ".framework.js", ".framework.js.");
        string wasm = Pick(files, ".wasm", ".wasm.");

        if (loader == null || data == null || framework == null || wasm == null)
        {
            throw new InvalidOperationException(
                $"[BuildScript] Cannot locate build products. loader={loader ?? "null"}, data={data ?? "null"}, " +
                $"framework={framework ?? "null"}, wasm={wasm ?? "null"}");
        }

        var info = new BuildInfo
        {
            loaderUrl = loader,
            dataUrl = data,
            frameworkUrl = framework,
            codeUrl = wasm,
            streamingAssetsUrl = Directory.Exists(Path.Combine(WebGLOutputDir, "StreamingAssets"))
                ? "StreamingAssets"
                : string.Empty,
            companyName = PlayerSettings.companyName,
            productName = PlayerSettings.productName,
            productVersion = PlayerSettings.bundleVersion,
            unityVersion = Application.unityVersion,
            builtAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };

        string infoPath = Path.Combine(WebGLOutputDir, "build-info.json");
        File.WriteAllText(infoPath, JsonUtility.ToJson(info, true));
        Debug.Log($"[BuildScript] build-info.json written: {infoPath}");
    }

    /// <summary>优先匹配未压缩文件名，其次匹配带压缩后缀（.br / .gz）的变体。</summary>
    private static string Pick(string[] files, string exactSuffix, string compressedSuffix)
    {
        return files.FirstOrDefault(file => file.EndsWith(exactSuffix, StringComparison.OrdinalIgnoreCase))
               ?? files.FirstOrDefault(file => file.IndexOf(compressedSuffix, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Serializable]
    private class BuildInfo
    {
        public string loaderUrl;
        public string dataUrl;
        public string frameworkUrl;
        public string codeUrl;
        public string streamingAssetsUrl;
        public string companyName;
        public string productName;
        public string productVersion;
        public string unityVersion;
        public string builtAt;
    }
}
