using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Subtracts the drivable volume from imported school collision, leaving render meshes alone.</summary>
public static class CourseBuildingClearance
{
    private const float CellSize = 16f;
    private const float SampleSpacing = 1f;
    private const float BelowRoad = .3f;
    private const float AboveRoad = 3f;
    private const float MinimumAreaSquared = 1e-12f;
    private const string AssetSuffix = "CourseClearance.asset";

    internal sealed class Prism
    {
        // All plane normals point inward; a point is inside when every distance is >= 0.
        public ClipPlane[] planes;
        public ClipPlane[] boundary;
        public Bounds bounds;

        public double Distance(int plane, ClipPoint point) => planes[plane].Distance(point);
    }

    internal sealed class VolumeIndex
    {
        public readonly List<Prism> prisms = new List<Prism>();
        private readonly Dictionary<Vector2Int, List<int>> cells = new Dictionary<Vector2Int, List<int>>();

        // Internal diagonals and sample joins are not clearance boundaries. Eroding every
        // prism separately would hide residual faces along those seams from validation.
        public bool ContainsInterior(Vector3 point, float margin, HashSet<int> seen, List<int> candidates)
        {
            Query(new Bounds(point, Vector3.zero), seen, candidates);
            foreach (int candidate in candidates)
            {
                Prism prism = prisms[candidate];
                bool inside = true;
                ClipPoint p = new ClipPoint(point);
                for (int i = 0; i < prism.planes.Length; i++)
                    if (prism.Distance(i, p) < 0)
                    { inside = false; break; }
                foreach (ClipPlane plane in prism.boundary)
                    if (plane.Distance(p) < margin) { inside = false; break; }
                if (inside) return true;
            }
            return false;
        }

        public void Add(Prism prism)
        {
            int index = prisms.Count;
            prisms.Add(prism);
            Vector2Int min = Cell(prism.bounds.min), max = Cell(prism.bounds.max);
            for (int x = min.x; x <= max.x; x++)
                for (int z = min.y; z <= max.y; z++)
                {
                    var key = new Vector2Int(x, z);
                    if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<int>());
                    list.Add(index);
                }
        }

        public void Query(Bounds bounds, HashSet<int> seen, List<int> result)
        {
            seen.Clear(); result.Clear();
            Vector2Int min = Cell(bounds.min), max = Cell(bounds.max);
            for (int x = min.x; x <= max.x; x++)
                for (int z = min.y; z <= max.y; z++)
                    if (cells.TryGetValue(new Vector2Int(x, z), out var list))
                        foreach (int index in list)
                            if (seen.Add(index) && prisms[index].bounds.Intersects(bounds)) result.Add(index);
            result.Sort(); // Stable clipping and asset contents on repeated bakes.
        }

