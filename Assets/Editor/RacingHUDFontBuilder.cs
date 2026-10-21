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
        // The dropdown's small arrow belongs to FontRole.English; M PLUS has no such glyph.
        string characters = new string(corpus.Where(c => c >= 32 && c != '▾').Distinct().OrderBy(c => c).ToArray());
        foreach (string weight in new[] { "Medium", "Bold", "Tutorial" })
        {
            string path = directory + (weight == "Tutorial" ? "/Tutorial ExtraBold.asset" : "/MPLUS HUD " + weight + ".asset");
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (Environment.GetEnvironmentVariable("RACING_REBUILD_HUD_FONTS") != "1" && existing != null && existing.material != null && existing.atlasTextures.All(t => t != null) && existing.HasCharacters(characters)) continue;
            Font source = AssetDatabase.LoadAssetAtPath<Font>(weight == "Tutorial" ? "Assets/Resources/UI/TutorialBold.ttf" : "Assets/Fonts/MPLUS1-HUD-" + weight + ".ttf");
            if (source == null) throw new InvalidOperationException("Missing static HUD font: " + weight);
            TMP_FontAsset font = null;
            string missing = "";
            // Keep one 2048 atlas; a growing UI corpus may need slightly smaller source glyphs.
            // SDF text still uses the same on-screen size. Do not silently omit late-sorted kanji.
            for (int pointSize = weight == "Tutorial" ? 90 : 80; pointSize >= 64; pointSize -= 4)
            {
                font = TMP_FontAsset.CreateFontAsset(source, pointSize, weight == "Tutorial" ? 12 : 8, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
                if (font.TryAddCharacters(characters, out missing)) break;
                UnityEngine.Object.DestroyImmediate(font);
                font = null;
            }
            if (font == null) throw new InvalidOperationException("HUD atlas is missing: " + missing);
            font.name = weight == "Tutorial" ? "Tutorial ExtraBold" : "MPLUS HUD " + weight;
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.material.name = font.name + " Material";
            font.material.SetFloat(ShaderUtilities.ID_FaceDilate, 0f);
            if (existing == null) AssetDatabase.CreateAsset(font, path);
            else
            {
                foreach (UnityEngine.Object child in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (child != existing) UnityEngine.Object.DestroyImmediate(child, true);
                EditorUtility.CopySerialized(font, existing);
                // Ownership moves to the persistent asset. TMP.OnDestroy would otherwise try to
                // destroy those persistent textures/material when the transient font is unloaded.
                font.atlasTextures = Array.Empty<Texture2D>();
                font.material = null;
                UnityEngine.Object.DestroyImmediate(font);
                font = existing;
            }
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (Texture2D atlas in font.atlasTextures)
            {
                atlas.name = font.name + " Atlas";
                AssetDatabase.AddObjectToAsset(atlas, font);
            }
            // CopySerialized replaces tables but not TMP's cached character lookup in this editor session.
            font.ReadFontAssetDefinition();
            EditorUtility.SetDirty(font);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("HUD_FONT_BUILD_PASS: static medium and bold, " + characters.Length + " characters.");
    }
}
#endif
