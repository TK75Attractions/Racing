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
        if (source.name.Contains("Paint")) { material.SetFloat("_Metallic", .28f); material.SetFloat("_Smoothness", .82f); }
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
