using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit world-only texture import budgets. Does not modify material appearance or UI.</summary>
public static class RaceTextureBudgetBuilder
{
    private sealed class Budget
    {
        public int size, aniso;
        public Budget(int size, int aniso) { this.size = size; this.aniso = aniso; }
    }

    [MenuItem("Racing/Rendering/Apply World Texture Budgets")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var targets = new SortedDictionary<string, Budget>(StringComparer.Ordinal);
        AddFolder(targets, "Assets/RealisticMobileCars - Pro3DModels/RMCar26/Materials", 2048, 4);
        AddFolder(targets, "Assets/SportCar/Models/SportCar_1/Materials", 2048, 4);
        AddFolder(targets, "Assets/SportCar/Models/Wheels", 1024, 4);
        AddFolder(targets, "Assets/RealisticMobileCars - Pro3DModels/Main/Materials", 512, 4);
        Add(targets, "Assets/Resources/RaceCourse/AsphaltAlbedo.png", 2048, 8);
        Add(targets, "Assets/Resources/RaceCourse/AsphaltHeight.png", 1024, 8);
        // Include future imported sign/mark textures only when referenced by these world materials.
        // Current road stripes and NeonRaceMarkers are geometric; there is no mip chain to change.
        foreach (string folder in new[] { "Assets/Prefab/Course", "Assets/Resources/RaceCourse", "Assets/Art/NightEnnichi" })
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (string property in material.GetTexturePropertyNames())
                    Add(targets, AssetDatabase.GetAssetPath(material.GetTexture(property)), 1024, 4);
            }
        }
        int changed = 0;
        foreach (var entry in targets)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(entry.Key);
            string before = EditorJsonUtility.ToJson(importer);
            importer.maxTextureSize = entry.Value.size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.BoxFilter;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = entry.Value.aniso;
            // Existing platform overrides otherwise bypass the default budget on exhibition PCs.
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            standalone.overridden = true;
            standalone.maxTextureSize = entry.Value.size;
            standalone.format = TextureImporterFormat.Automatic;
            standalone.textureCompression = TextureImporterCompression.CompressedHQ;
            standalone.crunchedCompression = false;
            importer.SetPlatformTextureSettings(standalone);
            if (before != EditorJsonUtility.ToJson(importer)) { importer.SaveAndReimport(); changed++; }
            Debug.Log($"WORLD_TEXTURE_BUDGET: {entry.Key}: max={entry.Value.size}, mips, trilinear, aniso={entry.Value.aniso}, HQ compression");
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"WORLD_TEXTURE_BUDGET_COMPLETE: {targets.Count} world textures, {changed} changed; " +
            "normal/sRGB/alpha/wrap settings and other platform overrides retained. Geometric neon/stripes have no texture mipmaps.");
    }

    private static void AddFolder(IDictionary<string, Budget> targets, string folder, int size, int aniso)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            Add(targets, AssetDatabase.GUIDToAssetPath(guid), size, aniso);
    }

    private static void Add(IDictionary<string, Budget> targets, string path, int size, int aniso)
    {
        if (string.IsNullOrEmpty(path) || path.IndexOf("/UI/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            !(AssetImporter.GetAtPath(path) is TextureImporter)) return;
        if (targets.TryGetValue(path, out Budget previous))
        {
            previous.size = Math.Max(previous.size, size);
            previous.aniso = Math.Max(previous.aniso, aniso);
        }
        else targets.Add(path, new Budget(size, aniso));
    }
}
