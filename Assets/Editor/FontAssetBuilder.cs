using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Survival.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 用脚本重建 TMP 字体资产，替代手动点 Font Asset Creator（ticket M2 第 0 节）。
/// 字符来源 = Assets/Fonts/ui-chars.txt ∪ UiStrings.AllCharacters，
/// 所以改文案时只要同步 UiStrings / ui-chars.txt，重新生成即可，不用再点 GUI。
///
/// 入口：菜单 `Survival/Rebuild Font Asset`；`SceneSetup` 缺资产时自动调用；`BuildScript` 构建前自动重烤。
/// </summary>
public static class FontAssetBuilder
{
    public const string SourceFontPath = "Assets/Fonts/NotoSansSC-UI-Subset.otf";
    public const string CharSetPath = "Assets/Fonts/ui-chars.txt";
    public const string OutputPath = "Assets/Fonts/NotoSansSC-UI-SDF.asset";

    // 参数与 ticket 一致：Sampling 64 / Padding 8 / SDFAA / Static / 1024x1024。
    private const int SamplingPointSize = 64;
    private const int Padding = 8;
    private const int AtlasWidth = 1024;
    private const int AtlasHeight = 1024;

    [MenuItem("Survival/Rebuild Font Asset")]
    public static void RebuildFontAsset()
    {
        if (TryRebuild(out string error))
        {
            Debug.Log("[FontAssetBuilder] 字体资产重建完成。");
        }
        else
        {
            Debug.LogError($"[FontAssetBuilder] 字体资产重建失败: {error}");
        }
    }

    /// <summary>资产不存在时才重建。</summary>
    public static bool EnsureFontAsset()
    {
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath) != null)
        {
            return true;
        }

        Debug.Log($"[FontAssetBuilder] {OutputPath} 不存在，自动生成。");
        return TryRebuild(out _);
    }

    /// <summary>无条件重建（构建前调用，保证图集与当前文案一致）。</summary>
    public static bool TryRebuild(out string error)
    {
        error = null;

        var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (sourceFont == null)
        {
            error = $"找不到源字体 {SourceFontPath}";
            return false;
        }

        string characters = CollectCharacters();
        if (characters.Length == 0)
        {
            error = "字符集为空（ui-chars.txt 与 UiStrings 都没有内容）";
            return false;
        }

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            SamplingPointSize,
            Padding,
            GlyphRenderMode.SDFAA,
            AtlasWidth,
            AtlasHeight,
            AtlasPopulationMode.Dynamic,
            false);

        if (fontAsset == null)
        {
            error = "TMP_FontAsset.CreateFontAsset 返回 null（检查 TMP Essentials 是否已导入）";
            return false;
        }

        if (!fontAsset.TryAddCharacters(characters, out string missing))
        {
            error = string.IsNullOrEmpty(missing)
                ? "TMP TryAddCharacters 失败（未返回缺字列表）"
                : $"字体文件缺少字符，无法烘焙: {missing}";
            return false;
        }

        // 烘焙完再切成 Static：静态图集不会在运行时扩容，WebGL 下行为可预期。
        fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
        fontAsset.name = Path.GetFileNameWithoutExtension(OutputPath);

        SaveAsset(fontAsset);

        List<char> tofu = new List<char>();
        foreach (char c in UiStrings.AllCharacters)
        {
            if (!fontAsset.HasCharacter(c))
            {
                tofu.Add(c);
            }
        }

        if (tofu.Count > 0)
        {
            error = $"以下字符没有进图集，UI 上会显示成方块: {new string(tofu.ToArray())}";
            return false;
        }

        Debug.Log($"[FontAssetBuilder] {OutputPath} 已生成：{characters.Length} 字符，" +
                  $"{AtlasWidth}x{AtlasHeight}，Sampling {SamplingPointSize}，Padding {Padding}。");
        return true;
    }

    private static void SaveAsset(TMP_FontAsset fontAsset)
    {
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath) != null)
        {
            AssetDatabase.DeleteAsset(OutputPath);
        }

        AssetDatabase.CreateAsset(fontAsset, OutputPath);

        Texture2D atlas = fontAsset.atlasTextures[0];
        atlas.name = fontAsset.name + " Atlas";
        atlas.hideFlags = HideFlags.None;
        AssetDatabase.AddObjectToAsset(atlas, fontAsset);

        Material material = fontAsset.material;
        material.name = fontAsset.name + " Material";
        material.hideFlags = HideFlags.None;
        AssetDatabase.AddObjectToAsset(material, fontAsset);

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
    }

    /// <summary>ui-chars.txt 里的全部非控制字符 ∪ UiStrings.AllCharacters。</summary>
    private static string CollectCharacters()
    {
        var characters = new HashSet<char>();

        if (File.Exists(CharSetPath))
        {
            foreach (char c in File.ReadAllText(CharSetPath, Encoding.UTF8))
            {
                if (!char.IsControl(c))
                {
                    characters.Add(c);
                }
            }
        }
        else
        {
            Debug.LogWarning($"[FontAssetBuilder] 缺少 {CharSetPath}，只烘焙 UiStrings 里出现的字符。");
        }

        foreach (char c in UiStrings.AllCharacters)
        {
            characters.Add(c);
        }

        characters.Add(' ');
        return new string(characters.OrderBy(c => c).ToArray());
    }
}
