using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CarWallEscapeValidation
{
    private const string SessionKey = "Racing.CarWallEscapeValidation";
    private static double started;

    [MenuItem("Racing/Validate Wall Escape (Play Mode)")]
    public static void RunBatch()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before starting validation.");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        started = EditorApplication.timeSinceStartup;
        SessionState.SetBool(SessionKey, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    public static void RunFullBatch()
    {
        RunGeometryChecks();
        RunBatch();
    }

    public static void RunGeometryChecks()
    {
        if (Application.isBatchMode && string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path))
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var meshes = new System.Collections.Generic.List<Mesh>();
        try
        {
            MeshCollider Make(string name, Vector3 position, Quaternion rotation, bool trapezoid = false)
            {
                var go = new GameObject(name);
                go.transform.SetPositionAndRotation(position, rotation);
                var mesh = new Mesh();
                meshes.Add(mesh);
                float top = trapezoid ? 0.25f : 1f;
                mesh.vertices = new[] { new Vector3(-1f, 0f, 0f), new Vector3(-top, 3f, 0f),
                    new Vector3(top, 3f, 0f), new Vector3(1f, 0f, 0f) };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateBounds();
                var collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                return collider;
            }
            var rectangle = Make("Non-readable rectangular wall", Vector3.zero, Quaternion.identity);
            rectangle.transform.localScale = new Vector3(2f, 1f, 2.5f);
            var floor = Make("Floor", Vector3.right * 10f, Quaternion.Euler(90f, 0f, 0f));
            var slope = Make("Slope", Vector3.right * 20f, Quaternion.Euler(45f, 0f, 0f));
            var trapezoid = Make("Trapezoid", Vector3.right * 30f, Quaternion.identity, true);
            var moving = Make("Kinematic wall", Vector3.right * 40f, Quaternion.identity);
            moving.gameObject.AddComponent<Rigidbody>().isKinematic = true;
            Physics.SyncTransforms();
            rectangle.sharedMesh.UploadMeshData(true);
            Require(!rectangle.sharedMesh.isReadable, "The test mesh must be non-readable.");
            StaticWallColliderVolume.EnsureForScene(scene);
            var volume = rectangle.GetComponent<BoxCollider>();
            Require(volume != null && !rectangle.enabled && !volume.isTrigger,
                "A non-readable rectangular wall must get a solid box collider.");
            Require(Mathf.Abs(volume.size.z * rectangle.transform.lossyScale.z - 0.15f) < 0.001f,
                "Wall thickness must remain 0.15 m under non-uniform scale.");
            Require(floor.enabled && slope.enabled && trapezoid.enabled && moving.enabled &&
                floor.GetComponent<BoxCollider>() == null && slope.GetComponent<BoxCollider>() == null &&
                trapezoid.GetComponent<BoxCollider>() == null && moving.GetComponent<BoxCollider>() == null,
                "Floors, slopes, non-rectangular shapes and moving walls must retain their original colliders.");
            StaticWallColliderVolume.EnsureForScene(scene);
            Require(rectangle.GetComponents<BoxCollider>().Length == 1, "Two cars must not duplicate wall colliders.");
            Debug.Log("WALL_GEOMETRY_CHECKS_PASS");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid()) UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (!SessionState.GetBool(SessionKey, false)) return;
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(SessionKey, false)) { EditorApplication.update -= Tick; return; }
        if (EditorApplication.timeSinceStartup - started > 240d)
        {
            Finish(false, "Wall escape validation timed out.");
            return;
        }
        if (!EditorApplication.isPlaying || UnityEngine.Object.FindFirstObjectByType<Gmanager>() == null ||
            UnityEngine.Object.FindFirstObjectByType<CarWallEscapeValidationProbe>() != null) return;
        new GameObject("Wall escape validation").AddComponent<CarWallEscapeValidationProbe>();
    }

    public static void Finish(bool passed, string message)
    {
        SessionState.SetBool(SessionKey, false);
        EditorApplication.update -= Tick;
        if (passed) Debug.Log(message); else Debug.LogError(message);
        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
