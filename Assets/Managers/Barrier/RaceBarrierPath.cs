using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 開発者がSceneビューで自由に配置する、片側分のレースバリア経路です。
/// RaceCourseのウェイポイントや幅には依存せず、制御点から表示メッシュと物理Colliderを生成します。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Racing/Race Barrier Path")]
public sealed class RaceBarrierPath : MonoBehaviour
{
    [Serializable]
    public sealed class ControlPoint
    {
        public Vector3 localPosition;

        [Min(0.1f)]
        public float wallHeight = 2.5f;

        [Min(0f)]
        [Tooltip("壁の上端を経路の外側へ広げる距離です。Colliderはこの値を使わず、ほぼ垂直に生成します。")]
        public float outwardFlare = 1.5f;
    }

    [Header("手動配置")]
    [SerializeField] private List<ControlPoint> controlPoints = new List<ControlPoint>();
    [SerializeField] private bool closedLoop;
    [SerializeField] private bool useCurves = true;
    [SerializeField, Range(2, 32)] private int samplesPerSegment = 8;

    [Header("断面")]
    [SerializeField, Range(2, 8)] private int wallSectionSegments = 4;
    [SerializeField, Range(-1, 1)] private int outwardSign = 1;

    [Header("Collider")]
    [SerializeField, Min(0.05f)] private float colliderThickness = 0.6f;
    [SerializeField] private PhysicsMaterial colliderMaterial;

    [Header("生成")]
    [SerializeField] private bool generateVisual = true;
    [SerializeField] private bool generateCollider = true;
    [SerializeField] private bool generateInEditMode = true;
    [SerializeField, Min(0.1f)] private float uvMetersPerRepeat = 2f;
    [SerializeField] private Material visualMaterial;

    private const string GeneratedRootName = "Generated";
    private const string VisualRootName = "WallVisual";
    private const string ColliderRootName = "WallCollider";

    private readonly List<Sample> samples = new List<Sample>();
    private Transform generatedRoot;
    private Transform visualRoot;
    private Transform colliderRoot;
    private Mesh generatedMesh;

    private struct Sample
    {
        public Vector3 position;
        public float height;
        public float outwardFlare;
    }

    public int ControlPointCount => controlPoints != null ? controlPoints.Count : 0;
    public bool ClosedLoop => closedLoop;

    private void OnEnable()
    {
        if (Application.isPlaying || generateInEditMode)
        {
            Rebuild();
        }
    }

    private void OnValidate()
    {
        samplesPerSegment = Mathf.Clamp(samplesPerSegment, 2, 32);
        wallSectionSegments = Mathf.Clamp(wallSectionSegments, 2, 8);
        outwardSign = outwardSign < 0 ? -1 : 1;
        colliderThickness = Mathf.Max(0.05f, colliderThickness);
        uvMetersPerRepeat = Mathf.Max(0.1f, uvMetersPerRepeat);

        if (!Application.isPlaying && generateInEditMode)
        {
            Rebuild();
        }
    }

    /// <summary>制御点からVisualとColliderを再生成します。</summary>
    public void Rebuild()
    {
        CleanupGeneratedObjects();

        if (controlPoints == null || controlPoints.Count < 2)
        {
            return;
        }

        BuildSamples(samples);
        if (samples.Count < 2)
        {
            return;
        }

        EnsureGeneratedRoots();

        if (generateVisual)
        {
            BuildVisualMesh();
        }

        if (generateCollider)
        {
            BuildColliderChain();
        }
    }

    /// <summary>Sceneビューのプレビューや他ツール向けに、制御点から生成した基底経路を返します。</summary>
    public void CopyPreviewPathWorld(List<Vector3> destination)
    {
        if (destination == null)
        {
            return;
        }

        destination.Clear();
        if (controlPoints == null || controlPoints.Count < 2)
        {
            return;
        }

        BuildSamples(samples);
        for (int i = 0; i < samples.Count; i++)
        {
            destination.Add(transform.TransformPoint(samples[i].position));
        }
    }

