using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Temporary verification instrumentation. Copy into the isolated Editor folder only.</summary>
public static class CourseLayoutSnapshot
{
    [Serializable] private sealed class RendererState
    {
        public string path;
        public bool enabled, active;
        public Bounds bounds;
    }
    [Serializable] private sealed class Support
    {
        public string path;
        public Bounds bounds;
        public int rays;
        public float minimumGroundY, maximumGroundY;
    }
    [Serializable] private sealed class SectionState
    {
        public string side;
        public float start, length, entry, exit, total;
        public bool reverse, loop;
    }
    [Serializable] private sealed class Snapshot
    {
        public string status = "PASS", groundAsset;
        public Vector3 buildingPosition, buildingLocalPosition, buildingLocalScale, buildingWorldOffset;
        public Quaternion buildingRotation, buildingLocalRotation;
        public Bounds originalGroundBounds, generatedGroundBounds;
        public float northSupportMargin;
        public int supportRayCount;
        public List<Support> supports = new List<Support>();
        public List<RendererState> renderers = new List<RendererState>();
        public List<SectionState> resolvedSections = new List<SectionState>();
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("COURSE_LAYOUT_SNAPSHOT_FAIL: " + message);
    }

    private static string PathOf(Transform value) => value.parent == null ? value.name : PathOf(value.parent) + "/" + value.name;

    private static Bounds WorldBounds(Mesh mesh, Transform transform)
    {
        Vector3[] vertices = mesh.vertices;
        Require(vertices.Length > 0, "Empty ground mesh.");
        var bounds = new Bounds(transform.TransformPoint(vertices[0]), Vector3.zero);
        foreach (Vector3 vertex in vertices) bounds.Encapsulate(transform.TransformPoint(vertex));
        return bounds;
    }