        private static Vector2Int Cell(Vector3 p) =>
            new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
    }

    public static void Run(Scene scene, RaceCourse course, MeshCollider tube)
    {
        if (LayerMask.NameToLayer("CourseOcclusion") != CoursePhysicsLayers.OcclusionLayer)
            throw new InvalidOperationException("CourseOcclusion must occupy layer 14.");
        VolumeIndex volumes = CreateVolumes(course, tube);
        Bake(scene, volumes);
    }

    internal static Matrix4x4 PhysicsMatrix(Transform transform) =>
        Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);

    internal static VolumeIndex CreateVolumes(RaceCourse course, MeshCollider tube)
    {
        if (course == null || !course.HasValidPath || tube == null || tube.sharedMesh == null)
            throw new InvalidOperationException("Clearance requires a valid course and tube collider.");
        bool oldBackfaces = Physics.queriesHitBackfaces;
        try
        {
            Physics.queriesHitBackfaces = true;
            Physics.SyncTransforms();
            return BuildVolumes(course, tube, ReadBarrierReach());
        }
        finally { Physics.queriesHitBackfaces = oldBackfaces; }
    }

    private static void Bake(Scene scene, VolumeIndex volumes)
    {
        var targets = new List<MeshCollider>();
        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
            {
                // Restoring an excluded object destroys its occlusion child, which this list still holds.
                if (collider == null) continue;
                if (collider.gameObject.layer == CoursePhysicsLayers.OcclusionLayer ||
                    !collider.gameObject.activeInHierarchy) continue;
                if (Excluded(collider.name)) { RestoreExcluded(collider); continue; }
                MeshFilter filter = collider.GetComponent<MeshFilter>();
                MeshFilter source = filter == null ? null : PrefabUtility.GetCorrespondingObjectFromSource(filter);
                if (source == null || source.sharedMesh == null ||
                    AssetDatabase.GetAssetPath(source) != TubeCoursePreparation.ModelPath) continue;
                targets.Add(collider);
                string name = CleanName(collider.name);
                nameCounts.TryGetValue(name, out int count);
                nameCounts[name] = count + 1;
            }

        int objects = 0, removed = 0;
        var details = new StringBuilder();
        foreach (MeshCollider collider in targets)
        {
            CourseClearanceSource state = GetSource(collider);
            Mesh original = state.originalMesh;
            if (original == null) continue;
            // Use the same 15cm slab that runtime wall preparation would create, then cut it.
            // Both sides survive outside the driving volume, even for a subdivided output mesh.
            Mesh solid = null;
            Mesh savedMesh = collider.sharedMesh;
            bool savedEnabled = collider.enabled;
            try
            {
                collider.sharedMesh = original;
                collider.enabled = true;
                Physics.SyncTransforms();
                if (!collider.isTrigger && collider.attachedRigidbody == null &&
                    StaticWallColliderVolume.TryGetWallBox(collider, out Bounds wall))
                    solid = CreateBoxMesh(wall);
            }
            finally { collider.sharedMesh = savedMesh; collider.enabled = savedEnabled; }
            Mesh clipped;
            int affected, remaining;
            try { clipped = ClipMesh(solid != null ? solid : original, collider.transform, volumes, out affected, out remaining); }
            finally { if (solid != null) UnityEngine.Object.DestroyImmediate(solid); }
            if (affected == 0)
            {
                collider.sharedMesh = original;
                collider.enabled = state.originalEnabled;
                if (state.occlusion != null) UnityEngine.Object.DestroyImmediate(state.occlusion.gameObject);
                state.occlusion = null;
                EditorUtility.SetDirty(state);
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                continue;
            }

            EnsureOcclusion(state);

            string name = CleanName(collider.name);
            if (nameCounts[name] > 1) name += StableId(collider);
            string path = TubeCoursePreparation.DataFolder + "/" + name + AssetSuffix;
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            // Release the old cooked mesh before updating the asset in place.
            collider.sharedMesh = null;
            collider.enabled = state.originalEnabled && remaining > 0;
            if (existing == null) { AssetDatabase.CreateAsset(clipped, path); existing = clipped; }
            else
            {
                EditorUtility.CopySerialized(clipped, existing);
                UnityEngine.Object.DestroyImmediate(clipped);
                EditorUtility.SetDirty(existing);
            }
            collider.sharedMesh = existing;
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            objects++; removed += affected;
            details.Append("\n  ").Append(HierarchyPath(collider.transform)).Append(": ")
                .Append(affected).Append(" source triangles cut, ").Append(remaining).Append(" output triangles")
                .Append(remaining == 0 ? " (collider disabled)" : "");
        }
        // A partially removed source triangle counts once, even if clipping creates more output triangles.
        Debug.Log($"COURSE_BUILDING_CLEARANCE_PASS: {objects} objects, {removed} triangles" + details);
    }

    private static CourseClearanceSource GetSource(MeshCollider collider)
    {
        foreach (var stored in collider.GetComponents<CourseClearanceSource>())
            if (stored.target == collider) return stored;
        var state = collider.gameObject.AddComponent<CourseClearanceSource>();
        state.target = collider;
        state.originalMesh = collider.sharedMesh;
        state.originalEnabled = collider.enabled;
        if (OwnsMesh(collider))
        {
            // Legacy bakes did not retain their inputs. Recover the prefab collider when possible;
            // an override/enabled value lost by the old bake cannot be reconstructed.
            MeshCollider prefab = PrefabUtility.GetCorrespondingObjectFromSource(collider);
            state.originalMesh = prefab != null ? prefab.sharedMesh :
                PrefabUtility.GetCorrespondingObjectFromSource(collider.GetComponent<MeshFilter>()).sharedMesh;
            if (prefab != null) state.originalEnabled = prefab.enabled;
            else if (collider.sharedMesh != null && collider.sharedMesh.GetIndexCount(0) == 0)
                state.originalEnabled = true;
            Debug.LogWarning("Legacy clearance input recovered from prefab; verify original mesh/enabled: " +
                HierarchyPath(collider.transform), collider);
        }
        EditorUtility.SetDirty(state);
        return state;
    }

    private static void EnsureOcclusion(CourseClearanceSource state)
    {
        if (state.occlusion == null)
        {
            var go = new GameObject("Course Occlusion " + StableId(state.target));
            go.transform.SetParent(state.target.transform, false);
            state.occlusion = go.AddComponent<MeshCollider>();
        }
        MeshCollider query = state.occlusion;
        query.gameObject.layer = CoursePhysicsLayers.OcclusionLayer;
        query.sharedMesh = state.originalMesh;
        // Visibility casts ignore triggers, just as they did before the clearance bake.
        query.enabled = state.originalEnabled && !state.target.isTrigger;
        query.isTrigger = false;
        query.convex = false;
        // Layer overrides on cars/items must not opt this query-only collider into contacts.
        query.includeLayers = 0;
        query.excludeLayers = ~0;
        query.layerOverridePriority = int.MaxValue;
        EditorUtility.SetDirty(state);
    }

    private static Mesh CreateBoxMesh(Bounds box)
    {
        var vertices = new Vector3[8];
        for (int i = 0; i < 8; i++)
            vertices[i] = box.center + Vector3.Scale(box.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        var mesh = new Mesh { name = "Thick school wall" };
        mesh.vertices = vertices;
        mesh.triangles = new[] { 0,2,1, 1,2,3, 4,5,6, 5,7,6, 0,4,2, 2,4,6,
            1,3,5, 3,7,5, 0,1,4, 1,5,4, 2,6,3, 3,6,7 };
        mesh.RecalculateBounds();
        return mesh;
    }

    // Trees are left to ClearRoadsideBranches: their 70k-120k triangle foliage colliders made the
    // clearance bake huge and, because that pass raycasts the current mesh, not repeatable.
    private static bool Excluded(string name) => name == "Plane" || name == "BézierCurve.001" ||
        name == "Plane.012" || name == "Plane.009" || name == "Cube.001" ||
        name.StartsWith("tree", StringComparison.OrdinalIgnoreCase);

    /// <summary>Undoes an earlier bake of an object that is now excluded.</summary>
    private static void RestoreExcluded(MeshCollider collider)
    {
        foreach (var state in collider.GetComponents<CourseClearanceSource>())
        {
            if (state.target != collider) continue;
            if (state.originalMesh != null) collider.sharedMesh = state.originalMesh;
            collider.enabled = state.originalEnabled;
            if (state.occlusion != null) UnityEngine.Object.DestroyImmediate(state.occlusion.gameObject);
            UnityEngine.Object.DestroyImmediate(state);
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            Debug.Log("Course clearance removed from excluded object: " + HierarchyPath(collider.transform), collider);
        }
    }

    private static bool OwnsMesh(MeshCollider collider)
    {
        string path = AssetDatabase.GetAssetPath(collider.sharedMesh);
        return path.StartsWith(TubeCoursePreparation.DataFolder + "/", StringComparison.Ordinal) &&
            path.EndsWith(AssetSuffix, StringComparison.Ordinal);
    }

    private static string CleanName(string name)
    {
        var result = new StringBuilder();
        foreach (char c in name) if (char.IsLetterOrDigit(c)) result.Append(c);
        return result.Length == 0 ? "Object" : result.ToString();
    }

    private static string HierarchyPath(Transform transform) => transform.parent == null ? transform.name :
        HierarchyPath(transform.parent) + "/" + transform.name;

    private static string StableId(MeshCollider collider)
    {
        string key = "";
        for (Transform t = collider.transform; t != null; t = t.parent)
            key = t.name + "[" + t.GetSiblingIndex() + "]/" + key;
        key += Array.IndexOf(collider.GetComponents<MeshCollider>(), collider);
        uint hash = 2166136261;
        foreach (char c in key) hash = unchecked((hash ^ c) * 16777619);
        return hash.ToString("X8");
    }

    private static float ReadBarrierReach()
    {
        // Auto barriers use SetWorldPath's default ControlPoint and component settings. The profile
        // API is private, so invoke that same implementation instead of duplicating its arc constants.
        var go = new GameObject("Clearance profile lookup") { hideFlags = HideFlags.HideAndDontSave };
        go.SetActive(false); // Do not generate meshes, register a path, or run OnEnable.
        try
        {
            var barrier = go.AddComponent<RaceBarrierPath>();
            var settings = new SerializedObject(barrier);
            int segments = settings.FindProperty("wallSectionSegments").intValue;
            var defaults = new RaceBarrierPath.ControlPoint();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            Type sampleType = typeof(RaceBarrierPath).GetNestedType("Sample", BindingFlags.NonPublic);
            MethodInfo sectionPoint = typeof(RaceBarrierPath).GetMethod("GetSectionPoint", flags);
            if (sampleType == null || sectionPoint == null)
                throw new InvalidOperationException("RaceBarrierPath profile API changed; cannot derive clearance width.");
            object sample = Activator.CreateInstance(sampleType);
            sampleType.GetField("height").SetValue(sample,
                Mathf.Max(defaults.wallHeight, settings.FindProperty("minimumWallHeight").floatValue));
            sampleType.GetField("outwardFlare").SetValue(sample, defaults.outwardFlare);
            float reach = 0f;
            for (int i = 0; i <= segments * 2; i++)
            {
                var point = (Vector3)sectionPoint.Invoke(barrier, new object[] { sample, Vector3.right, i });
                reach = Mathf.Max(reach, point.x);
            }
            if (reach <= 0f) throw new InvalidOperationException("Barrier profile has no outward reach.");
            return reach;
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    // Cooking an identical MeshCollider can move PhysX's ray-hit boundary by a
    // fraction of a millimetre. Use source triangles for this offline boolean so
    // repeated bakes produce the same portal and collision mesh fragments.
    private sealed class TubeVolumeQuery
    {
        private struct Triangle
        {
            public ClipPoint origin, edge1, edge2;
            public Bounds bounds;
        }

        private readonly Triangle[] triangles;
        private readonly Bounds bounds;

        public TubeVolumeQuery(MeshCollider tube)
        {
            triangles = Array.Empty<Triangle>();
            if (tube == null || !tube.enabled || !tube.gameObject.activeInHierarchy || tube.sharedMesh == null) return;
            Vector3[] vertices = tube.sharedMesh.vertices;
            int[] indices = tube.sharedMesh.triangles;
            if (vertices.Length == 0 || indices.Length == 0) return;
            Matrix4x4 matrix = PhysicsMatrix(tube.transform);
            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 vertex in vertices) bounds.Encapsulate(vertex);
            triangles = new Triangle[indices.Length / 3];
            for (int i = 0; i < triangles.Length; i++)
            {
                Vector3 a = vertices[indices[i * 3]], b = vertices[indices[i * 3 + 1]], c = vertices[indices[i * 3 + 2]];
                var triangleBounds = new Bounds(a, Vector3.zero); triangleBounds.Encapsulate(b); triangleBounds.Encapsulate(c);
                ClipPoint origin = new ClipPoint(a);
                triangles[i] = new Triangle { origin = origin, edge1 = new ClipPoint(b) - origin,
                    edge2 = new ClipPoint(c) - origin, bounds = triangleBounds };
            }
        }

        public bool Contains(Vector3 center, Vector3 side)
        {
            Vector3 origin = center + Vector3.up * .25f;
            return Hit(origin, -side, 25f) && Hit(origin, side, 25f) && Hit(origin, Vector3.up, 40f);
        }

        private bool Hit(Vector3 origin, Vector3 direction, float maximum)
        {
            if (triangles.Length == 0 || direction.sqrMagnitude < 1e-12f) return false;
            direction.Normalize();
            var rayBounds = new Bounds(origin, Vector3.zero); rayBounds.Encapsulate(origin + direction * maximum);
            rayBounds.Expand(.00001f);
            if (!bounds.Intersects(rayBounds)) return false;
            ClipPoint rayOrigin = new ClipPoint(origin), rayDirection = new ClipPoint(direction);
            foreach (Triangle triangle in triangles)
            {
                if (!triangle.bounds.Intersects(rayBounds)) continue;
                ClipPoint p = ClipPoint.Cross(rayDirection, triangle.edge2);
                double determinant = ClipPoint.Dot(triangle.edge1, p);
                if (Math.Abs(determinant) < 1e-12) continue;
                double inverse = 1.0 / determinant;
                ClipPoint offset = rayOrigin - triangle.origin;
                double u = ClipPoint.Dot(offset, p) * inverse;
                if (u < -1e-10 || u > 1.0 + 1e-10) continue;
                ClipPoint q = ClipPoint.Cross(offset, triangle.edge1);
                double v = ClipPoint.Dot(rayDirection, q) * inverse;
                if (v < -1e-10 || u + v > 1.0 + 1e-10) continue;
                double distance = ClipPoint.Dot(triangle.edge2, q) * inverse;
                if (distance > 0.0 && distance <= maximum) return true;
            }
            return false;
        }
    }

    private static VolumeIndex BuildVolumes(RaceCourse course, MeshCollider tube, float shoulder)
    {
        var result = new VolumeIndex();
        var tubeQuery = new TubeVolumeQuery(tube);
        var path = new List<Vector3>();
        course.CopyCenterPathWorld(path);
        float progress = 0f;
        // Keep all rounded-path knots, subdividing long intervals to resolve tube portals accurately.
        for (int i = 1; i < path.Count; i++)
        {
            float length = Vector3.Distance(path[i - 1], path[i]);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / SampleSpacing));
            for (int step = 0; step < steps; step++)
            {
                float start = progress + length * step / steps;
                float end = progress + length * (step + 1) / steps;
                if (!course.TryGetSampleAtProgress(start, out var a) ||
                    !course.TryGetSampleAtProgress(end, out var b)) continue;
                bool aInside = tubeQuery.Contains(a.position, a.right);
                bool bInside = tubeQuery.Contains(b.position, b.right);
                if (aInside && bInside) continue;
                if (aInside != bInside)
                {
                    float low = start, high = end;
                    for (int iteration = 0; iteration < 16; iteration++)
                    {
                        float mid = (low + high) * .5f;
                        course.TryGetSampleAtProgress(mid, out var sample);
                        if (tubeQuery.Contains(sample.position, sample.right) == aInside) low = mid;
                        else high = mid;
                    }
                    course.TryGetSampleAtProgress(aInside ? high : low, out var portal);
                    if (aInside) a = portal; else b = portal;
                }
                // HasRoadAtProgress deliberately does not gate clipping: no-road samples carry the
                // airborne centerline and width used by barriers through the jump as well.
                Vector3 al = a.leftEdge - a.right * shoulder, ar = a.rightEdge + a.right * shoulder;
                Vector3 bl = b.leftEdge - b.right * shoulder, br = b.rightEdge + b.right * shoulder;
                bool startBoundary = aInside || (!course.ClosedLoop && i == 1 && step == 0);
                bool endBoundary = bInside || (!course.ClosedLoop && i == path.Count - 1 && step == steps - 1);
                ClipPoint center = (new ClipPoint(al) + new ClipPoint(ar) + new ClipPoint(bl) + new ClipPoint(br)) * .25;
                var boundary = new List<ClipPlane>
                {
                    SidePlane(new ClipPoint(al), new ClipPoint(bl), center),
                    SidePlane(new ClipPoint(ar), new ClipPoint(br), center)
                };
                if (startBoundary) boundary.Add(SidePlane(new ClipPoint(al), new ClipPoint(ar), center));
                if (endBoundary) boundary.Add(SidePlane(new ClipPoint(bl), new ClipPoint(br), center));
                AddPrism(result, al, ar, bl, boundary);
                AddPrism(result, ar, br, bl, boundary);
            }
            progress += length;
        }
        return result;
    }

    private static void AddPrism(VolumeIndex index, Vector3 a, Vector3 b, Vector3 c, List<ClipPlane> boundary)
    {
        ClipPoint da = new ClipPoint(a), db = new ClipPoint(b), dc = new ClipPoint(c);
        ClipPoint up = new ClipPoint(0, 1, 0);
        ClipPoint normal = ClipPoint.Cross(db - da, dc - da);
        if (Math.Abs(normal.y) < 1e-8) return;
        if (normal.y < 0) normal = normal * -1;
        ClipPoint center = (da + db + dc) * (1.0 / 3);
        var planes = new ClipPlane[5];
        ClipPoint[] points = { da, db, dc };
        for (int i = 0; i < 3; i++)
        {
            planes[i] = SidePlane(points[i], points[(i + 1) % 3], center);
        }
        // Vertical offsets from the interpolated road, rather than a world-horizontal height slab.
        planes[3] = new ClipPlane(normal, da - up * BelowRoad);
        planes[4] = new ClipPlane(normal * -1, da + up * AboveRoad);
        Bounds bounds = new Bounds(a - Vector3.up * BelowRoad, Vector3.zero);
        foreach (Vector3 p in new[] { a, b, c })
        {
            bounds.Encapsulate(p - Vector3.up * BelowRoad);
            bounds.Encapsulate(p + Vector3.up * AboveRoad);
        }
        // Both triangles need both outer side planes for the 5cm validation margin,
        // even when a particular side only touches that triangle at a corner.
        var boundaries = new List<ClipPlane>(boundary) { planes[3], planes[4] };
        index.Add(new Prism { planes = planes, boundary = boundaries.ToArray(), bounds = bounds });
    }

    private static ClipPlane SidePlane(ClipPoint p, ClipPoint q, ClipPoint center)
    {
        // Identical anchors and double normals prevent cracks between reversed/shared edges.
        if (p.x > q.x || (p.x == q.x && (p.y > q.y || (p.y == q.y && p.z > q.z))))
        { ClipPoint swap = p; p = q; q = swap; }
        ClipPoint inward = ClipPoint.Cross(q - p, new ClipPoint(0, 1, 0));
        if (ClipPoint.Dot(inward, center - p) < 0) inward = inward * -1;
        return new ClipPlane(inward, p);
    }

    internal readonly struct ClipPlane
    {
        private readonly ClipPoint normal, origin;
        public ClipPlane(ClipPoint normal, ClipPoint origin)
        {
            this.normal = normal * (1.0 / Math.Sqrt(normal.sqrMagnitude));
            this.origin = origin;
        }
        public double Distance(ClipPoint point) => ClipPoint.Dot(point - origin, normal);
    }

    // Float plane offsets and repeated lerps near 600m leave long, almost zero-width
    // fragments between adjoining prisms (Plane.017). Keep planes and the entire boolean
    // operation in double precision; round only the final mesh vertices.
    internal readonly struct ClipPoint : IEquatable<ClipPoint>
    {
        public readonly double x, y, z;
        public ClipPoint(Vector3 p) : this(p.x, p.y, p.z) { }
        public ClipPoint(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public Vector3 Vector => new Vector3((float)x, (float)y, (float)z);
        public double sqrMagnitude => x * x + y * y + z * z;
        public static ClipPoint operator +(ClipPoint a, ClipPoint b) => new ClipPoint(a.x+b.x, a.y+b.y, a.z+b.z);
        public static ClipPoint operator -(ClipPoint a, ClipPoint b) => new ClipPoint(a.x-b.x, a.y-b.y, a.z-b.z);
        public static ClipPoint operator *(ClipPoint a, double t) => new ClipPoint(a.x*t, a.y*t, a.z*t);
        public static double Dot(ClipPoint a, ClipPoint b) => a.x*b.x + a.y*b.y + a.z*b.z;
        public static ClipPoint Cross(ClipPoint a, ClipPoint b) =>
            new ClipPoint(a.y*b.z-a.z*b.y, a.z*b.x-a.x*b.z, a.x*b.y-a.y*b.x);
        public bool Equals(ClipPoint other) => x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z);
        public override bool Equals(object obj) => obj is ClipPoint other && Equals(other);
        public override int GetHashCode() => (x, y, z).GetHashCode();
    }

    private static Mesh ClipMesh(Mesh source, Transform transform, VolumeIndex index,
        out int affected, out int remaining)
    {
        affected = remaining = 0;
        Vector3[] world = source.vertices;
        // Physics uses rotation and lossy scale, without the shear in localToWorldMatrix
        // when a rotated child has a non-uniformly scaled parent. Clip in that same frame;
        // otherwise the saved vertices move the clearance away from the road when cooked.
        Matrix4x4 toWorld = PhysicsMatrix(transform);
        Matrix4x4 toLocal = toWorld.inverse;
        Bounds bounds = new Bounds(toWorld.MultiplyPoint3x4(source.bounds.center), Vector3.zero);
        for (int i = 0; i < world.Length; i++)
        {
            world[i] = toWorld.MultiplyPoint3x4(world[i]); bounds.Encapsulate(world[i]);
        }
        var seen = new HashSet<int>();
        var candidates = new List<int>();
        index.Query(bounds, seen, candidates);
        if (candidates.Count == 0) return null;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var vertexIndices = new Dictionary<Vector3, int>();
        int[] input = source.triangles;
        for (int i = 0; i < input.Length; i += 3)
        {
            Vector3 a = world[input[i]], b = world[input[i + 1]], c = world[input[i + 2]];
            var triangleBounds = new Bounds(a, Vector3.zero);
            triangleBounds.Encapsulate(b); triangleBounds.Encapsulate(c);
            index.Query(triangleBounds, seen, candidates);
            var pieces = new List<List<ClipPoint>> { new List<ClipPoint> { new ClipPoint(a), new ClipPoint(b), new ClipPoint(c) } };
            bool changed = false;
            foreach (int candidate in candidates)
            {
                if (pieces.Count == 0) break;
                var next = new List<List<ClipPoint>>();
                foreach (var piece in pieces) changed |= Subtract(piece, index.prisms[candidate], next);
                pieces = next;
            }
            if (changed) affected++;
            MergeConvexPieces(pieces);
            foreach (var piece in pieces)
                for (int j = 1; j < piece.Count - 1; j++)
                {
                    if (ClipPoint.Cross(piece[j] - piece[0], piece[j + 1] - piece[0]).sqrMagnitude <= MinimumAreaSquared) continue;
                    triangles.Add(AddVertex(toLocal.MultiplyPoint3x4(piece[0].Vector), vertices, vertexIndices));
                    triangles.Add(AddVertex(toLocal.MultiplyPoint3x4(piece[j].Vector), vertices, vertexIndices));
                    triangles.Add(AddVertex(toLocal.MultiplyPoint3x4(piece[j + 1].Vector), vertices, vertexIndices));
                }
        }
        if (affected == 0) return null;
        remaining = triangles.Count / 3;
        var mesh = new Mesh { name = source.name + " course collision clearance",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
        return mesh;
    }

    private static int AddVertex(Vector3 point, List<Vector3> vertices, Dictionary<Vector3, int> indices)
    {
        if (indices.TryGetValue(point, out int index)) return index;
        index = vertices.Count;
        vertices.Add(point); indices.Add(point, index);
        return index;
    }

    // Only merge adjacent fragments of the same source triangle when their union is convex.
    // This removes internal prism seams without bridging a clearance hole or changing its boundary.
    private static void MergeConvexPieces(List<List<ClipPoint>> pieces)
    {
        bool merged;
        do
        {
            merged = false;
            var edges = new Dictionary<(ClipPoint, ClipPoint), (int polygon, int edge)>();
            for (int i = 0; i < pieces.Count && !merged; i++)
            {
                var a = pieces[i];
                for (int j = 0; j < a.Count; j++)
                {
                    ClipPoint start = a[j], end = a[(j + 1) % a.Count];
                    if (edges.TryGetValue((end, start), out var other) && other.polygon != i)
                    {
                        var b = pieces[other.polygon];
                        var union = new List<ClipPoint>(a.Count + b.Count - 2);
                        for (int k = 1; k < a.Count; k++) union.Add(a[(j + k) % a.Count]);
                        for (int k = 1; k < b.Count; k++) union.Add(b[(other.edge + k) % b.Count]);
                        if (IsConvex(union))
                        {
                            RemoveCollinear(union);
                            pieces[other.polygon] = union;
                            pieces.RemoveAt(i);
                            merged = true;
                            break;
                        }
                    }
                    edges[(start, end)] = (i, j);
                }
            }
        } while (merged);
        foreach (var piece in pieces) RemoveCollinear(piece);
    }

    private static bool IsConvex(List<ClipPoint> polygon)
    {
        ClipPoint normal = default;
        for (int i = 1; i < polygon.Count - 1; i++)
            normal += ClipPoint.Cross(polygon[i] - polygon[0], polygon[i + 1] - polygon[0]);
        if (normal.sqrMagnitude <= MinimumAreaSquared) return false;
        normal = normal * (1.0 / Math.Sqrt(normal.sqrMagnitude));
        for (int i = 0; i < polygon.Count; i++)
        {
            ClipPoint a = polygon[(i + 1) % polygon.Count] - polygon[i];
            ClipPoint b = polygon[(i + 2) % polygon.Count] - polygon[(i + 1) % polygon.Count];
            if (ClipPoint.Dot(ClipPoint.Cross(a, b), normal) < 0f) return false;
        }
        return true;
    }

    private static void RemoveCollinear(List<ClipPoint> polygon)
    {
        for (int i = polygon.Count - 1; i >= 0 && polygon.Count > 3; i--)
        {
            ClipPoint a = polygon[i] - polygon[(i + polygon.Count - 1) % polygon.Count];
            ClipPoint b = polygon[(i + 1) % polygon.Count] - polygon[i];
            if (ClipPoint.Cross(a, b).sqrMagnitude <= MinimumAreaSquared && ClipPoint.Dot(a, b) >= 0f)
                polygon.RemoveAt(i);
        }
    }

    private static bool Subtract(List<ClipPoint> polygon, Prism prism, List<List<ClipPoint>> output)
    {
        // First establish an intersection with positive surface area. AABB-only contacts and
        // tangencies must not split or replace a collider that never enters the driving volume.
        List<ClipPoint> intersection = polygon;
        for (int plane = 0; plane < prism.planes.Length; plane++)
        {
            bool anyInside = false, anyOutside = false;
            foreach (ClipPoint p in intersection)
            {
                double d = prism.Distance(plane, p);
                anyInside |= d >= 0f; anyOutside |= d < 0f;
            }
            if (!anyInside) { output.Add(polygon); return false; }
            if (anyOutside) intersection = Clip(intersection, prism, plane, true);
            if (intersection.Count < 3) { output.Add(polygon); return false; }
        }
        ClipPoint area = default;
        for (int i = 1; i < intersection.Count - 1; i++)
            area += ClipPoint.Cross(intersection[i] - intersection[0], intersection[i + 1] - intersection[0]);
        if (area.sqrMagnitude <= MinimumAreaSquared) { output.Add(polygon); return false; }

        List<ClipPoint> inside = polygon;
        for (int plane = 0; plane < prism.planes.Length; plane++)
        {
            bool anyOutside = false;
            foreach (ClipPoint p in inside) anyOutside |= prism.Distance(plane, p) < 0f;
            if (!anyOutside) continue;
            List<ClipPoint> outside = Clip(inside, prism, plane, false);
            if (outside.Count >= 3) output.Add(outside);
            inside = Clip(inside, prism, plane, true);
            if (inside.Count < 3) break;
        }
        return true;
    }

    private static List<ClipPoint> Clip(List<ClipPoint> polygon, Prism prism, int plane, bool keepInside)
    {
        var result = new List<ClipPoint>(polygon.Count + 1);
        ClipPoint previous = polygon[polygon.Count - 1];
        double before = prism.Distance(plane, previous) * (keepInside ? 1f : -1f);
        foreach (ClipPoint current in polygon)
        {
            double value = prism.Distance(plane, current) * (keepInside ? 1f : -1f);
            if ((value >= 0f) != (before >= 0f))
                result.Add((previous + (current - previous) * (before / (before - value))));
            if (value >= 0f) result.Add(current);
            previous = current; before = value;
        }
        return result;
    }
}
