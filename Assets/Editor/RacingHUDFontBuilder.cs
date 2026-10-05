#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>Bakes static M PLUS medium/bold HUD atlases. No runtime font generation is required.</summary>
public static class RacingHUDFontBuilder
{
    [MenuItem("Racing/UI/Build HUD Fonts")]
    public static void Build()
    {
        string directory = "Assets/Resources/UI/HUD";
        Directory.CreateDirectory(directory);
        AssetDatabase.Refresh();
        foreach (string image in new[] { "TitleBackground", "ResultBackground" })
        {
            TextureImporter importer = AssetImporter.GetAtPath("Assets/Resources/UI/Neon/" + image + ".png") as TextureImporter;
            if (importer == null) continue;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        string[] sources = Directory.GetFiles("Assets/Managers/UI", "*.cs", SearchOption.AllDirectories);
        string corpus = new string(Enumerable.Range(32, 95).Select(c => (char)c).ToArray());
        foreach (string source in sources)
            foreach (Match match in Regex.Matches(File.ReadAllText(source), "\"([^\"\\r\\n]*)\""))
                corpus += match.Groups[1].Value;
        string characters = new string(corpus.Where(c => c >= 32).Distinct().OrderBy(c => c).ToArray());
        foreach (string weight in new[] { "Medium", "Bold" })
        {
            string path = directory + "/MPLUS HUD " + weight + ".asset";
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null)
            {
                existing.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                if (!existing.TryAddCharacters(characters, out string missingExisting))
                    throw new InvalidOperationException("HUD atlas is missing: " + missingExisting);
                existing.atlasPopulationMode = AtlasPopulationMode.Static;
                EditorUtility.SetDirty(existing);
                foreach (Texture2D atlas in existing.atlasTextures) EditorUtility.SetDirty(atlas);
                continue;
            }
            Font source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/MPLUS1-HUD-" + weight + ".ttf");
            if (source == null) throw new InvalidOperationException("Missing static HUD font: " + weight);
            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source, 80, 8, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
            font.name = "MPLUS HUD " + weight;
            if (!font.TryAddCharacters(characters, out string missing))
                throw new InvalidOperationException("HUD atlas is missing: " + missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.material.name = font.name + " Material";
            font.material.SetFloat(ShaderUtilities.ID_FaceDilate, 0f);
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (Texture2D atlas in font.atlasTextures)
            {
                atlas.name = font.name + " Atlas";
                AssetDatabase.AddObjectToAsset(atlas, font);
            }
            EditorUtility.SetDirty(font);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("HUD_FONT_BUILD_PASS: static medium and bold, " + characters.Length + " characters.");
    }
}
#endif
