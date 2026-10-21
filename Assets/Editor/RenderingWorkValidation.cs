using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Regression checks for rendering optimizations that retain all geometry.</summary>
public static class RenderingWorkValidation
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [MenuItem("Racing/Validation/Rendering Work")]
    public static void Run()
    {
        ValidateScenery();
        ValidateMarks();
        ValidateLightningReuse();
        Debug.Log("RENDERING_WORK_VALIDATION_PASS: identical scenery faces, normals and materials; regional culling; tire buffer wrap/clear/refill; lightning reuse.");
    }

    private static void ValidateScenery()
    {
        var builder = new CampusSceneryMesh(3);
        builder.Box(new Vector3(-220, 2, -190), new Vector3(8, 4, 9), 0, 33);
        builder.Ellipsoid(new Vector3(220, 8, 200), new Vector3(5, 8, 4), 1, .47f);
        builder.Branch(new Vector3(95, 0, 95), new Vector3(101, 12, 99), .5f, .1f, 2);
        builder.Ring(new Vector3(350, 0, 400), new Vector2(70, 50), new Vector2(68, 48), 2);
        builder.Box(Vector3.down, new Vector3(1000, .2f, 1000), 0);
        Mesh original = builder.Build();
        List<Mesh> chunks = builder.BuildChunks();
        try
        {
            var expected = Faces(original);
            int vertices = 0;
            foreach (Mesh chunk in chunks)
            {
                vertices += chunk.vertexCount;
                foreach (var pair in Faces(chunk))
                {
                    Require(expected.TryGetValue(pair.Key, out int count) && count >= pair.Value,
                        "Chunking must preserve every vertex, normal, winding and material.");
                    expected[pair.Key] -= pair.Value;
                }
            }
            foreach (int count in expected.Values) Require(count == 0, "No scenery faces may disappear.");
            Require(vertices == original.vertexCount, "Chunking must not simplify or duplicate geometry.");
            Require(chunks.Count > 2, "Distant scenery must have independent culling bounds.");

            var cameraRoot = new GameObject("Scenery culling validation");
            try
            {
                Camera camera = cameraRoot.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 25;
                camera.aspect = 1;
                camera.farClipPlane = 100;
                camera.transform.position = new Vector3(-220, 15, -240);
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
                int visible = 0;
                foreach (Mesh chunk in chunks)
                    if (GeometryUtility.TestPlanesAABB(planes, chunk.bounds)) visible += chunk.vertexCount;
                Require(GeometryUtility.TestPlanesAABB(planes, original.bounds), "The original global batch must be visible.");
                Require(visible > 0 && visible < vertices / 2, "Offscreen regions must leave the draw set without a distance cutoff.");
                Debug.Log($"SCENERY_CULLING: identical total {vertices} vertices; representative view {visible} vertices; {chunks.Count} regions.");
            }
            finally { Object.DestroyImmediate(cameraRoot); }
        }
        finally
        {
            Object.DestroyImmediate(original);
            foreach (Mesh chunk in chunks) Object.DestroyImmediate(chunk);
        }
    }

    // Preserve exact float bits when comparing faces, including shared/coplanar faces.
    private static Dictionary<string, int> Faces(Mesh mesh)
    {
        var result = new Dictionary<string, int>();
        Vector3[] positions = mesh.vertices, normals = mesh.normals;
        for (int material = 0; material < mesh.subMeshCount; material++)
        {
            int[] indices = mesh.GetTriangles(material);
            for (int i = 0; i < indices.Length; i += 3)
            {
                string key = material.ToString();
                for (int j = 0; j < 3; j++)
                {
                    int index = indices[i + j];
                    foreach (Vector3 vector in new[] { positions[index], normals[index] })
                        key += $"/{BitConverter.SingleToInt32Bits(vector.x)}/{BitConverter.SingleToInt32Bits(vector.y)}/{BitConverter.SingleToInt32Bits(vector.z)}";
                }
                result.TryGetValue(key, out int count);
                result[key] = count + 1;
            }
        }
        return result;
    }

    private static void ValidateMarks()
    {
        TireMarkRenderer marks = TireMarkRenderer.GetOrCreate();
        try
        {
            AddMark(marks, -1); // Initialize explicitly; Edit Mode does not run Awake.
            marks.Clear();
            Mesh mesh = marks.GetComponent<MeshFilter>().sharedMesh;
            Flush(marks);
            Require(mesh.GetIndexCount(0) == 0, "Empty buffers must not draw unused marks.");
            for (int i = 0; i < marks.MaxSegments - 2; i++) AddMark(marks, i);
            Flush(marks);
            Vector3[] before = mesh.vertices;
            for (int i = 0; i < 5; i++) AddMark(marks, 10000 + i);
            Flush(marks);
            Vector3[] after = mesh.vertices;
            Require(marks.SegmentCount == marks.MaxSegments && mesh.GetIndexCount(0) == marks.MaxSegments * 6,
                "The ring must retain its full capacity.");
            for (int i = 3; i < marks.MaxSegments - 2; i++)
                Require(after[i * 4] == before[i * 4], "Partial uploads must leave older marks untouched.");
            Require(after[0].z == 10002 && after[8].z == 10004 && after[(marks.MaxSegments - 2) * 4].z == 10000,
                "Both sides of a wrapped update must reach the GPU buffer.");
            Require(mesh.colors32[0].a == 127 && mesh.uv[3] == Vector2.one,
                "Partial uploads must preserve colour and immutable UV streams.");

            // More than one entire ring can arrive before the next LateUpdate.
            for (int i = 0; i < marks.MaxSegments + 5; i++) AddMark(marks, 20000 + i);
            Flush(marks);
            Vector3[] cpu = (Vector3[])Field(marks, "vertices");
            after = mesh.vertices;
            for (int i = 0; i < after.Length; i++) Require(after[i] == cpu[i], "A saturated update must upload every slot.");
            AddMark(marks, 30000);
            marks.Clear();
            Flush(marks);
            Require(mesh.GetIndexCount(0) == 0, "Clear must discard pending updates.");
            AddMark(marks, 40000);
            Flush(marks);
            Require(mesh.GetIndexCount(0) == 6 && mesh.vertices[0].z == 40000,
                "Refilling after clear must draw only new marks.");
        }
        finally { Object.DestroyImmediate(marks.gameObject); }
    }

    private static void ValidateLightningReuse()
    {
        var root = new GameObject("Lightning reuse validation");
        LightningEffects effects = root.AddComponent<LightningEffects>();
        typeof(LightningEffects).GetMethod("Awake", Private).Invoke(effects, null);
        MethodInfo spawn = typeof(LightningEffects).GetMethod("SpawnBolt", Private);
        MethodInfo release = typeof(LightningEffects).GetMethod("ReleaseAllBolts", Private);
        try
        {
            var style = new LightningStyle { subdivisions = 5, branchCount = 3, duration = .3f,
                width = .1f, color = Color.cyan, lightIntensity = 2, lightRange = 5 };
            Func<bool> strike = () => (bool)spawn.Invoke(effects, new object[] {
                new LightningAnchor(Vector3.zero), new LightningAnchor(Vector3.right * 10), style });
            Require(strike(), "A valid strike must spawn.");
            IList active = (IList)Field(effects, "activeBolts");
            object bolt = active[0];
            IList strands = (IList)PublicField(bolt, "strands");
            var arrays = new HashSet<object>();
            foreach (object strand in strands) arrays.Add(PublicField(strand, "world"));
            bolt.GetType().GetField("age").SetValue(bolt, .2f);
            release.Invoke(effects, null);
            Require(effects.ActiveBoltCount == 0, "Clearing must release all strikes.");
            Require(strike() && ReferenceEquals(active[0], bolt), "Repeated strikes must reuse bolt state.");
            foreach (object strand in (IList)PublicField(bolt, "strands"))
                Require(arrays.Contains(PublicField(strand, "world")), "Repeated strikes must reuse position arrays.");
            Require((float)PublicField(bolt, "age") == 0, "A reused bolt must restart its lifetime.");
            release.Invoke(effects, null);
            for (int subdivisions = 1; subdivisions <= 7; subdivisions++)
            {
                style.subdivisions = subdivisions;
                Require(strike(), "All subdivision levels must remain supported.");
                foreach (object strand in (IList)PublicField(active[0], "strands"))
                    Require(((Vector3[])PublicField(strand, "world")).Length == (int)PublicField(strand, "count"),
                        "Changing style must select an array of the exact rendered length.");
                release.Invoke(effects, null);
            }
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void AddMark(TireMarkRenderer marks, int z) => marks.AddSegment(
        new Vector3(-.1f, 0, z), new Vector3(.1f, 0, z), .5f,
        new Vector3(-.1f, 0, z + 1), new Vector3(.1f, 0, z + 1), 1);
    private static void Flush(TireMarkRenderer marks) => typeof(TireMarkRenderer).GetMethod("LateUpdate", Private).Invoke(marks, null);
    private static object Field(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    private static object PublicField(object target, string name) => target.GetType().GetField(name).GetValue(target);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
