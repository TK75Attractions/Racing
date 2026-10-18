using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Bakes terrain apertures without changing the supplied tube mesh.</summary>
public static class TubeCoursePreparation
{
    public const string ModelPath = "Assets/Scenes/1009こうしゃチューブ.fbx";
    public const string DataFolder = "Assets/Scenes/TubeCourseData";

    private struct Vertex
    {
        public Vector3 position;
        public Vector2 uv;
        public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex
        { position = Vector3.Lerp(a.position, b.position, t), uv = Vector2.Lerp(a.uv, b.uv, t) };
    }

    [MenuItem("Racing/Prepare Tube Course Terrain")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Open SampleScene first.");
            scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        }
        MeshFilter tube = null, ground = null;
        var entranceGrounds = new List<MeshFilter>();
        var trees = new List<MeshCollider>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(filter)) != ModelPath) continue;
                if (filter.name == "BézierCurve.001") tube = filter;
                if (filter.name == "Plane") ground = filter;
                if (filter.name == "Plane.012" || filter.name == "Plane.009" || filter.name == "Cube.001") entranceGrounds.Add(filter);
                if (filter.name.StartsWith("tree", StringComparison.Ordinal) && filter.GetComponent<MeshCollider>() != null)
                    trees.Add(filter.GetComponent<MeshCollider>());
            }
        if (tube == null || ground == null) throw new InvalidOperationException("Tube or ground is missing.");
        Directory.CreateDirectory(DataFolder);
        AssetDatabase.Refresh();
        // Always start from the unchanged imported mesh, so repeated baking is idempotent.
        Mesh source = PrefabUtility.GetCorrespondingObjectFromSource(tube).sharedMesh;
        List<List<Vector3>> rings = ReadRings(source, tube.transform);
        Mesh originalGround = PrefabUtility.GetCorrespondingObjectFromSource(ground).sharedMesh;
        float groundHeight = ground.transform.TransformPoint(originalGround.bounds.center).y;
        List<Vector2[]> cuts = BuildGroundApertures(rings, groundHeight);
        Mesh clearedGround = ClipGround(originalGround, ground.transform, cuts);
        Mesh groundAsset = SaveMesh(clearedGround, DataFolder + "/GroundWithTubeApertures.asset");
        ground.sharedMesh = groundAsset;
        ground.GetComponent<MeshCollider>().sharedMesh = groundAsset;
        PrefabUtility.RecordPrefabInstancePropertyModifications(ground);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ground.GetComponent<MeshCollider>());

        RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        if (course == null) throw new InvalidOperationException("RaceCourse is missing.");
        course.RebuildCache();
        ClearRoadsideBranches(course, trees);
        foreach (MeshFilter floor in entranceGrounds)
        {
            Mesh original = PrefabUtility.GetCorrespondingObjectFromSource(floor).sharedMesh;
            List<Vector2[]> roadCuts = BuildRoadClearance(course, floor.GetComponent<MeshRenderer>().bounds);
            Mesh asset = SaveMesh(ClipGround(original, floor.transform, roadCuts),
                DataFolder + "/" + floor.name.Replace(".", "") + "RoadClearance.asset");
            floor.sharedMesh = asset;
            floor.GetComponent<MeshCollider>().sharedMesh = asset;
            PrefabUtility.RecordPrefabInstancePropertyModifications(floor);
            PrefabUtility.RecordPrefabInstancePropertyModifications(floor.GetComponent<MeshCollider>());
        }

        // FBX tube normals point outward. Keep the visual geometry intact and make its
        // collision surface usable from inside as well as outside.
        var tubeCollision = new Mesh { name = "Tube double sided collision", indexFormat = source.indexFormat };
        tubeCollision.vertices = source.vertices;
        int[] originalTriangles = source.triangles;
        int[] triangles = new int[originalTriangles.Length * 2];
        Array.Copy(originalTriangles, triangles, originalTriangles.Length);
        for (int i = 0; i < originalTriangles.Length; i += 3)
        {
            int offset = originalTriangles.Length + i;
            triangles[offset] = originalTriangles[i];
            triangles[offset + 1] = originalTriangles[i + 2];
            triangles[offset + 2] = originalTriangles[i + 1];
        }
        tubeCollision.triangles = triangles;
        tubeCollision.RecalculateBounds();
        MeshCollider collider = tube.GetComponent<MeshCollider>();
        collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation |
            MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;
        collider.sharedMesh = SaveMesh(tubeCollision, DataFolder + "/TubeInteriorCollision.asset");
        PrefabUtility.RecordPrefabInstancePropertyModifications(collider);

        string materialPath = DataFolder + "/TubeInterior.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Tube Interior" };
            Material imported = tube.GetComponent<MeshRenderer>().sharedMaterial;
            Color color = imported != null && imported.HasProperty("_BaseColor") ? imported.GetColor("_BaseColor") :
                imported != null && imported.HasProperty("_Color") ? imported.color : new Color(.42f, .46f, .5f);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .15f);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(material);
        MeshRenderer renderer = tube.GetComponent<MeshRenderer>();
        var materials = new Material[source.subMeshCount];
        for (int i = 0; i < materials.Length; i++) materials[i] = material;
        renderer.sharedMaterials = materials;
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        AlignRaceMarkers(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"TUBE_TERRAIN_PREPARATION_PASS: {rings.Count} rings, {cuts.Count} terrain apertures, original tube vertices retained.");
    }

    private static void AlignRaceMarkers(UnityEngine.SceneManagement.Scene scene)
    {
        RaceCourse course = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.GetComponent<RaceCourse>() != null) course = root.GetComponent<RaceCourse>();
        if (course == null) return;
        course.RebuildCache(); course.RebuildRoad();
        var path = new List<Vector3>(); course.CopyCenterPathWorld(path);
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform marker in root.GetComponentsInChildren<Transform>(true))
            {
                bool checkpoint = marker.GetComponent<CheckpointSensor>() != null;
                bool goal = marker.GetComponent<GoalSensor>() != null;
                bool boxes = marker.name.StartsWith("Item Box Row", StringComparison.Ordinal);
                bool crystal = marker.name.StartsWith("Charge Crystal", StringComparison.Ordinal);
                if (!checkpoint && !goal && !boxes && !crystal) continue;
                float nearest = float.PositiveInfinity, progress = 0f;
                Vector3 point = marker.position, direction = marker.forward;
                for (int i = 1; i < path.Count; i++)
                {
                    Vector3 delta = path[i] - path[i - 1];
                    float length = delta.magnitude;
                    bool road = course.HasRoadAtProgress(progress + length * .5f);
                    progress += length;
                    if (!road) continue;
                    Vector2 start = new Vector2(path[i - 1].x, path[i - 1].z);
                    Vector2 segment = new Vector2(delta.x, delta.z);
                    if (segment.sqrMagnitude < .000001f) continue;
                    Vector2 target = new Vector2(marker.position.x, marker.position.z);
                    float t = Mathf.Clamp01(Vector2.Dot(target - start, segment) / segment.sqrMagnitude);
                    float distance = (target - start - segment * t).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance; point = Vector3.Lerp(path[i - 1], path[i], t); direction = -delta;
                }
                marker.position = point + Vector3.up * (checkpoint || goal ? 2f : boxes ? 3f : .01f);
                if (checkpoint || boxes)
                {
                    direction.y = 0f;
                    if (direction.sqrMagnitude > .000001f) marker.rotation = Quaternion.LookRotation(direction, Vector3.up);
                }
                EditorUtility.SetDirty(marker);
            }
    }

    public static List<List<Vector3>> ReadRings(Mesh mesh, Transform transform)
    {
        Vector3[] vertices = mesh.vertices;
        Vector2[] uv = mesh.uv;
        var groups = new SortedDictionary<int, SortedDictionary<int, Vector3>>();
        for (int i = 0; i < vertices.Length; i++)
        {
            if (uv[i].y > .999f) continue; // Profile seam duplicates the first point.
            int longitudinal = Mathf.RoundToInt(uv[i].x * 120f);
            int profile = Mathf.RoundToInt(uv[i].y * 34f);
            if (!groups.TryGetValue(longitudinal, out var ring))
                groups.Add(longitudinal, ring = new SortedDictionary<int, Vector3>());
            ring[profile] = transform.TransformPoint(vertices[i]);
        }
        var result = new List<List<Vector3>>();
        foreach (var ring in groups.Values) result.Add(new List<Vector3>(ring.Values));
        return result;
    }

    private static List<Vector2[]> BuildRoadClearance(RaceCourse course, Bounds bounds)
    {
        var result = new List<Vector2[]>();
        for (float distance = 0f; distance < course.TotalLength; distance += 2f)
        {
            if (!course.HasRoadAtProgress(distance + 1f)) continue;
            course.TryGetSampleAtProgress(distance, out var a);
            course.TryGetSampleAtProgress(Mathf.Min(distance + 2f, course.TotalLength), out var b);
            // Keep the surrounding floor; only the road footprint needs clearance.
            if (Mathf.Min(a.position.y, b.position.y) > bounds.max.y + .5f) continue;
            float aw = a.width * .5f + .5f, bw = b.width * .5f + .5f;
            Vector3 al = a.position - a.right * aw, ar = a.position + a.right * aw;
            Vector3 bl = b.position - b.right * bw, br = b.position + b.right * bw;
            if (Mathf.Max(Mathf.Max(al.x, ar.x), Mathf.Max(bl.x, br.x)) < bounds.min.x ||
                Mathf.Min(Mathf.Min(al.x, ar.x), Mathf.Min(bl.x, br.x)) > bounds.max.x ||
                Mathf.Max(Mathf.Max(al.z, ar.z), Mathf.Max(bl.z, br.z)) < bounds.min.z ||
                Mathf.Min(Mathf.Min(al.z, ar.z), Mathf.Min(bl.z, br.z)) > bounds.max.z) continue;
            Vector2[] polygon = { new Vector2(al.x, al.z), new Vector2(ar.x, ar.z),
                new Vector2(br.x, br.z), new Vector2(bl.x, bl.z) };
            float area = 0f;
            for (int i = 0; i < polygon.Length; i++) area += Cross(polygon[i], polygon[(i + 1) % polygon.Length]);
            if (area < 0f) Array.Reverse(polygon);
            result.Add(polygon);
        }
        return result;
    }

    private static void ClearRoadsideBranches(RaceCourse course, List<MeshCollider> trees)
    {
        bool oldBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            for (float distance = 0f; distance < course.TotalLength; distance += 1f)
            {
                if (!course.HasRoadAtProgress(distance)) continue;
                course.TryGetSampleAtProgress(distance, out var sample);
                foreach (float fraction in new[] { -.45f, -.4f, 0f, .4f, .45f })
                {
                    Vector3 point = sample.position + sample.right * (sample.width * fraction);
                    foreach (MeshCollider tree in trees)
                    {
                        if (!tree.enabled || !tree.Raycast(new Ray(point + Vector3.up * 3f, Vector3.down), out var hit, 3f)) continue;
                        // The imported tree combines leaves/branches/trunk in one collider.
                        // Retain its renderer while preventing hanging branches from snagging a car.
                        tree.enabled = false;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(tree);
                    }
                }
            }
        }
        finally { Physics.queriesHitBackfaces = oldBackfaces; }
    }

    private static List<Vector2[]> BuildGroundApertures(List<List<Vector3>> rings, float height)
    {
        var sections = new List<Vector2[]>();
        foreach (var ring in rings)
        {
            var crossings = new List<Vector2>();
            for (int i = 0; i < ring.Count; i++)
            {
                Vector3 a = ring[i], b = ring[(i + 1) % ring.Count];
                if ((a.y <= height) == (b.y <= height)) continue;
                Vector3 p = Vector3.Lerp(a, b, (height - a.y) / (b.y - a.y));
                crossings.Add(new Vector2(p.x, p.z));
            }
            if (crossings.Count >= 2) sections.Add(new[] { crossings[0], crossings[1] });
            else sections.Add(null);
        }
        var polygons = new List<Vector2[]>();
        for (int i = 1; i < sections.Count; i++)
        {
            if (sections[i - 1] == null || sections[i] == null) continue;
            Vector2 a = sections[i - 1][0], b = sections[i - 1][1];
            Vector2 c = sections[i][0], d = sections[i][1];
            // Order each cross section consistently even when the authored tube backtracks.
            if ((a - c).sqrMagnitude + (b - d).sqrMagnitude > (a - d).sqrMagnitude + (b - c).sqrMagnitude)
            { Vector2 swap = c; c = d; d = swap; }
            Vector2[] polygon = { a, b, d, c };
            float area = 0f;
            for (int j = 0; j < polygon.Length; j++) area += Cross(polygon[j], polygon[(j + 1) % polygon.Length]);
            if (Mathf.Abs(area) < .001f) continue;
            if (area < 0f) Array.Reverse(polygon);
            polygons.Add(polygon);
        }
        return polygons;
    }

    private static Mesh ClipGround(Mesh source, Transform transform, List<Vector2[]> cuts)
    {
        Vector3[] input = source.vertices;
        Vector2[] inputUV = source.uv;
        var vertices = new List<Vector3>(); var uv = new List<Vector2>();
        var submeshes = new List<int>[source.subMeshCount];
        for (int submesh = 0; submesh < source.subMeshCount; submesh++)
        {
            submeshes[submesh] = new List<int>();
            int[] indices = source.GetTriangles(submesh);
            for (int i = 0; i < indices.Length; i += 3)
            {
                var triangle = new List<Vertex>();
                for (int j = 0; j < 3; j++)
                {
                    int index = indices[i + j];
                    triangle.Add(new Vertex { position = transform.TransformPoint(input[index]), uv = inputUV.Length == input.Length ? inputUV[index] : Vector2.zero });
                }
                var pieces = new List<List<Vertex>> { triangle };
                foreach (Vector2[] cut in cuts)
                {
                    var next = new List<List<Vertex>>();
                    foreach (List<Vertex> piece in pieces) Subtract(piece, cut, next);
                    pieces = next;
                }
                foreach (List<Vertex> piece in pieces)
                {
                    int first = vertices.Count;
                    foreach (Vertex vertex in piece) { vertices.Add(transform.InverseTransformPoint(vertex.position)); uv.Add(vertex.uv); }
                    for (int j = 1; j < piece.Count - 1; j++)
                    {
                        if (Vector3.Cross(piece[j].position - piece[0].position, piece[j + 1].position - piece[0].position).sqrMagnitude < .00000001f) continue;
                        submeshes[submesh].Add(first); submeshes[submesh].Add(first + j); submeshes[submesh].Add(first + j + 1);
                    }
                }
            }
        }
        Mesh mesh = new Mesh { name = "Ground retaining tube clearance", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.subMeshCount = submeshes.Length;
        for (int i = 0; i < submeshes.Length; i++) mesh.SetTriangles(submeshes[i], i);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    private static void Subtract(List<Vertex> polygon, Vector2[] cut, List<List<Vertex>> output)
    {
        List<Vertex> inside = polygon;
        for (int i = 0; i < cut.Length && inside.Count >= 3; i++)
        {
            Vector2 a = cut[i], b = cut[(i + 1) % cut.Length];
            List<Vertex> outside = Clip(inside, a, b, false);
            if (outside.Count >= 3) output.Add(outside);
            inside = Clip(inside, a, b, true);
        }
    }

    private static List<Vertex> Clip(List<Vertex> polygon, Vector2 a, Vector2 b, bool keepInside)
    {
        var result = new List<Vertex>();
        Vertex previous = polygon[polygon.Count - 1];
        float before = Cross(b - a, new Vector2(previous.position.x, previous.position.z) - a);
        if (!keepInside) before = -before;
        foreach (Vertex current in polygon)
        {
            float value = Cross(b - a, new Vector2(current.position.x, current.position.z) - a);
            if (!keepInside) value = -value;
            if ((value >= 0f) != (before >= 0f)) result.Add(Vertex.Lerp(previous, current, before / (before - value)));
            if (value >= 0f) result.Add(current);
            previous = current; before = value;
        }
        return result;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static Mesh SaveMesh(Mesh mesh, string path)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }
}
