using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Bakes only tube visuals, without reimporting the FBX or retaining its trees on the CPU.</summary>
public static class TubeWallVisualPreparation
{
    [MenuItem("Racing/Prepare Tube Wall Visuals")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity")
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Open SampleScene first.");
            scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        }
        TubeSpeedPatternController controller = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (var candidate in root.GetComponentsInChildren<TubeSpeedPatternController>(true))
            {
                if (controller != null) throw new InvalidOperationException("Expected one tube wall controller.");
                controller = candidate;
            }
        if (controller == null || controller.Tube == null || controller.Tube.sharedMesh == null)
            throw new InvalidOperationException("Configured tube wall controller is missing. Run TubeSpeedPatternSetup first.");

        Directory.CreateDirectory(TubeCoursePreparation.DataFolder);
        AssetDatabase.Refresh();
        Mesh copy = CopyReadableMesh(controller.Tube.sharedMesh);
        try
        {
            if (copy.vertexCount == 0 || copy.uv.Length != copy.vertexCount)
                throw new InvalidOperationException("Tube mesh needs authored ring UVs.");
            Mesh wall = controller.CreateBakedWallMetadata(copy, out Vector3[] centers, out float length, out bool reverse);
            if (centers.Length < 2 || length <= 0f)
            {
                UnityEngine.Object.DestroyImmediate(wall);
                throw new InvalidOperationException("Tube mesh needs at least two distinct UV rings.");
            }
            Mesh sourceAsset = SaveMesh(copy, "TubeWallSource.asset");
            copy = null; // SaveMesh now owns the copy (or has destroyed it after updating the asset).
            Mesh wallAsset = SaveMesh(wall, "TubeWallOverlay.asset");
            controller.SetBakedWallMetadata(sourceAsset, wallAsset, centers, length, reverse);
            EditorUtility.SetDirty(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            controller.Rebuild();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the tube wall scene references.");
            Debug.Log($"TUBE_WALL_VISUAL_PREPARATION_PASS: {centers.Length} rings, {length:F1}m; " +
                $"local UV grid: {wallAsset.vertexCount} vertices, {wallAsset.triangles.Length / 3} triangles; FBX importer unchanged.");
        }
        finally
        {
            if (copy != null && !AssetDatabase.Contains(copy)) UnityEngine.Object.DestroyImmediate(copy);
        }
    }

    private static Mesh CopyReadableMesh(Mesh source)
    {
        // Editor-only API deliberately bypasses isReadable. Copy all streams verbatim,
        // preserving authored normals, UVs, indices and submesh topology.
        using (var input = MeshUtility.AcquireReadOnlyMeshData(source))
        {
            var output = Mesh.AllocateWritableMeshData(1);
            try
            {
                var data = input[0];
                var target = output[0];
                target.SetVertexBufferParams(data.vertexCount, source.GetVertexAttributes());
                for (int stream = 0; stream < data.vertexBufferCount; stream++)
                    target.GetVertexData<byte>(stream).CopyFrom(data.GetVertexData<byte>(stream));
                target.SetIndexBufferParams(data.GetIndexData<byte>().Length /
                    (data.indexFormat == IndexFormat.UInt16 ? 2 : 4), data.indexFormat);
                target.GetIndexData<byte>().CopyFrom(data.GetIndexData<byte>());
                target.subMeshCount = data.subMeshCount;
                for (int i = 0; i < data.subMeshCount; i++)
                    target.SetSubMesh(i, data.GetSubMesh(i), MeshUpdateFlags.DontRecalculateBounds);
            }
            catch
            {
                output.Dispose();
                throw;
            }
            var mesh = new Mesh { name = "Tube wall readable source" };
            Mesh.ApplyAndDisposeWritableMeshData(output, mesh);
            mesh.bounds = source.bounds;
            return mesh;
        }
    }

    private static Mesh SaveMesh(Mesh mesh, string filename)
    {
        mesh.hideFlags = HideFlags.None;
        string path = TubeCoursePreparation.DataFolder + "/" + filename;
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }
}