    [MenuItem("Racing/Validation/Write Course Layout Snapshot")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode first.");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Transform wing = null;
        MeshFilter ground = null;
        RaceCourse course = null;
        var snapshot = new Snapshot();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                Transform source = PrefabUtility.GetCorrespondingObjectFromSource(transform);
                if (source != null && source.parent == source.root && AssetDatabase.GetAssetPath(source) == TubeCoursePreparation.ModelPath &&
                    (source.name == "Cube.008" || source.name == "Cylinder.002" || source.name == "人間" || source.name.StartsWith("人間.", StringComparison.Ordinal)))
                {
                    Vector3 expected = source.name == "Cube.008" ? new Vector3(0, 0, 23) :
                        source.name == "Cylinder.002" ? new Vector3(3, 0, 0) : new Vector3(0, 0, 20);
                    Vector3 original = transform.parent.TransformPoint(source.localPosition);
                    Require((transform.position - original - expected).magnitude < .0002f, "Unexpected absolute offset: " + source.name);
                    Require(Quaternion.Angle(transform.localRotation, source.localRotation) < .001f &&
                        (transform.localScale - source.localScale).magnitude < .00001f, "Rotation/scale changed: " + source.name);
                }
                if (source != null && source.name == "Cube.008" && AssetDatabase.GetAssetPath(source) == TubeCoursePreparation.ModelPath)
                {
                    Require(wing == null, "Multiple north wings.");
                    wing = transform;
                }
            }
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshFilter source = PrefabUtility.GetCorrespondingObjectFromSource(filter);
                if (source != null && source.name == "Plane" && AssetDatabase.GetAssetPath(source) == TubeCoursePreparation.ModelPath)
                {
                    Require(ground == null, "Multiple source main grounds.");
                    ground = filter;
                }
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                snapshot.renderers.Add(new RendererState { path = PathOf(renderer.transform), enabled = renderer.enabled,
                    active = renderer.gameObject.activeInHierarchy, bounds = renderer.bounds });
            foreach (RaceCourse candidate in root.GetComponentsInChildren<RaceCourse>(true))
            {
                Require(course == null, "Multiple courses.");
                course = candidate;
            }
        }
        Require(wing != null && ground != null && course != null, "Missing wing, ground or course.");
        Transform originalWing = PrefabUtility.GetCorrespondingObjectFromSource(wing);
        Vector3 originalPosition = wing.parent != null ? wing.parent.TransformPoint(originalWing.localPosition) : originalWing.localPosition;
        snapshot.buildingWorldOffset = wing.position - originalPosition;
        Require((snapshot.buildingWorldOffset - new Vector3(0, 0, 23)).magnitude < .0002f, "Wing is not exactly 23m world north of source.");
        Require(Mathf.Abs(wing.position.y - originalPosition.y) < .0002f, "Wing Y changed.");
        Require(Quaternion.Angle(wing.localRotation, originalWing.localRotation) < .001f, "Wing rotation changed.");
        Require((wing.localScale - originalWing.localScale).magnitude < .00001f, "Wing scale changed.");
        snapshot.buildingPosition = wing.position; snapshot.buildingLocalPosition = wing.localPosition;
        snapshot.buildingRotation = wing.rotation; snapshot.buildingLocalRotation = wing.localRotation; snapshot.buildingLocalScale = wing.localScale;
        MeshCollider collider = ground.GetComponent<MeshCollider>();
        Require(collider != null && collider.enabled && collider.sharedMesh != null, "Generated ground collider missing or disabled.");
        Require(ground.sharedMesh == collider.sharedMesh, "Ground visual/collider meshes differ.");
        Mesh originalGround = PrefabUtility.GetCorrespondingObjectFromSource(ground).sharedMesh;
        snapshot.originalGroundBounds = WorldBounds(originalGround, ground.transform);
        snapshot.generatedGroundBounds = WorldBounds(ground.sharedMesh, ground.transform);
        snapshot.groundAsset = AssetDatabase.GetAssetPath(ground.sharedMesh);
        Bounds before = snapshot.originalGroundBounds, after = snapshot.generatedGroundBounds;
        Require(Mathf.Abs(after.max.z - 868f) < .002f, "North bound is not 868m.");
        Require(Mathf.Abs(after.min.z - before.min.z) < .002f && Mathf.Abs(after.min.x - before.min.x) < .002f &&
            Mathf.Abs(after.max.x - before.max.x) < .002f, "An unrelated ground boundary moved.");
        Renderer[] moved = wing.GetComponentsInChildren<Renderer>(true);
        Require(moved.Length == 6, "Expected six wing renderers.");
        float wingNorth = float.NegativeInfinity;
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        Physics.SyncTransforms();
        try
        {
            foreach (Renderer renderer in moved)
            {
                Bounds bounds = renderer.bounds;
                wingNorth = Mathf.Max(wingNorth, bounds.max.z);
                var support = new Support { path = PathOf(renderer.transform), bounds = bounds,
                    minimumGroundY = float.PositiveInfinity, maximumGroundY = float.NegativeInfinity };
                // Include corners and a 5x5 footprint grid. Cast only against the exact
                // generated ground collider so roofs or other colliders cannot mask holes.
                for (int ix = 0; ix <= 4; ix++)
                    for (int iz = 0; iz <= 4; iz++)
                    {
                        float x = Mathf.Lerp(bounds.min.x, bounds.max.x, ix / 4f);
                        float z = Mathf.Lerp(bounds.min.z, bounds.max.z, iz / 4f);
                        Require(collider.Raycast(new Ray(new Vector3(x, 100f, z), Vector3.down), out RaycastHit hit, 150f), "Unsupported footprint at " + support.path);
                        Require(Mathf.Abs(hit.point.y) < .002f, "Unexpected ground height at " + support.path);
                        support.minimumGroundY = Mathf.Min(support.minimumGroundY, hit.point.y);
                        support.maximumGroundY = Mathf.Max(support.maximumGroundY, hit.point.y);
                        support.rays++; snapshot.supportRayCount++;
                    }
                snapshot.supports.Add(support);
            }
        }
        finally { Physics.queriesHitBackfaces = backfaces; }
        snapshot.northSupportMargin = after.max.z - wingNorth;
        Require(snapshot.northSupportMargin >= 1f, "North support margin below 1m.");
        RaceBarrierProfiles profiles = course.GetComponent<RaceBarrierProfiles>();
        Require(profiles != null, "Missing barrier profiles.");
        course.RebuildCache();
        var centers = new List<Vector3>(); course.CopyCenterPathWorld(centers);
        foreach (RaceBarrierProfiles.ResolvedSection section in profiles.Resolve(centers, course.ClosedLoop))
            snapshot.resolvedSections.Add(new SectionState { side = section.side.ToString(), start = section.start, length = section.length,
                entry = section.entry, exit = section.exit, total = section.total, reverse = section.reverse, loop = section.loop });
        Require(snapshot.resolvedSections.Count > 0, "No resolved barrier sections.");
        snapshot.renderers.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
        snapshot.supports.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
        string output = "Artifacts/CourseLayoutHandoff/layout-snapshot.json";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(snapshot, true));
        Debug.Log($"COURSE_LAYOUT_SNAPSHOT_PASS: {snapshot.supportRayCount} exact-ground rays, {snapshot.northSupportMargin:F6}m north margin; {output}");
    }
}
