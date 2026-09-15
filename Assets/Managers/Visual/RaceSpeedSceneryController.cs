using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RaceCourseの中心線を利用して、路面の破線と近景マーカーを生成します。
/// 物理コースや判定には影響しない表示専用オブジェクトです。
/// </summary>
[DisallowMultipleComponent]
public sealed class RaceSpeedSceneryController : MonoBehaviour
{
    [SerializeField] private RaceCourse course;
    [SerializeField, Min(4f)] private float dashSpacing = 14f;
    [SerializeField, Min(0.5f)] private float dashLength = 5f;
    [SerializeField, Min(0.01f)] private float dashWidth = 0.18f;
    [SerializeField, Min(4f)] private float markerSpacing = 28f;
    [SerializeField, Min(0.1f)] private float roadsideOffset = 33f;
    [SerializeField, Min(0.1f)] private float markerHeight = 2.2f;
    [SerializeField, Min(0f)] private float surfaceOffset = 0.08f;
    [SerializeField] private Color roadMarkingColor = new Color(1f, 0.72f, 0.18f, 0.95f);
    [SerializeField] private Color roadsideColor = new Color(0.08f, 0.85f, 1f, 0.95f);

    private readonly List<Vector3> path = new List<Vector3>();
    private Transform generatedRoot;
    private Material markingMaterial;
    private Material roadsideMaterial;

    private void Awake()
    {
        if (course == null) course = GetComponent<RaceCourse>();
        Build();
    }

    private void Build()
    {
        if (course == null) return;
        course.CopyCenterPathWorld(path);
        if (path.Count < 2) return;

        generatedRoot = new GameObject("SpeedSceneryGenerated").transform;
        generatedRoot.SetParent(transform, true);
        generatedRoot.position = Vector3.zero;
        generatedRoot.rotation = Quaternion.identity;

        markingMaterial = CreateMaterial("SpeedRoadMarkings", roadMarkingColor);
        roadsideMaterial = CreateMaterial("SpeedRoadsideMarkers", roadsideColor);

        BuildRepeatedObjects(dashSpacing, (position, direction, distance) =>
        {
            CreateBox(
                "RoadSpeedDash",
                position + Vector3.up * surfaceOffset,
                direction,
                new Vector3(dashWidth, 0.025f, dashLength),
                markingMaterial);
        });

        BuildRepeatedObjects(markerSpacing, (position, direction, distance) =>
        {
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                CreateBox(
                    "RoadsideSpeedMarker",
                    position + side * (roadsideOffset * sign) + Vector3.up * (markerHeight * 0.5f),
                    direction,
                    new Vector3(0.18f, markerHeight, 0.18f),
                    roadsideMaterial);
            }
        });
    }

    private void BuildRepeatedObjects(float spacing, System.Action<Vector3, Vector3, float> create)
    {
        float totalDistance = GetPathLength();
        if (totalDistance <= 0f) return;

        for (float distance = 0f; distance < totalDistance; distance += Mathf.Max(1f, spacing))
        {
            SamplePath(distance, out Vector3 position, out Vector3 direction);
            create(position, direction, distance);
        }
    }

    private float GetPathLength()
    {
        float length = 0f;
        for (int index = 1; index < path.Count; index++)
            length += Vector3.Distance(path[index - 1], path[index]);
        return length;
    }

    private void SamplePath(float distance, out Vector3 position, out Vector3 direction)
    {
        float remaining = Mathf.Max(0f, distance);
        for (int index = 1; index < path.Count; index++)
        {
            Vector3 start = path[index - 1];
            Vector3 end = path[index];
            Vector3 delta = end - start;
            float segmentLength = delta.magnitude;
            if (segmentLength <= 0.001f) continue;
            if (remaining <= segmentLength)
            {
                position = Vector3.Lerp(start, end, remaining / segmentLength);
                direction = delta / segmentLength;
                return;
            }
            remaining -= segmentLength;
        }

        position = path[path.Count - 1];
        direction = (path[path.Count - 1] - path[path.Count - 2]).normalized;
    }

    private void CreateBox(string objectName, Vector3 position, Vector3 direction, Vector3 scale, Material material)
    {
        GameObject instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
        instance.name = objectName;
        instance.transform.SetParent(generatedRoot, true);
        instance.transform.SetPositionAndRotation(
            position,
            direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity);
        instance.transform.localScale = scale;
        Collider collider = instance.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        MeshRenderer renderer = instance.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static Material CreateMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Unlit/Color");
        if (shader == null) return null;
        Material material = new Material(shader) { name = materialName };
        material.color = color;
        material.SetColor("_BaseColor", color);
        material.SetColor("_EmissionColor", color * 1.5f);
        material.EnableKeyword("_EMISSION");
        return material;
    }

    private void OnDestroy()
    {
        if (generatedRoot != null) Destroy(generatedRoot.gameObject);
        if (markingMaterial != null) Destroy(markingMaterial);
        if (roadsideMaterial != null) Destroy(roadsideMaterial);
    }
}
