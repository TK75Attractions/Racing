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
    private static readonly HashSet<RaceBarrierPath> activePaths = new HashSet<RaceBarrierPath>();
    private readonly List<Vector3> drivingInner = new List<Vector3>();
    private readonly List<Vector3> drivingOuter = new List<Vector3>();
    private Transform generatedRoot;
    private Transform visualRoot;
    private Transform colliderRoot;
    private Mesh generatedMesh;
    private Mesh generatedColliderMesh;
    private TubePortalProfile portalStart, portalEnd;
    private float pathLength;
    private sealed class PortalLoft { public TubePortalProfile profile; public Vector3[] end, tangent; }
    private PortalLoft startLoft, endLoft;
    public bool HasPortalBlend => portalStart != null || portalEnd != null;
#if UNITY_EDITOR
    private bool editorRefreshQueued;
#endif

    private struct Sample
    {
        public Vector3 position;
        public float height;
        public float outwardFlare;
        public float pathDistance;
    }

    public int ControlPointCount => controlPoints != null ? controlPoints.Count : 0;
    public bool ClosedLoop => closedLoop;

    /// <summary>道路境界など、既にサンプリングされた経路を補間せずに壁へ変換します。</summary>
    public void SetWorldPath(IReadOnlyList<Vector3> points, int outside, bool loop,
        Material material, PhysicsMaterial physicsMaterial, bool createCollider,
        TubePortalProfile start = null, TubePortalProfile end = null)
    {
        portalStart = start; portalEnd = end;
        controlPoints.Clear();
        for (int i = 0; i < points.Count; i++)
            controlPoints.Add(new ControlPoint { localPosition = transform.InverseTransformPoint(points[i]) });
        closedLoop = loop;
        useCurves = false;
        outwardSign = outside < 0 ? -1 : 1;
        visualMaterial = material;
        colliderMaterial = physicsMaterial;
        generateCollider = createCollider;
        Rebuild();
    }

    private void OnEnable()
    {
        activePaths.Add(this);
        if (Application.isPlaying || generateInEditMode)
        {
            Rebuild();
        }
    }

    private void OnDisable()
    {
        activePaths.Remove(this);
        CleanupGeneratedObjects();
    }

    /// <summary>道路と同様に高さを無視し、物理バリアの張り出しを走行範囲に含めます。</summary>
    public static bool IsPointInsideDrivingFootprint(Vector3 position, UnityEngine.SceneManagement.Scene scene)
    {
        Vector2 point = new Vector2(position.x, position.z);
        foreach (RaceBarrierPath path in activePaths)
        {
            if (path == null || !path.isActiveAndEnabled || path.gameObject.scene != scene ||
                path.generatedColliderMesh == null) continue;
            for (int i = 1; i < path.drivingInner.Count; i++)
            {
                Vector3 a = path.transform.TransformPoint(path.drivingInner[i - 1]);
                Vector3 b = path.transform.TransformPoint(path.drivingOuter[i - 1]);
                Vector3 c = path.transform.TransformPoint(path.drivingInner[i]);
                Vector3 d = path.transform.TransformPoint(path.drivingOuter[i]);
                if (RaceCourse.IsInsideTriangle(point, a, b, c) ||
                    RaceCourse.IsInsideTriangle(point, b, d, c)) return true;
            }
        }
        return false;
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

        pathLength = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            if (i > 0) pathLength += Vector3.Distance(samples[i - 1].position, samples[i].position);
            Sample row = samples[i]; row.pathDistance = pathLength; samples[i] = row;
        }
        startLoft = BuildPortalLoft(portalStart, true);
        endLoft = BuildPortalLoft(portalEnd, false);
        EnsureGeneratedRoots();

        if (generateVisual)
        {
            BuildVisualMesh();
            BuildSolidFrame();
        }

        if (generateCollider)
        {
            BuildColliderChain();
            // かえしの途中が最も外へ張り出すため、断面全体から外縁を求めます。
            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 outward = GetOutward(GetTangent(i));
                Vector3 outer = samples[i].position;
                float maximum = 0f;
                for (int section = 1; section <= wallSectionSegments * 2; section++)
                {
                    Vector3 candidate = GetSectionPoint(samples[i], outward, section);
                    float distance = Vector3.Dot(candidate - samples[i].position, outward);
                    if (distance <= maximum) continue;
                    maximum = distance;
                    outer = candidate;
                }
                drivingInner.Add(samples[i].position);
                drivingOuter.Add(outer);
            }
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

        var preview = new List<Sample>(); BuildSamples(preview);
        for (int i = 0; i < preview.Count; i++) destination.Add(transform.TransformPoint(preview[i].position));
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
        List<Vector2> edgeDistances = new List<Vector2>();
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
            float totalArcDistance = GetSectionArcDistance(samples[i], sectionCount);

            for (int section = 0; section <= sectionCount; section++)
            {
                float sectionArcDistance = GetSectionArcDistance(samples[i], section);
                vertices.Add(GetSectionPoint(samples[i], outward, section));
                uvs.Add(new Vector2(distance / uvMetersPerRepeat, sectionArcDistance / uvMetersPerRepeat));
                edgeDistances.Add(new Vector2(sectionArcDistance, totalArcDistance - sectionArcDistance));
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
        generatedMesh.SetUVs(1, edgeDistances);
        generatedMesh.SetTriangles(triangles, 0);
        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateBounds();

        GameObject visual = new GameObject("WallVisual");
        visual.layer = gameObject.layer;
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

    private void BuildSolidFrame()
    {
        var geometry = new RaceEnvironmentMeshBuilder();
        Color metal = new Color(.10f, .17f, .22f);
        float distance = 0f, nextPost = 0f;
        int top = wallSectionSegments * 2;
        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 tangent = GetTangent(i).normalized, outward = GetOutward(tangent);
            if (i > 0) distance += Vector3.Distance(samples[i-1].position, samples[i].position);
            if (distance >= nextPost || (!closedLoop && i == samples.Count-1))
            {
                // A solid curved support also reads as the panel joint. It lies behind the drive surface.
                for (int s = 0; s < top; s++)
                {
                    Vector3 normal = GetSectionBackNormal(outward, s);
                    Vector3 a = GetSectionPoint(samples[i], outward, s) + normal * .18f;
                    Vector3 b = GetSectionPoint(samples[i], outward, s+1) + GetSectionBackNormal(outward, s+1) * .18f;
                    geometry.Box((a+b)*.5f, tangent * .20f, b-a, normal * .36f, metal);
                }
                nextPost = distance + 12f;
            }
            if (i == 0) continue;
            foreach (int section in new[] { 0, top })
            {
                Vector3 previousOutward = GetOutward(GetTangent(i-1));
                Vector3 a = GetSectionPoint(samples[i-1], previousOutward, section) + GetSectionBackNormal(previousOutward, section) * .15f;
                Vector3 b = GetSectionPoint(samples[i], outward, section) + GetSectionBackNormal(outward, section) * .15f;
                Vector3 normal = GetSectionBackNormal(outward, section);
                geometry.Box((a+b)*.5f, b-a, Vector3.Cross(normal, (b-a).normalized) * .22f, normal * .30f, metal);
            }
        }
        Mesh mesh = geometry.ToMesh("RaceBarrierSolidFrame");
        mesh.hideFlags = HideFlags.DontSave;
        var frame = new GameObject("Solid rails and panel supports", typeof(MeshFilter), typeof(MeshRenderer));
        frame.layer = gameObject.layer;
        frame.transform.SetParent(visualRoot, false);
        frame.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = frame.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = Resources.Load<Material>("RaceBarrier/M_EnvironmentSolid");
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
                    GetSurfaceBackNormal(i, outward, section) * colliderThickness);
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
        collision.layer = gameObject.layer;
        collision.transform.SetParent(colliderRoot, false);
        MeshCollider collider = collision.AddComponent<MeshCollider>();
        collider.convex = false;
        collider.sharedMesh = generatedColliderMesh;
        collider.sharedMaterial = colliderMaterial;
    }

    private Vector3 OriginalAtDistance(float distance, int section)
    {
        distance = Mathf.Clamp(distance, 0, pathLength);
        int low = 0, high = samples.Count-2;
        while (low < high) { int m=(low+high)/2; if (samples[m+1].pathDistance < distance) low=m+1; else high=m; }
        float t = Mathf.InverseLerp(samples[low].pathDistance, samples[low+1].pathDistance, distance);
        return Vector3.Lerp(GetOriginalSectionPoint(samples[low], GetOutward(GetTangent(low)), section),
            GetOriginalSectionPoint(samples[low+1], GetOutward(GetTangent(low+1)), section), t);
    }
    private PortalLoft BuildPortalLoft(TubePortalProfile profile, bool start)
    {
        if (profile == null) return null;
        int count = wallSectionSegments * 2 + 1;
        var loft = new PortalLoft { profile = profile, end = new Vector3[count], tangent = new Vector3[count] };
        float d = start ? TubePortalProfile.BlendLength : pathLength - TubePortalProfile.BlendLength;
        for (int k=0;k<count;k++)
        {
            loft.end[k] = OriginalAtDistance(d, k);
            loft.tangent[k] = (OriginalAtDistance(d+.1f,k)-OriginalAtDistance(d-.1f,k)) / .2f * (start ? 1 : -1);
        }
        return loft;
    }
    private Vector3 GetSectionPoint(Sample sample, Vector3 outward, int section)
    {
        float distance = sample.pathDistance; PortalLoft loft = startLoft;
        if (endLoft != null && (loft == null || pathLength-distance < distance)) { loft=endLoft; distance=pathLength-distance; }
        if (loft == null || distance >= TubePortalProfile.BlendLength) return GetOriginalSectionPoint(sample, outward, section);
        float t = Mathf.Clamp01(distance / TubePortalProfile.BlendLength), t2=t*t, t3=t2*t;
        // One Hermite arc per longitudinal column: exact positions AND derivatives at
        // the tube and the outdoor wall. No long tangent extrapolation / quintic bulge.
        Vector3 a = transform.InverseTransformPoint(loft.profile.points[section]);
        Vector3 tangent = transform.InverseTransformVector(loft.profile.outwardTangents[section]);
        return (2*t3-3*t2+1)*a + (t3-2*t2+t)*TubePortalProfile.BlendLength*tangent +
            (-2*t3+3*t2)*loft.end[section] + (t3-t2)*TubePortalProfile.BlendLength*loft.tangent[section];
    }

    private Vector3 GetSurfaceBackNormal(int index, Vector3 outward, int section)
    {
        if (!HasPortalBlend) return GetSectionBackNormal(outward, section);
        Vector3 profile = GetSectionPoint(samples[index], outward, Mathf.Min(wallSectionSegments * 2, section + 1)) -
            GetSectionPoint(samples[index], outward, Mathf.Max(0, section - 1));
        Vector3 normal = Vector3.Cross(GetTangent(index), profile).normalized;
        if (Vector3.Dot(normal, GetSectionBackNormal(outward, section)) < 0) normal = -normal;
        return normal;
    }

    private Vector3 GetOriginalSectionPoint(Sample sample, Vector3 outward, int section)
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
        rootObject.layer = gameObject.layer;
        rootObject.hideFlags = HideFlags.DontSave;
        generatedRoot = rootObject.transform;
        generatedRoot.SetParent(transform, false);

        visualRoot = new GameObject(VisualRootName).transform;
        visualRoot.gameObject.layer = gameObject.layer;
        visualRoot.SetParent(generatedRoot, false);
        colliderRoot = new GameObject(ColliderRootName).transform;
        colliderRoot.gameObject.layer = gameObject.layer;
        colliderRoot.SetParent(generatedRoot, false);
        colliderRoot.gameObject.isStatic = true;
    }

    private void CleanupGeneratedObjects()
    {
        drivingInner.Clear();
        drivingOuter.Clear();
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
                if (filter.sharedMesh != null && (filter.sharedMesh.name == "RaceBarrierVisualMesh" || filter.sharedMesh.name == "RaceBarrierSolidFrame"))
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
