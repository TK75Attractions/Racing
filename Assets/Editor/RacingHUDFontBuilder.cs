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
