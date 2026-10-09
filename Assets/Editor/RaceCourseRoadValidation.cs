using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RaceCourseRoadValidation
{
    [MenuItem("Racing/Validate Race Course Road")]
    public static void Run()
    {
        GameObject root = new GameObject("Road validation");
        try
        {
            RaceCourse course = root.AddComponent<RaceCourse>();
            SetPath(course, new[] { Vector3.zero, Vector3.forward * 30f }, false);
            ValidateSurface(course);
            ValidatePaint(course);
            ValidateRebuildAndUndo(course);
            ValidateLoopAndSlope(course);
            ValidateEmptyAndToggles(course);
            ValidateVehicleSupport();
            RaceCourseValidation.Run();
            Debug.Log("RACE_COURSE_ROAD_VALIDATION_PASS: exact band mesh, upward collision, dash gaps, both red/white curbs, materials, slope, closed seam, transforms, undo, cleanup, narrow/empty paths, Rigidbody support and existing course regression checks.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static Transform Road(RaceCourse course) => course.transform.Find(RaceCourseRoad.RootName);
    private static Mesh Surface(RaceCourse course) => Road(course).Find("Road Surface").GetComponent<MeshFilter>().sharedMesh;
    private static Mesh Paint(RaceCourse course) => Road(course).Find("Road Markings").GetComponent<MeshFilter>().sharedMesh;
    private static MeshCollider Collider(RaceCourse course) => Road(course).Find("Road Surface").GetComponent<MeshCollider>();

    private static void ValidateSurface(RaceCourse course, bool flatStraight = true)
    {
        Mesh mesh = Surface(course);
        var left = new List<Vector3>(); var right = new List<Vector3>();
        course.CopyCourseBandWorld(left, right);
        Require(mesh.vertexCount == left.Count * 2, "Road uses course band vertices.");
        for (int i = 0; i < left.Count; i++)
        {
            Near(course.transform.TransformPoint(mesh.vertices[i * 2]), left[i], "Left width boundary");
            Near(course.transform.TransformPoint(mesh.vertices[i * 2 + 1]), right[i], "Right width boundary");
        }
        MeshCollider collider = Collider(course);
        Require(collider.sharedMesh == mesh && !collider.convex && !collider.isTrigger, "Physical surface uses render mesh.");
        Physics.SyncTransforms();
        if (flatStraight)
        {
            Require(collider.Raycast(new Ray(new Vector3(0f, 10f, 10f), Vector3.down), out RaycastHit hit, 20f), "Road blocks downward rays.");
            Near(hit.point, new Vector3(0f, 0f, 10f), "Physical height");
            Require(hit.normal.y > 0.99f, "Collision faces upward.");
            Require(!collider.Raycast(new Ray(new Vector3(5.1f, 10f, 10f), Vector3.down), out _, 20f), "No invisible collider outside road.");
        }
        Require(Road(course).GetComponentsInChildren<Collider>().Length == 1, "Paint does not add wheel-catching colliders.");
        foreach (MeshRenderer renderer in Road(course).GetComponentsInChildren<MeshRenderer>())
            foreach (Material material in renderer.sharedMaterials)
                Require(material != null && material.shader != null && material.shader.name == "Universal Render Pipeline/Lit", "Build-safe road materials.");
    }

    private static void ValidatePaint(RaceCourse course)
    {
        Mesh mesh = Paint(course);
        Require(PaintedAt(mesh, new Vector2(0f, 1f), 0), "Central white dash.");
        Require(!PaintedAt(mesh, new Vector2(0f, 4.5f), 0), "Central dash gap.");
        foreach (float x in new[] { -4.8f, 4.8f })
        {
            Require(PaintedAt(mesh, new Vector2(x, 1f), 1), "Both curbs start red.");
            Require(PaintedAt(mesh, new Vector2(x, 3f), 0), "Both curbs alternate white.");
        }
        foreach (Vector3 vertex in mesh.vertices)
            Require(Mathf.Abs(vertex.x) <= 5.001f && vertex.y > 0f, "Paint stays inside road, above surface.");
    }

    private static void ValidateRebuildAndUndo(RaceCourse course)
    {
        Mesh old = Surface(course);
        Undo.IncrementCurrentGroup();
        SerializedObject serialized = new SerializedObject(course);
        serialized.FindProperty("waypoints").GetArrayElementAtIndex(0).FindPropertyRelative("width").floatValue = 20f;
        serialized.ApplyModifiedProperties();
        Undo.FlushUndoRecordObjects();
        course.RebuildRoad();
        Require(old == null, "Rebuild releases previous mesh.");
        Near(Surface(course).vertices[0].x, -10f, "Width edit regenerates road.");
        Undo.PerformUndo();
        course.RebuildCache(); course.RebuildRoad();
        Near(Surface(course).vertices[0].x, -5f, "Undo restores road width.");
        Undo.ClearUndo(course);
        course.transform.SetPositionAndRotation(new Vector3(12f, 7f, -20f), Quaternion.Euler(0f, 65f, 0f));
        course.transform.localScale = new Vector3(2f, 3f, 4f);
        typeof(RaceCourse).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(course, null);
        Require(course.TryGetSampleAtProgress(course.TotalLength * 0.5f, out RaceCourse.CourseSample sample), "Transformed road sample.");
        Physics.SyncTransforms();
        Require(Collider(course).Raycast(new Ray(sample.position + Vector3.up * 10f, Vector3.down), out RaycastHit hit, 20f), "Transformed road collision.");
        Near(hit.point, sample.position, "Transformed surface height");
        Require(course.transform.childCount == 1, "Rebuild leaves exactly one generated root.");
        course.gameObject.layer = 8;
        typeof(RaceCourse).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(course, null);
        Require(Collider(course).gameObject.layer == 8, "Changing the course layer updates road collision layer.");
        course.gameObject.layer = 0;
        course.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        course.transform.localScale = Vector3.one;
    }

    private static void ValidateLoopAndSlope(RaceCourse course)
    {
        SetPath(course, new[] { Vector3.zero, new Vector3(0f, 20f, 40f) }, false, 8f, 16f);
        ValidateSurface(course, false);
        course.TryGetSampleAtProgress(course.TotalLength * 0.5f, out RaceCourse.CourseSample sample);
        Physics.SyncTransforms();
        Require(Collider(course).Raycast(new Ray(sample.position + Vector3.up * 10f, Vector3.down), out RaycastHit hit, 20f), "Slope collision.");
        Near(hit.point, sample.position, "Slope height");
        Require(hit.normal.y > 0.5f && hit.normal.z < -0.1f, "Slope normal supports wheels.");

        SetPath(course, new[] { Vector3.zero, new Vector3(0f, 0f, 30f), new Vector3(30f, 0f, 30f), new Vector3(30f, 0f, 0f) }, true);
        Mesh mesh = Surface(course);
        Near(mesh.vertices[0], mesh.vertices[mesh.vertexCount - 2], "Loop left seam");
        Near(mesh.vertices[1], mesh.vertices[mesh.vertexCount - 1], "Loop right seam");
        Near(mesh.normals[0], mesh.normals[mesh.vertexCount - 2], "Loop normals");
        Require(Paint(course).GetTriangles(0).Length > 0 && Paint(course).GetTriangles(1).Length > 0, "Closed road has red and white paint.");

        SetPath(course, new[] { Vector3.zero, new Vector3(20f, 10f, 30f), new Vector3(-15f, 0f, 60f) }, false, 6f, 18f);
        SerializedObject serialized = new SerializedObject(course);
        serialized.FindProperty("waypoints").GetArrayElementAtIndex(0).FindPropertyRelative("curve").floatValue = 8f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        course.RebuildRoad();
        // A sharp turn can overlap neighboring surface triangles. Check the source planes
        // rather than requiring a downward ray to select one particular overlapping triangle.
        Physics.SyncTransforms();
        foreach (Vector3 vertex in Paint(course).vertices)
        {
            Vector3 world = course.transform.TransformPoint(vertex);
            Require(course.IsPointInsideCourse(world), "Curved paint stays in the defined band.");
        }
        Mesh paint = Paint(course);
        Vector3[] vertices = paint.vertices;
        int[] indices = paint.triangles;
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 local = (vertices[indices[i]] + vertices[indices[i + 1]] + vertices[indices[i + 2]]) / 3f;
            Vector3 world = course.transform.TransformPoint(local);
            Require(OnSurface(Surface(course), world - Vector3.up * 0.01f), "Paint follows the exact road plane.");
        }
    }

    private static void ValidateEmptyAndToggles(RaceCourse course)
    {
        SetPath(course, new[] { Vector3.zero, Vector3.forward * 10f }, false, 0.05f, 0.05f);
        foreach (Vector3 vertex in Paint(course).vertices) Require(Mathf.Abs(vertex.x) <= 0.02501f, "Narrow road clamps paint width.");
        course.enabled = false;
        Require(Road(course) == null, "Disabling component cleans up generated objects.");
        course.enabled = true;
        Require(Road(course) != null, "Re-enabling component restores road.");
        SerializedObject serialized = new SerializedObject(course);
        serialized.FindProperty("road").FindPropertyRelative("generateCollider").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo(); course.RebuildRoad();
        Require(Road(course).GetComponentsInChildren<Collider>().Length == 0, "Collider toggle.");
        serialized.Update();
        serialized.FindProperty("generateRoad").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo(); course.RebuildRoad();
        Require(Road(course) == null, "Disabling generation removes road.");
        serialized.Update();
        serialized.FindProperty("generateRoad").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        SetPath(course, new[] { Vector3.zero, Vector3.zero }, false);
        Require(Road(course) == null, "Duplicate-only course clears road.");
        SetPath(course, Array.Empty<Vector3>(), false);
        Require(Road(course) == null, "Empty course clears road.");
        SetPath(course, new[] { Vector3.zero, Vector3.forward * 10f }, false, 0f, 0f);
        Require(Road(course) == null, "Zero-width course has no surface or collider.");
    }

    private static void ValidateVehicleSupport()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject road = new GameObject("Physical road");
            SceneManager.MoveGameObjectToScene(road, scene);
            RaceCourse course = road.AddComponent<RaceCourse>();
            SetPath(course, new[] { Vector3.zero, Vector3.forward * 30f }, false);
            GameObject car = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(car, scene);
            car.transform.position = new Vector3(0f, 3f, 10f);
            Rigidbody body = car.AddComponent<Rigidbody>();
            body.constraints = RigidbodyConstraints.FreezeRotation;
            Physics.SyncTransforms();
            PhysicsScene physics = scene.GetPhysicsScene();
            Require(physics.IsValid() && physics != Physics.defaultPhysicsScene, "Vehicle test must use isolated preview physics.");
            for (int step = 0; step < 150; step++) physics.Simulate(0.02f);
            Require(Mathf.Abs(body.position.y - 0.5f) < 0.08f, "Rigidbody rests on generated road instead of falling through.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void SetPath(RaceCourse course, Vector3[] positions, bool loop, float startWidth = 10f, float endWidth = 10f)
    {
        SerializedObject serialized = new SerializedObject(course);
        serialized.FindProperty("closedLoop").boolValue = loop;
        SerializedProperty points = serialized.FindProperty("waypoints");
        points.arraySize = positions.Length;
        for (int i = 0; i < positions.Length; i++)
        {
            SerializedProperty point = points.GetArrayElementAtIndex(i);
            point.FindPropertyRelative("position").vector2Value = new Vector2(positions[i].x, positions[i].z);
            point.FindPropertyRelative("height").floatValue = positions[i].y;
            point.FindPropertyRelative("curve").floatValue = 0f;
            point.FindPropertyRelative("width").floatValue = Mathf.Lerp(startWidth, endWidth, positions.Length > 1 ? i / (float)(positions.Length - 1) : 0f);
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        course.RebuildCache(); course.RebuildRoad();
    }

    private static bool PaintedAt(Mesh mesh, Vector2 point, int material)
    {
        Vector3[] vertices = mesh.vertices;
        int[] indices = mesh.GetTriangles(material);
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a3 = vertices[indices[i]], b3 = vertices[indices[i + 1]], c3 = vertices[indices[i + 2]];
            Vector2 a = new Vector2(a3.x, a3.z), b = new Vector2(b3.x, b3.z), c = new Vector2(c3.x, c3.z);
            float determinant = Cross(b - a, c - a);
            if (Mathf.Abs(determinant) < 0.000001f) continue;
            float u = Cross(point - a, c - a) / determinant;
            float v = Cross(b - a, point - a) / determinant;
            if (u >= -0.00001f && v >= -0.00001f && u + v <= 1.00001f) return true;
        }
        return false;
    }

    private static bool OnSurface(Mesh mesh, Vector3 point)
    {
        Vector3[] vertices = mesh.vertices;
        int[] indices = mesh.triangles;
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
            Vector3 ab = b - a, ac = c - a, ap = point - a;
            Vector3 normal = Vector3.Cross(ab, ac).normalized;
            if (Mathf.Abs(Vector3.Dot(ap, normal)) > 0.001f) continue;
            float dot00 = Vector3.Dot(ab, ab), dot01 = Vector3.Dot(ab, ac), dot11 = Vector3.Dot(ac, ac);
            float denominator = dot00 * dot11 - dot01 * dot01;
            if (Mathf.Abs(denominator) < 0.0000001f) continue;
            float dot20 = Vector3.Dot(ap, ab), dot21 = Vector3.Dot(ap, ac);
            float u = (dot11 * dot20 - dot01 * dot21) / denominator;
            float v = (dot00 * dot21 - dot01 * dot20) / denominator;
            if (u >= -0.0001f && v >= -0.0001f && u + v <= 1.0001f) return true;
        }
        return false;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    private static void Near(Vector3 a, Vector3 b, string message) => Require(Vector3.Distance(a, b) < 0.01f, message);
    private static void Near(float a, float b, string message) => Require(Mathf.Abs(a - b) < 0.01f, message);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
