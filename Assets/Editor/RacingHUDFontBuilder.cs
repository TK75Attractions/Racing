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
        foreach (string image in new[] { "Assets/Resources/UI/Neon/TitleBackground.png", "Assets/Resources/UI/Neon/ResultBackground.png", "Assets/Resources/UI/Neon/MedalGold.png", "Assets/Resources/UI/Neon/MedalSilver.png", "Assets/Editor/ReferenceArt/RaceBackdrop.png", "Assets/Resources/UI/TutorialReferenceDecorated.png" })
        {
            TextureImporter importer = AssetImporter.GetAtPath(image) as TextureImporter;
            if (importer == null) continue;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
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
        foreach (string weight in new[] { "Medium", "Bold", "Tutorial" })
        {
            string path = directory + (weight == "Tutorial" ? "/Tutorial ExtraBold.asset" : "/MPLUS HUD " + weight + ".asset");
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null && existing.material != null && existing.atlasTextures.All(t => t != null) && existing.HasCharacters(characters)) continue;
            Font source = AssetDatabase.LoadAssetAtPath<Font>(weight == "Tutorial" ? "Assets/Resources/UI/TutorialBold.ttf" : "Assets/Fonts/MPLUS1-HUD-" + weight + ".ttf");
            if (source == null) throw new InvalidOperationException("Missing static HUD font: " + weight);
            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source, weight == "Tutorial" ? 90 : 80, weight == "Tutorial" ? 12 : 8, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
            font.name = weight == "Tutorial" ? "Tutorial ExtraBold" : "MPLUS HUD " + weight;
            if (!font.TryAddCharacters(characters, out string missing))
                throw new InvalidOperationException("HUD atlas is missing: " + missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.material.name = font.name + " Material";
            font.material.SetFloat(ShaderUtilities.ID_FaceDilate, 0f);
            if (existing == null) AssetDatabase.CreateAsset(font, path);
            else
            {
                foreach (UnityEngine.Object child in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (child != existing) UnityEngine.Object.DestroyImmediate(child, true);
                EditorUtility.CopySerialized(font, existing);
                // The temporary TMP asset owns its atlas/material; retain it until editor teardown.
                font = existing;
            }
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
