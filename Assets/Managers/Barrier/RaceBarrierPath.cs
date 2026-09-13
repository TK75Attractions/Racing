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
        [Tooltip("下部の登り円弧が60度に達する位置の高さです。上部には乗り越え防止のかえしが続きます。")]
        public float wallHeight = 6f;

        [Min(0f)]
        [Tooltip("下部円弧の終点の最小外側距離です。60度の円弧を保つため、指定が大きい場合は高さも増えます。")]
        public float outwardFlare = 2.5f;
    }

    [Header("手動配置")]
    [SerializeField] private List<ControlPoint> controlPoints = new List<ControlPoint>();
    [SerializeField] private bool closedLoop;
    [SerializeField] private bool useCurves = true;
    [SerializeField, Range(2, 32)] private int samplesPerSegment = 8;

    [Header("断面")]
    [SerializeField, Range(16, 32)]
    [Tooltip("下部の登り円弧と上部のかえし円弧、それぞれの分割数です。")]
    private int wallSectionSegments = 16;
    [SerializeField, Range(-1, 1)] private int outwardSign = 1;
    [SerializeField, Min(0.1f)]
    [Tooltip("下部の登り円弧が60度に達する位置の最低高さです。各制御点はこの値以上なら個別の高さが優先されます。")]
    private float minimumWallHeight = 6f;

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
    private const string DefaultVisualMaterialPath = "RaceBarrier/M_RaceBarrier";
    private const float WallArcAngle = Mathf.PI / 3f;
    private const float ReturnArcEndAngle = 165f * Mathf.Deg2Rad;
    private const float ReturnArcRadiusRatio = 0.375f;

    private readonly List<Sample> samples = new List<Sample>();
    private Transform generatedRoot;
    private Transform visualRoot;
    private Transform colliderRoot;
    private Mesh generatedMesh;
    private Mesh generatedColliderMesh;
#if UNITY_EDITOR
    private bool editorRefreshQueued;
#endif

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

    private void OnDisable()
    {
        CleanupGeneratedObjects();
    }

    private void OnValidate()
    {
        samplesPerSegment = Mathf.Clamp(samplesPerSegment, 2, 32);
        wallSectionSegments = Mathf.Clamp(wallSectionSegments, 16, 32);
        outwardSign = outwardSign < 0 ? -1 : 1;
        minimumWallHeight = Mathf.Max(0.1f, minimumWallHeight);
        colliderThickness = Mathf.Max(0.05f, colliderThickness);
        uvMetersPerRepeat = Mathf.Max(0.1f, uvMetersPerRepeat);

#if UNITY_EDITOR
        if (!Application.isPlaying && !editorRefreshQueued)
        {
            // OnValidate 中の GameObject 作成・破棄は Unity が禁止している。
            editorRefreshQueued = true;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                editorRefreshQueued = false;
                if (this == null)
                {
                    return;
                }

                if (isActiveAndEnabled && generateInEditMode)
                {
                    Rebuild();
                }
                else
                {
                    CleanupGeneratedObjects();
                }
            };
        }
