using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Partition whole triangles without moving vertices or modifying imported FBX/collision.</summary>
public static class CourseRenderChunkBuilder
{
    private const string Folder = "Assets/Art/RenderBudget/Chunks";
    private const string RootName = "Render Chunks (generated)";
    private const float CellSize = 100f;

    private sealed class Prepared
    {
        public MeshRenderer source;
        public Mesh original;
        public readonly List<Mesh> meshes = new List<Mesh>();
        public string id;
        public bool enabled;
    }

    [MenuItem("Racing/Rendering/Build School And Tube Render Chunks")]
    public static void Run()
    {
        Scene scene = RenderPreparationScene.Open();
        var records = Records(scene);
        var staged = new List<Prepared>();
        try
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshFilter imported = PrefabUtility.GetCorrespondingObjectFromSource(filter);
                if (AssetDatabase.GetAssetPath(imported) != TubeCoursePreparation.ModelPath) continue;
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                Mesh mesh = filter.sharedMesh;
                CourseRenderChunks previous = records.Find(r => r.source == renderer);
                bool enabled = previous != null ? previous.sourceWasEnabled : renderer != null && renderer.enabled;
                if (!enabled || !filter.gameObject.activeInHierarchy || mesh == null) continue;
                // Trees retain their authored four mesh LODs and selection bias. Transparent surfaces,
                // baked lightmaps, custom vertex streams and LODGroups need separate handling.
                if (filter.name.StartsWith("tree", StringComparison.OrdinalIgnoreCase) ||
                    mesh.name.StartsWith("tree", StringComparison.OrdinalIgnoreCase) ||
                    filter.GetComponentInParent<LODGroup>() != null || renderer.additionalVertexStreams != null ||
                    (renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 65534) ||
                    mesh.blendShapeCount != 0 || mesh.lodCount > 1 ||
                    renderer.sharedMaterials.Length != mesh.subMeshCount) continue;
                bool opaque = true;
                foreach (Material material in renderer.sharedMaterials)
                    if (material == null || material.renderQueue > (int)RenderQueue.GeometryLast) opaque = false;
                if (!opaque || Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.z) < CellSize * 1.5f) continue;
                var item = new Prepared { source = renderer, original = mesh, enabled = enabled,
                    id = Hash128.Compute(GlobalObjectId.GetGlobalObjectIdSlow(renderer).ToString()).ToString() };
                staged.Add(item); // Dispose all temporary meshes even if partitioning fails.
                Partition(item);
            }
            RenderPreparationScene.Folder(Folder);
            // All source geometry is examined before touching renderer enable flags or scene children.
            RestoreRecords(records);
            int sources = 0, chunks = 0;
            foreach (Prepared item in staged)
            {
                if (item.meshes.Count == 0) continue;
                var owner = new GameObject(RootName);
                owner.transform.SetParent(item.source.transform, false);
                var record = owner.AddComponent<CourseRenderChunks>();
                record.source = item.source;
                record.sourceMesh = item.original;
                record.sourceWasEnabled = item.enabled;
                for (int i = 0; i < item.meshes.Count; i++)
                {
                    Mesh mesh = item.meshes[i];
                    string path = Folder + "/" + item.id + "_" + i.ToString("D3") + ".asset";
                    Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (saved == null)
                    {
                        AssetDatabase.CreateAsset(mesh, path);
                        saved = mesh;
                    }
                    else { EditorUtility.CopySerialized(mesh, saved); EditorUtility.SetDirty(saved); }
                    var part = new GameObject("Chunk " + i.ToString("D3"));
                    part.layer = item.source.gameObject.layer;
                    part.transform.SetParent(owner.transform, false);
                    // Keep occluder/static batching flags off: these are independent culling units.
                    part.AddComponent<MeshFilter>().sharedMesh = saved;
                    MeshRenderer replacement = part.AddComponent<MeshRenderer>();
                    EditorUtility.CopySerialized(item.source, replacement);
                    replacement.enabled = true;
                    replacement.ResetBounds();
                    replacement.ResetLocalBounds();
                    // Keep reflection/light probe sampling anchored to the original renderer centre.
                    // Otherwise chunk centres would introduce visible lighting seams.
                    if (item.source.probeAnchor == null)
                    {
                        Transform anchor = owner.transform.Find("Original Probe Anchor");
                        if (anchor == null)
                        {
                            anchor = new GameObject("Original Probe Anchor").transform;
                            anchor.SetParent(owner.transform, false);
                            anchor.position = item.source.bounds.center;
                        }
                        replacement.probeAnchor = anchor;
                    }
                }
                item.source.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(item.source);
                sources++;
                chunks += item.meshes.Count;
            }
            RenderPreparationScene.Save(scene);
            Debug.Log($"COURSE_RENDER_CHUNKS: {sources} original renderers -> {chunks} chunks; " +
                "100m XZ cells, original filters/colliders/FBX/trees retained. Measure both camera stacks.");
        }
        catch
        {
            // Discard partial scene mutations; generated asset files can be reused on the next run.
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(RenderPreparationScene.Path);
            throw;
        }
        finally
        {
            foreach (Prepared item in staged)
            foreach (Mesh mesh in item.meshes)
                if (mesh != null && !AssetDatabase.Contains(mesh)) UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static void Partition(Prepared item)
    {
        Mesh mesh = item.original;
        using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
        using (var positions = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
        {
            var source = data[0];
            source.GetVertices(positions);
            var cells = new SortedDictionary<Vector2Int, List<int>[]>(new CellComparer());
            long inputIndices = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var sub = source.GetSubMesh(s);
                if (sub.topology != MeshTopology.Triangles)
                    throw new InvalidOperationException("Non-triangle school mesh: " + mesh.name);
                using (var indices = new NativeArray<int>(sub.indexCount, Allocator.Temp))
                {
                    source.GetIndices(indices, s, true);
                    inputIndices += indices.Length;
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        Vector3 center = item.source.transform.TransformPoint(
                            (positions[indices[i]] + positions[indices[i + 1]] + positions[indices[i + 2]]) / 3f);
                        var key = new Vector2Int(Mathf.FloorToInt(center.x / CellSize), Mathf.FloorToInt(center.z / CellSize));
                        if (!cells.TryGetValue(key, out var groups))
                        {
                            groups = new List<int>[mesh.subMeshCount];
                            for (int j = 0; j < groups.Length; j++) groups[j] = new List<int>();
                            cells.Add(key, groups);
                        }
                        groups[s].Add(indices[i]); groups[s].Add(indices[i + 1]); groups[s].Add(indices[i + 2]);
                    }
                }
            }
            // Bound the added draw calls. A dominant cell suggests little benefit; leave it alone.
            if (cells.Count < 2 || cells.Count > 48) { Debug.Log("CHUNKS_SKIPPED: " + mesh.name + " cells=" + cells.Count); return; }
            long largest = 0;
            foreach (var cell in cells.Values)
            {
                long count = 0;
                foreach (var group in cell) count += group.Count;
                largest = Math.Max(largest, count);
            }
            if (largest > inputIndices * .85) return;
            long outputIndices = 0, outputVertices = 0;
            foreach (var cell in cells.Values)
            {
                var remap = new Dictionary<int, int>();
                var originals = new List<int>();
                var triangles = new List<int>[mesh.subMeshCount];
                for (int s = 0; s < cell.Length; s++)
                {
                    triangles[s] = new List<int>(cell[s].Count);
                    foreach (int old in cell[s])
                    {
                        if (!remap.TryGetValue(old, out int next))
                        {
                            next = originals.Count; remap.Add(old, next); originals.Add(old);
                        }
                        triangles[s].Add(next);
                    }
                    outputIndices += triangles[s].Count;
                }
                var chunk = new Mesh { name = mesh.name + " Render Chunk", indexFormat = IndexFormat.UInt32 };
                item.meshes.Add(chunk);
                chunk.SetVertexBufferParams(originals.Count, mesh.GetVertexAttributes());
                for (int stream = 0; stream < source.vertexBufferCount; stream++)
                {
                    int stride = source.GetVertexBufferStride(stream);
                    var bytes = source.GetVertexData<byte>(stream);
                    using (var output = new NativeArray<byte>(originals.Count * stride, Allocator.Temp))
                    {
                        for (int v = 0; v < originals.Count; v++)
                            NativeArray<byte>.Copy(bytes, originals[v] * stride, output, v * stride, stride);
                        chunk.SetVertexBufferData(output, 0, 0, output.Length, stream);
                    }
                }
                chunk.subMeshCount = mesh.subMeshCount;
                for (int s = 0; s < mesh.subMeshCount; s++) chunk.SetTriangles(triangles[s], s, false);
                chunk.RecalculateBounds();
                outputVertices += originals.Count;
            }
            if (inputIndices != outputIndices) throw new InvalidOperationException("Triangle conservation failed.");
            if (outputVertices > mesh.vertexCount * 1.5)
            {
                foreach (Mesh chunk in item.meshes) UnityEngine.Object.DestroyImmediate(chunk);
                item.meshes.Clear();
                Debug.Log("CHUNKS_SKIPPED: excessive vertex duplication: " + mesh.name);
                return;
            }
            Debug.Log($"CHUNKS_PREPARED: {item.source.name}: {inputIndices / 3} triangles retained; " +
                $"{mesh.vertexCount}->{outputVertices} vertices; {item.meshes.Count} culling bounds.");
        }
    }

    private sealed class CellComparer : IComparer<Vector2Int>
    {
        public int Compare(Vector2Int a, Vector2Int b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);
    }

    private static List<CourseRenderChunks> Records(Scene scene)
    {
        var result = new List<CourseRenderChunks>();
        foreach (GameObject root in scene.GetRootGameObjects()) result.AddRange(root.GetComponentsInChildren<CourseRenderChunks>(true));
        return result;
    }

    private static void RestoreRecords(List<CourseRenderChunks> records)
    {
        foreach (CourseRenderChunks record in records)
            if (record.source == null) throw new InvalidOperationException("Chunk rollback source is missing.");
        foreach (CourseRenderChunks record in records)
        {
            record.source.enabled = record.sourceWasEnabled;
            PrefabUtility.RecordPrefabInstancePropertyModifications(record.source);
            UnityEngine.Object.DestroyImmediate(record.gameObject);
        }
    }

    [MenuItem("Racing/Rendering/Restore Original School And Tube Renderers")]
    public static void Restore()
    {
        Scene scene = RenderPreparationScene.Open();
        RestoreRecords(Records(scene));
        RenderPreparationScene.Save(scene);
        Debug.Log("COURSE_RENDER_CHUNKS_RESTORED: original renderer flags restored; generated assets retained for reuse.");
    }
}