    public void AddControlPoint()
    {
        if (controlPoints == null)
        {
            controlPoints = new List<ControlPoint>();
        }

        Vector3 position = controlPoints.Count == 0
            ? Vector3.zero
            : controlPoints[controlPoints.Count - 1].localPosition + Vector3.forward * 10f;

        controlPoints.Add(new ControlPoint { localPosition = position });
    }

    public void RemoveLastControlPoint()
    {
        if (controlPoints == null || controlPoints.Count == 0)
        {
            return;
        }

        controlPoints.RemoveAt(controlPoints.Count - 1);
    }

    private void BuildSamples(List<Sample> destination)
    {
        destination.Clear();
        int pointCount = controlPoints.Count;
        int segmentCount = closedLoop ? pointCount : pointCount - 1;
        int sampleCount = useCurves ? Mathf.Max(2, samplesPerSegment) : 1;

        for (int segment = 0; segment < segmentCount; segment++)
        {
            int next = closedLoop ? (segment + 1) % pointCount : segment + 1;
            for (int step = 0; step <= sampleCount; step++)
            {
                if (segment > 0 && step == 0)
                {
                    continue;
                }

                if (closedLoop && segment == segmentCount - 1 && step == sampleCount)
                {
                    // 閉ループの終点は先頭と同じ点を1つだけ残して、メッシュの継ぎ目を閉じます。
                }

                float t = step / (float)sampleCount;
                Vector3 position = useCurves
                    ? EvaluateCatmullRom(segment, t)
                    : Vector3.Lerp(controlPoints[segment].localPosition, controlPoints[next].localPosition, t);

                destination.Add(new Sample
                {
                    position = position,
                    height = Mathf.Lerp(controlPoints[segment].wallHeight, controlPoints[next].wallHeight, t),
                    outwardFlare = Mathf.Lerp(controlPoints[segment].outwardFlare, controlPoints[next].outwardFlare, t)
                });
            }
        }
    }

    private Vector3 EvaluateCatmullRom(int segment, float t)
    {
        int count = controlPoints.Count;
        int p1Index = segment;
        int p2Index = closedLoop ? (segment + 1) % count : segment + 1;
        int p0Index = closedLoop ? (segment - 1 + count) % count : Mathf.Max(0, segment - 1);
        int p3Index = closedLoop ? (segment + 2) % count : Mathf.Min(count - 1, segment + 2);

        return CatmullRom(
            controlPoints[p0Index].localPosition,
            controlPoints[p1Index].localPosition,
            controlPoints[p2Index].localPosition,
            controlPoints[p3Index].localPosition,
            t);
    }

