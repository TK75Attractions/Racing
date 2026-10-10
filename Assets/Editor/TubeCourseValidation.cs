using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Checks the authored tunnel, the generated road and their shared clearance.</summary>
public static class TubeCourseValidation
{
    [Serializable] private sealed class Report
    {
        public float courseLength;
        public int foldedRoadTriangles;
        public float maximumHeadingChangeOver5Meters;
        public float maximumGrade;
        public float maximumGradeChangeOver5Meters;
        public float retainedMainGroundFraction;
        public int originalTubeVertices;
        public bool originalTubeMeshPreserved;
        public int floorIntersections;
        public int tubeWallObstructions;
        public Vector3[] center;
    }

    [MenuItem("Racing/Validate Tube Course Clearance")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
        bool oldBackfaces = Physics.queriesHitBackfaces;
        try
        {
            RaceCourse course = null;
            MeshFilter tube = null, ground = null;
            var floors = new List<MeshCollider>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<RaceCourse>() != null) course = root.GetComponent<RaceCourse>();
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    MeshFilter source = PrefabUtility.GetCorrespondingObjectFromSource(filter);
                    if (AssetDatabase.GetAssetPath(source) != TubeCoursePreparation.ModelPath) continue;
                    if (filter.name == "BézierCurve.001") tube = filter;
                    if (filter.name == "Plane") ground = filter;
                    MeshCollider collider = filter.GetComponent<MeshCollider>();
                    if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger)
                        floors.Add(collider);
                }
            }
            Require(course != null && tube != null && ground != null, "Tube course scene is incomplete.");
            course.RebuildCache(); course.RebuildRoad(); Physics.SyncTransforms();
            Physics.queriesHitBackfaces = true;
            var report = new Report { courseLength = course.TotalLength };
            var path = new List<Vector3>(); course.CopyCenterPathWorld(path); report.center = path.ToArray();
            Mesh roadMesh = course.GetComponentInChildren<MeshCollider>().sharedMesh;
            Vector3[] vertices = roadMesh.vertices; int[] triangles = roadMesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                if (Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]],
                    vertices[triangles[i + 2]] - vertices[triangles[i]]).y <= 0f) report.foldedRoadTriangles++;
            Mesh original = PrefabUtility.GetCorrespondingObjectFromSource(tube).sharedMesh;
            report.originalTubeVertices = original.vertexCount;
            report.originalTubeMeshPreserved = tube.sharedMesh == original;
            Mesh tubeCollision = tube.GetComponent<MeshCollider>().sharedMesh;
            Require(tubeCollision.vertexCount == original.vertexCount && tubeCollision.triangles.Length == original.triangles.Length * 2,
                "Tube collider must retain the original shape with both face directions.");
            report.retainedMainGroundFraction = Area(ground.sharedMesh) /
                Area(PrefabUtility.GetCorrespondingObjectFromSource(ground).sharedMesh);

            for (float distance = 0f; distance < course.TotalLength; distance += 1f)
            {
                if (!course.HasRoadAtProgress(distance)) continue;
                course.TryGetSampleAtProgress(distance, out var sample);
                bool nearJump = false;
                for (float offset = -8f; offset <= 8f; offset += 1f)
                    if (!course.HasRoadAtProgress(distance + offset)) nearJump = true;
                if (!nearJump)
                {
                    course.TryGetSampleAtProgress(distance - 5f, out var before);
                    course.TryGetSampleAtProgress(distance + 5f, out var after);
                    Vector3 a = sample.position - before.position, b = after.position - sample.position;
                    float ga = Grade(a), gb = Grade(b);
                    report.maximumGrade = Mathf.Max(report.maximumGrade, Mathf.Abs(ga), Mathf.Abs(gb));
                    report.maximumGradeChangeOver5Meters = Mathf.Max(report.maximumGradeChangeOver5Meters, Mathf.Abs(ga - gb));
                    a.y = b.y = 0f;
                    report.maximumHeadingChangeOver5Meters = Mathf.Max(report.maximumHeadingChangeOver5Meters, Vector3.Angle(a, b));
                }
                foreach (float fraction in new[] { -.4f, 0f, .4f })
                {
                    Vector3 point = sample.position + sample.right * (sample.width * fraction);
                    foreach (MeshCollider floor in floors)
                        if (floor.Raycast(new Ray(point + Vector3.up, Vector3.down), out var hit, 1.04f) && hit.point.y > point.y + .04f)
                        {
                            report.floorIntersections++;
                            Debug.LogError($"Floor overlaps road: {floor.name} at {hit.point}");
                        }
                    if (sample.position.y < 2f && tube.GetComponent<MeshCollider>().Raycast(
                        new Ray(point + Vector3.up * .7f, -sample.forward), out var wall, 2f) && Mathf.Abs(wall.normal.y) < .5f)
                        report.tubeWallObstructions++;
                }
            }
            string output = Environment.GetEnvironmentVariable("RACING_TUBE_VALIDATION_OUTPUT") ??
                Path.Combine(Path.GetTempPath(), "racing-tube-quality");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            Require(report.originalTubeMeshPreserved && report.retainedMainGroundFraction > .99f, "Authored terrain was not preserved.");
            Require(report.foldedRoadTriangles == 0 && report.floorIntersections == 0 && report.tubeWallObstructions == 0,
                "Road folds or terrain obstructions remain.");
            Require(report.maximumHeadingChangeOver5Meters < 20f && report.maximumGrade < .5f && report.maximumGradeChangeOver5Meters < .2f,
                "Road alignment contains an abrupt bend or grade transition.");
            Debug.Log($"TUBE_COURSE_VALIDATION_PASS: length={report.courseLength:F1}m, heading/5m={report.maximumHeadingChangeOver5Meters:F2}°, " +
                $"grade={report.maximumGrade:F3}, grade change/5m={report.maximumGradeChangeOver5Meters:F3}, " +
                $"retained ground={report.retainedMainGroundFraction:P2}, road folds/intersections=0.");
        }
        finally { Physics.queriesHitBackfaces = oldBackfaces; EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static float Grade(Vector3 direction) => direction.y / Mathf.Max(.0001f, new Vector2(direction.x, direction.z).magnitude);
    private static float Area(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices; int[] indices = mesh.triangles; float area = 0f;
        for (int i = 0; i < indices.Length; i += 3)
            area += Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]], vertices[indices[i + 2]] - vertices[indices[i]]).magnitude * .5f;
        return area;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
