using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One wall overlay with distance/curvature data; never changes tube topology or collision.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class TubeSpeedPatternController : MonoBehaviour
{
    public const string OverlayName = "Tube Speed Light Patterns";
    [SerializeField] private MeshFilter tube;
    [SerializeField] private Material patternMaterial;

    public MeshFilter Tube => tube;
    public Material PatternMaterial => patternMaterial;

    private Mesh overlayMesh;
    private GameObject ownedOverlay;
    private TubePortalVisualController portals;

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
        if (!tube.sharedMesh.isReadable || tube.sharedMesh.vertexCount == 0 ||
            tube.sharedMesh.uv.Length != tube.sharedMesh.vertexCount)
        {
            Debug.LogWarning("Tube wall visuals need a readable tube mesh with authored ring UVs.", this);
            return;
        }
        GameObject overlay = new GameObject(OverlayName, typeof(MeshFilter), typeof(MeshRenderer))
        { hideFlags = HideFlags.DontSave, layer = tube.gameObject.layer };
        ownedOverlay = overlay;
        overlay.transform.SetParent(tube.transform, false);
        overlayMesh = BuildWallMetadata(out Vector3[] centers, out float length, out bool reverse);
        overlay.GetComponent<MeshFilter>().sharedMesh = overlayMesh;
        portals = overlay.AddComponent<TubePortalVisualController>();
        portals.Build(tube, centers, reverse);
        var materials = new Material[tube.sharedMesh.subMeshCount];
        for (int i = 0; i < materials.Length; i++) materials[i] = patternMaterial;
        MeshRenderer renderer = overlay.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        var properties = new MaterialPropertyBlock();
        properties.SetFloat("_TubeLength", length);
        renderer.SetPropertyBlock(properties);
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

    // Same UV ring grouping as TubeCoursePreparation.ReadRings. UV2 stores actual
    // distance, outer-wall emphasis, signed turn and curvature; no vertices are added.
    private Mesh BuildWallMetadata(out Vector3[] centers, out float length, out bool reverse)
    {
        Mesh source = tube.sharedMesh;
        Vector3[] vertices = source.vertices;
        Vector2[] uv = source.uv;
        var groups = new SortedDictionary<int, Dictionary<int, Vector3>>();
        for (int i = 0; i < vertices.Length; i++)
        {
            int key = Mathf.RoundToInt(uv[i].x * 1000000f);
            if (!groups.TryGetValue(key, out var ring)) groups.Add(key, ring = new Dictionary<int, Vector3>());
            if (uv[i].y < .999f) ring[Mathf.RoundToInt(uv[i].y * 1000000f)] = tube.transform.TransformPoint(vertices[i]);
        }
        centers = new Vector3[groups.Count];
        var indices = new Dictionary<int, int>();
        int index = 0;
        foreach (var pair in groups)
        {
            foreach (Vector3 point in pair.Value.Values) centers[index] += point;
            centers[index] /= Mathf.Max(1, pair.Value.Count);
            indices.Add(pair.Key, index++);
        }
        var distances = new float[centers.Length];
        for (int i = 1; i < centers.Length; i++) distances[i] = distances[i - 1] + Vector3.Distance(centers[i - 1], centers[i]);
        length = centers.Length == 0 ? 0f : distances[centers.Length - 1];
        reverse = false;
        RaceCourse course = GetComponent<RaceCourse>();
        if (course != null && centers.Length > 1)
        {
            course.RebuildCache();
            // The checkpoint heading determines race direction, as in LapManager.
            float sign = 1f;
            CheckpointSensor start = null;
            foreach (CheckpointSensor checkpoint in FindObjectsByType<CheckpointSensor>(FindObjectsSortMode.None))
                if (start == null || checkpoint.CheckpointIndex < start.CheckpointIndex ||
                    (checkpoint.CheckpointIndex == start.CheckpointIndex && checkpoint.transform.GetSiblingIndex() < start.transform.GetSiblingIndex()))
                    start = checkpoint;
            if (start != null && course.TryGetNearestCenterLineDirection(start.transform.position, out Vector3 startDirection))
                sign = Vector3.Dot(start.transform.forward, startDirection) < 0f ? -1f : 1f;
            if (course.TryGetNearestCenterLineDirection(centers[0], out Vector3 direction))
                reverse = Vector3.Dot(centers[1] - centers[0], direction * sign) < 0f;
        }
        var metadata = new List<Vector4>(vertices.Length);
        for (int i = 0; i < vertices.Length; i++)
        {
            int r = indices[Mathf.RoundToInt(uv[i].x * 1000000f)];
            int before = Mathf.Max(0, r - 1), after = Mathf.Min(centers.Length - 1, r + 1);
            Vector3 incoming = (centers[r] - centers[before]).normalized;
            Vector3 outgoing = (centers[after] - centers[r]).normalized;
            Vector3 curvature = outgoing - incoming;
            if (before == r || after == r) curvature = Vector3.zero;
            float span = Mathf.Max(1f, (distances[after] - distances[before]) * .5f);
            float bend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.006f, .035f, curvature.magnitude / span));
            Vector3 radial = (tube.transform.TransformPoint(vertices[i]) - centers[r]).normalized;
            float outer = Mathf.Max(0f, Vector3.Dot(radial, -curvature.normalized)) * bend;
            float turn = Mathf.Sign(Vector3.Dot(Vector3.Cross(incoming, outgoing), Vector3.up)) * bend * (reverse ? -1f : 1f);
            metadata.Add(new Vector4(reverse ? length - distances[r] : distances[r], outer, turn, bend));
        }
        Mesh mesh = Instantiate(source);
        mesh.name = "Tube wall distance and curvature (unchanged topology)";
        mesh.hideFlags = HideFlags.DontSave;
        mesh.SetUVs(1, metadata);
        return mesh;
    }

    private void Clear()
    {
        if (portals != null) portals.Release();
        portals = null;
        if (overlayMesh != null)
        {
            if (Application.isPlaying) Destroy(overlayMesh); else DestroyImmediate(overlayMesh);
            overlayMesh = null;
        }
        if (ownedOverlay != null)
        {
            ownedOverlay.SetActive(false);
            if (Application.isPlaying) Destroy(ownedOverlay); else DestroyImmediate(ownedOverlay);
            ownedOverlay = null;
        }
        if (tube == null) return;
        // Find transient children too, so script reloads do not duplicate the overlay.
        for (int i = tube.transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = tube.transform.GetChild(i).gameObject;
            if (child.name != OverlayName || (child.hideFlags & HideFlags.DontSave) != HideFlags.DontSave) continue;
            var portal = child.GetComponent<TubePortalVisualController>();
            if (portal != null) portal.Release();
            child.SetActive(false);
            if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
        }
    }
}