#endif
    }

    /// <summary>制御点からVisualとColliderを再生成します。</summary>
    public void Rebuild()
    {
        CleanupGeneratedObjects();
        // 旧シーンに保存された少ない断面分割も、滑らかな登り口に更新する。
        wallSectionSegments = Mathf.Clamp(wallSectionSegments, 16, 32);

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

                float t = step / (float)sampleCount;
                Vector3 position = useCurves
                    ? EvaluateCatmullRom(segment, t)
                    : Vector3.Lerp(controlPoints[segment].localPosition, controlPoints[next].localPosition, t);

                destination.Add(new Sample
                {
                    position = position,
                    height = Mathf.Max(minimumWallHeight,
                        Mathf.Lerp(controlPoints[segment].wallHeight, controlPoints[next].wallHeight, t)),
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
        int sectionCount = wallSectionSegments * 2;
        generatedMesh = new Mesh { name = "RaceBarrierVisualMesh", hideFlags = HideFlags.DontSave };
        generatedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
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

            for (int section = 0; section <= sectionCount; section++)
            {
                vertices.Add(GetSectionPoint(samples[i], outward, section));
                uvs.Add(new Vector2(distance / uvMetersPerRepeat,
                    GetSectionArcDistance(samples[i], section) / uvMetersPerRepeat));
            }

            if (i == 0)
            {
                continue;
            }

            int previousRowStart = rowStart - sectionCount - 1;
            for (int section = 0; section < sectionCount; section++)
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
        renderer.sharedMaterial = visualMaterial != null
            ? visualMaterial
            : Resources.Load<Material>(DefaultVisualMaterialPath);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private void BuildColliderChain()
    {
        int sectionCount = wallSectionSegments * 2;
        int rowWidth = (sectionCount + 1) * 2;
        int outerOffset = sectionCount + 1;
        List<Vector3> vertices = new List<Vector3>(samples.Count * rowWidth);
        List<int> triangles = new List<int>();

        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 outward = GetOutward(GetTangent(i));
            for (int section = 0; section <= sectionCount; section++)
            {
                vertices.Add(GetSectionPoint(samples[i], outward, section));
            }

            for (int section = 0; section <= sectionCount; section++)
            {
                vertices.Add(GetSectionPoint(samples[i], outward, section) +
                    GetSectionBackNormal(outward, section) * colliderThickness);
            }

            if (i == 0)
            {
                continue;
            }

            int previous = (i - 1) * rowWidth;
            int current = i * rowWidth;
            for (int section = 0; section < sectionCount; section++)
            {
                // 道路側と外側は逆向きの面にし、衝突面を実際の反り形状へ一致させる。
                AddQuad(triangles, previous + section, current + section,
                    previous + section + 1, current + section + 1, outwardSign < 0);
                AddQuad(triangles, previous + outerOffset + section,
                    previous + outerOffset + section + 1,
                    current + outerOffset + section,
                    current + outerOffset + section + 1, outwardSign < 0);
            }

            int top = sectionCount;
            AddQuad(triangles, previous + top, current + top,
                previous + outerOffset + top, current + outerOffset + top, outwardSign < 0);
            AddQuad(triangles, previous, previous + outerOffset,
                current, current + outerOffset, outwardSign < 0);
        }

        if (!closedLoop)
        {
            int last = (samples.Count - 1) * rowWidth;
            for (int section = 0; section < sectionCount; section++)
            {
                AddQuad(triangles, section, section + 1,
                    outerOffset + section, outerOffset + section + 1, outwardSign < 0);
                AddQuad(triangles, last + section, last + outerOffset + section,
                    last + section + 1, last + outerOffset + section + 1, outwardSign < 0);
            }
        }

        generatedColliderMesh = new Mesh { name = "RaceBarrierColliderMesh", hideFlags = HideFlags.DontSave };
        generatedColliderMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        generatedColliderMesh.SetVertices(vertices);
        generatedColliderMesh.SetTriangles(triangles, 0);
        generatedColliderMesh.RecalculateBounds();

        GameObject collision = new GameObject("WallCollider");
        collision.transform.SetParent(colliderRoot, false);
        MeshCollider collider = collision.AddComponent<MeshCollider>();
        collider.convex = false;
        collider.sharedMesh = generatedColliderMesh;
        collider.sharedMaterial = colliderMaterial;
    }

    private Vector3 GetSectionPoint(Sample sample, Vector3 outward, int section)
    {
        float radius = GetArcRadius(sample);
        float angle = GetSectionAngle(section);
        if (section <= wallSectionSegments)
        {
            // 下部は路面に水平接続する60度の円弧。
            return sample.position + outward * (radius * Mathf.Sin(angle)) +
                Vector3.up * (radius * (1f - Mathf.Cos(angle)));
        }

        // 上部は小さい半径で同じ接線から続け、90度を超えて道路側へ戻す。
        float returnRadius = radius * ReturnArcRadiusRatio;
        return sample.position +
            outward * (radius * Mathf.Sin(WallArcAngle) +
                returnRadius * (Mathf.Sin(angle) - Mathf.Sin(WallArcAngle))) +
            Vector3.up * (radius * (1f - Mathf.Cos(WallArcAngle)) +
                returnRadius * (Mathf.Cos(WallArcAngle) - Mathf.Cos(angle)));
    }

    private float GetSectionAngle(int section)
    {
        if (section <= wallSectionSegments)
        {
            return WallArcAngle * section / wallSectionSegments;
        }

        return WallArcAngle + (ReturnArcEndAngle - WallArcAngle) *
            (section - wallSectionSegments) / wallSectionSegments;
    }

    private float GetSectionArcDistance(Sample sample, int section)
    {
        float radius = GetArcRadius(sample);
        float angle = GetSectionAngle(section);
        return section <= wallSectionSegments
            ? radius * angle
            : radius * WallArcAngle + radius * ReturnArcRadiusRatio * (angle - WallArcAngle);
    }

    private Vector3 GetSectionBackNormal(Vector3 outward, int section)
    {
        float angle = GetSectionAngle(section);
        // 一定の厚みを面法線方向に付ける。根元側は路面下へ入り、段差を作らない。
        return outward * Mathf.Sin(angle) - Vector3.up * Mathf.Cos(angle);
    }

    private static float GetArcRadius(Sample sample)
    {
        // 高さと最小張り出しのどちらを優先しても、弧の角度は常に60度にする。
        return Mathf.Max(sample.height / (1f - Mathf.Cos(WallArcAngle)),
            sample.outwardFlare / Mathf.Sin(WallArcAngle));
    }

    private static void AddQuad(List<int> triangles, int a, int b, int c, int d, bool reverse)
    {
        if (reverse)
        {
            triangles.Add(a); triangles.Add(c); triangles.Add(b);
            triangles.Add(c); triangles.Add(d); triangles.Add(b);
        }
        else
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(c); triangles.Add(b); triangles.Add(d);
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
        GameObject rootObject = new GameObject(GeneratedRootName);
        rootObject.hideFlags = HideFlags.DontSave;
        generatedRoot = rootObject.transform;
        generatedRoot.SetParent(transform, false);

        visualRoot = new GameObject(VisualRootName).transform;
        visualRoot.SetParent(generatedRoot, false);
        colliderRoot = new GameObject(ColliderRootName).transform;
        colliderRoot.SetParent(generatedRoot, false);
        colliderRoot.gameObject.isStatic = true;
    }

    private void CleanupGeneratedObjects()
    {
        // ExecuteAlways の非シリアライズ参照は、スクリプト再読み込み時に失われる。
        // 既存の生成階層を直接探し、古い壁がシーンに残らないようにする。
        List<GameObject> roots = new List<GameObject>();
        HashSet<Mesh> meshes = new HashSet<Mesh>();
        if (generatedMesh != null)
        {
            meshes.Add(generatedMesh);
        }
        if (generatedColliderMesh != null)
        {
            meshes.Add(generatedColliderMesh);
        }

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (!IsGeneratedRoot(child))
            {
                continue;
            }

            roots.Add(child.gameObject);
            foreach (MeshFilter filter in child.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null && filter.sharedMesh.name == "RaceBarrierVisualMesh")
                {
                    meshes.Add(filter.sharedMesh);
                }
            }
            foreach (MeshCollider collider in child.GetComponentsInChildren<MeshCollider>(true))
            {
                if (collider.sharedMesh != null && collider.sharedMesh.name == "RaceBarrierColliderMesh")
                {
                    meshes.Add(collider.sharedMesh);
                }
            }
        }

        if (generatedRoot != null && !roots.Contains(generatedRoot.gameObject))
        {
            roots.Add(generatedRoot.gameObject);
        }

        foreach (GameObject root in roots)
        {
            root.SetActive(false);
            DestroyGeneratedObject(root);
        }

        generatedRoot = null;
        visualRoot = null;
        colliderRoot = null;
        generatedMesh = null;
        generatedColliderMesh = null;

        foreach (Mesh mesh in meshes)
        {
            DestroyGeneratedObject(mesh);
        }
    }

    private static bool IsGeneratedRoot(Transform child)
    {
        return child.name == GeneratedRootName &&
            child.Find(VisualRootName) != null &&
            child.Find(ColliderRootName) != null;
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