    private void BuildVisualMesh()
    {
        generatedMesh = new Mesh { name = "RaceBarrierVisualMesh", hideFlags = HideFlags.DontSave };
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        float distance = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            if (i > 0)
            {
                distance += Vector3.Distance(samples[i - 1].position, samples[i].position);
            }

            Vector3 tangent = GetTangent(i);
            Vector3 outward = GetOutward(tangent);
            int rowStart = vertices.Count;

            for (int section = 0; section <= wallSectionSegments; section++)
            {
                float t = section / (float)wallSectionSegments;
                float height = samples[i].height * t;
                float flare = samples[i].outwardFlare * Mathf.SmoothStep(0f, 1f, t);
                vertices.Add(samples[i].position + outward * flare + Vector3.up * height);
                uvs.Add(new Vector2(distance / uvMetersPerRepeat, height / uvMetersPerRepeat));
            }

            if (i == 0)
            {
                continue;
            }

            int previousRowStart = rowStart - wallSectionSegments - 1;
            for (int section = 0; section < wallSectionSegments; section++)
            {
                int a = previousRowStart + section;
                int b = previousRowStart + section + 1;
                int c = rowStart + section;
                int d = rowStart + section + 1;

                if (outwardSign > 0)
                {
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
                else
                {
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(d);
                    triangles.Add(c);
                }
            }
        }

        generatedMesh.SetVertices(vertices);
        generatedMesh.SetUVs(0, uvs);
        generatedMesh.SetTriangles(triangles, 0);
        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateBounds();

        GameObject visual = new GameObject("WallVisual");
        visual.transform.SetParent(visualRoot, false);
        MeshFilter filter = visual.AddComponent<MeshFilter>();
        MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
        filter.sharedMesh = generatedMesh;
        renderer.sharedMaterial = visualMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private void BuildColliderChain()
    {
        for (int i = 1; i < samples.Count; i++)
        {
            Vector3 start = samples[i - 1].position;
            Vector3 end = samples[i].position;
            Vector3 delta = end - start;
            Vector3 horizontalDelta = Vector3.ProjectOnPlane(delta, Vector3.up);
            if (horizontalDelta.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            Vector3 direction = horizontalDelta.normalized;
            Vector3 outward = GetOutward(direction);
            float height = Mathf.Max(samples[i - 1].height, samples[i].height);
            float length = horizontalDelta.magnitude + colliderThickness;
            Vector3 midpoint = (start + end) * 0.5f + outward * (colliderThickness * 0.5f);
            float baseY = Mathf.Min(start.y, end.y);

            GameObject segment = new GameObject($"WallCollider_{i - 1:000}");
            segment.transform.SetParent(colliderRoot, false);
            segment.transform.localPosition = midpoint;
            segment.transform.localRotation = Quaternion.LookRotation(direction, Vector3.up);

            BoxCollider collider = segment.AddComponent<BoxCollider>();
            collider.size = new Vector3(colliderThickness, height, length);
            collider.center = Vector3.up * (baseY - midpoint.y + height * 0.5f);
            collider.sharedMaterial = colliderMaterial;
        }
    }

    private Vector3 GetTangent(int index)
    {
        int last = samples.Count - 1;
        if (closedLoop && last > 1)
        {
            int effectiveLast = last - 1;
            int previous = index <= 0 ? effectiveLast - 1 : index - 1;
            int next = index >= effectiveLast ? 1 : index + 1;
            return (samples[next].position - samples[previous].position).normalized;
        }

        if (index <= 0)
        {
            return (samples[1].position - samples[0].position).normalized;
        }

        if (index >= last)
        {
            return (samples[last].position - samples[last - 1].position).normalized;
        }

        return (samples[index + 1].position - samples[index - 1].position).normalized;
    }

    private Vector3 GetOutward(Vector3 tangent)
    {
        Vector3 horizontalTangent = Vector3.ProjectOnPlane(tangent, Vector3.up).normalized;
        if (horizontalTangent.sqrMagnitude < 0.0001f)
        {
            horizontalTangent = Vector3.forward;
        }

        return Vector3.Cross(Vector3.up, horizontalTangent).normalized * outwardSign;
    }

    private void EnsureGeneratedRoots()
    {
        generatedRoot = new GameObject(GeneratedRootName).transform;
        generatedRoot.SetParent(transform, false);
        generatedRoot.hideFlags = HideFlags.DontSave;

        visualRoot = new GameObject(VisualRootName).transform;
        visualRoot.SetParent(generatedRoot, false);
        colliderRoot = new GameObject(ColliderRootName).transform;
        colliderRoot.SetParent(generatedRoot, false);
        colliderRoot.gameObject.isStatic = true;
    }

    private void CleanupGeneratedObjects()
    {
        if (generatedRoot != null)
        {
            DestroyGeneratedObject(generatedRoot.gameObject);
        }

        generatedRoot = null;
        visualRoot = null;
        colliderRoot = null;

        if (generatedMesh != null)
        {
            DestroyGeneratedObject(generatedMesh);
            generatedMesh = null;
        }
    }

    private static void DestroyGeneratedObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }
}
