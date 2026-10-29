using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Owns URP-compatible copies of the sedan's legacy surface materials.</summary>
public sealed class CarSelectionMaterialSet : MonoBehaviour
{
    private readonly Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
    public Material Convert(Material source)
    {
        if (source == null || source.shader == null || source.shader.name.StartsWith("Universal Render Pipeline/") ||
            GraphicsSettings.defaultRenderPipeline == null && QualitySettings.renderPipeline == null) return source;
        if (copies.TryGetValue(source, out Material existing)) return existing;
        Material material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = source.name + " (URP Display)" };
        Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
        material.SetColor("_BaseColor", color);
        CopyTexture(source, material, "_MainTex", "_BaseMap");
        CopyTexture(source, material, "_BumpMap", "_BumpMap");
        CopyTexture(source, material, "_MetallicGlossMap", "_MetallicGlossMap");
        CopyTexture(source, material, "_EmissionMap", "_EmissionMap");
        if (source.HasProperty("_Metallic")) material.SetFloat("_Metallic", source.GetFloat("_Metallic"));
        if (source.HasProperty("_Glossiness")) material.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
        if (source.HasProperty("_EmissionColor"))
        {
            Color emission = source.GetColor("_EmissionColor"); material.SetColor("_EmissionColor", emission);
            if (emission.maxColorComponent > .001f) material.EnableKeyword("_EMISSION");
        }
        ApplySurfaceTuning(material, source.name);
        if (color.a < .99f)
        {
            material.SetFloat("_Surface", 1f); material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = (int)RenderQueue.Transparent;
        }
        copies.Add(source, material);
        return material;
    }
    public static void ApplySurfaceTuning(Material material, string materialName)
    {
        if (material == null || string.IsNullOrEmpty(materialName)) return;
        string name = materialName.Replace(" (Instance)", string.Empty);
        float metallic, smoothness;
        Color? tint = null;
        if (name.StartsWith("Body1", StringComparison.OrdinalIgnoreCase) || name.IndexOf("Paint", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("RMCar26_Body", StringComparison.OrdinalIgnoreCase) >= 0)
        { metallic = .32f; smoothness = .88f; }
        else if (name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0)
        { metallic = .18f; smoothness = .96f; tint = new Color(.12f, .18f, .24f, 1f); }
        else if (name.IndexOf("rubber", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("tire", StringComparison.OrdinalIgnoreCase) >= 0)
        { metallic = 0f; smoothness = .2f; tint = new Color(.075f, .075f, .08f, 1f); }
        else if (name.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("ring", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("metal", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("exhaust", StringComparison.OrdinalIgnoreCase) >= 0)
        { metallic = .92f; smoothness = .78f; }
        else if (name.IndexOf("black", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("bottom", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0)
        { metallic = .05f; smoothness = .3f; tint = new Color(.09f, .095f, .105f, 1f); }
        else return;
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
        if (tint.HasValue)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint.Value);
            if (material.HasProperty("_Color")) material.SetColor("_Color", tint.Value);
        }
    }
    private static void CopyTexture(Material source, Material target, string from, string to)
    {
        if (!source.HasProperty(from) || source.GetTexture(from) == null) return;
        target.SetTexture(to, source.GetTexture(from)); target.SetTextureScale(to, source.GetTextureScale(from));
        target.SetTextureOffset(to, source.GetTextureOffset(from));
        if (to == "_BumpMap") target.EnableKeyword("_NORMALMAP");
        if (to == "_MetallicGlossMap") target.EnableKeyword("_METALLICSPECGLOSSMAP");
    }
    private void OnDestroy()
    {
        foreach (Material material in copies.Values)
            if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
    }
}
