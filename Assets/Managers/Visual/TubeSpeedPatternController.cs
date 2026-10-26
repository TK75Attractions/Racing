using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Layers emissive patterns on the authored or prepared tube; owns no mesh or collider.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class TubeSpeedPatternController : MonoBehaviour
{
    public const string OverlayName = "Tube Speed Light Patterns";
    [SerializeField] private MeshFilter tube;
    [SerializeField] private Material patternMaterial;

    private Material smoothPatternMaterial;
    public MeshFilter Tube => tube;
    public Material PatternMaterial => patternMaterial;

    private void OnEnable() => Rebuild();

    private void OnValidate()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= RefreshAfterValidation;
        UnityEditor.EditorApplication.delayCall += RefreshAfterValidation;
#endif
    }

#if UNITY_EDITOR
    private void RefreshAfterValidation()
    {
        if (this != null && isActiveAndEnabled) Rebuild();
    }
#endif

    public void Rebuild()
    {
        Clear();
        if (!isActiveAndEnabled || tube == null || tube.sharedMesh == null || patternMaterial == null) return;
        GameObject overlay = new GameObject(OverlayName, typeof(MeshFilter), typeof(MeshRenderer))
        { hideFlags = HideFlags.DontSave, layer = tube.gameObject.layer };
        overlay.transform.SetParent(tube.transform, false);
        overlay.GetComponent<MeshFilter>().sharedMesh = tube.sharedMesh;
        Material display = patternMaterial;
        if (tube.sharedMesh.name == TubeSurfaceGeometry.SmoothMeshName)
        {
            smoothPatternMaterial = new Material(patternMaterial) { name = "Smooth tube light patterns", hideFlags = HideFlags.DontSave };
            smoothPatternMaterial.SetFloat("_CullMode", (float)CullMode.Back);
            smoothPatternMaterial.SetFloat("_NormalDirection", 1f);
            display = smoothPatternMaterial;
        }
        var materials = new Material[tube.sharedMesh.subMeshCount];
        for (int i = 0; i < materials.Length; i++) materials[i] = display;
        MeshRenderer renderer = overlay.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= RefreshAfterValidation;
#endif
        Clear();
    }

    private void Clear()
    {
        if (smoothPatternMaterial != null)
        {
            if (Application.isPlaying) Destroy(smoothPatternMaterial); else DestroyImmediate(smoothPatternMaterial);
            smoothPatternMaterial = null;
        }
        if (tube == null) return;
        // Find transient children too, so script reloads do not duplicate the overlay.
        for (int i = tube.transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = tube.transform.GetChild(i).gameObject;
            if (child.name != OverlayName || (child.hideFlags & HideFlags.DontSave) != HideFlags.DontSave) continue;
            child.SetActive(false);
            if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
        }
    }
}
