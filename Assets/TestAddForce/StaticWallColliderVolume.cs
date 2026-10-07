using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>一枚の矩形の壁だけを両面で衝突する薄い立体へ置き換えます。FBX は変更しません。</summary>
public static class StaticWallColliderVolume
{
    private const float WallThickness = 0.15f;
    private static readonly HashSet<int> PreparedScenes = new HashSet<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetScenes() => PreparedScenes.Clear();

    public static void EnsureForScene(Scene scene)
    {
        if (!scene.IsValid() || !PreparedScenes.Add(scene.handle)) return;
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (MeshCollider surface in root.GetComponentsInChildren<MeshCollider>(true))
        {
            if (!surface.enabled || surface.isTrigger || !surface.gameObject.activeInHierarchy ||
                surface.attachedRigidbody != null || surface.GetComponentInParent<RaceBarrierPath>() != null ||
                !TryGetWallBox(surface, out Bounds bounds)) continue;

            var volume = surface.gameObject.AddComponent<BoxCollider>();
            volume.center = bounds.center;
            volume.size = bounds.size;
            volume.sharedMaterial = surface.sharedMaterial;
            volume.contactOffset = surface.contactOffset;
            volume.includeLayers = surface.includeLayers;
            volume.excludeLayers = surface.excludeLayers;
            volume.layerOverridePriority = surface.layerOverridePriority;
            surface.enabled = false;
        }
    }

    private static bool TryGetWallBox(MeshCollider surface, out Bounds bounds)
    {
        bounds = default;
        Mesh mesh = surface.sharedMesh;
        if (mesh == null || mesh.vertexCount < 4 || mesh.vertexCount > 12) return false;
        bounds = mesh.bounds;
        Vector3 size = bounds.size;
        Vector3 scale = surface.transform.lossyScale;
        int axis = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        float worldScale = Mathf.Abs(scale[axis]);
        if (worldScale < 0.0001f || size[axis] * worldScale > 0.02f) return false;
        Vector3 localNormal = Vector3.zero;
        localNormal[axis] = 1f;
        // 接地できる坂・床・天井を箱で塞ぎません。
        if (Mathf.Abs(surface.transform.TransformDirection(localNormal).normalized.y) >= 0.2f) return false;

        int first = (axis + 1) % 3;
        int second = (axis + 2) % 3;
        if (size[first] < 0.01f || size[second] < 0.01f) return false;
        ulong indexCount = 0;
        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            indexCount += mesh.GetIndexCount(submesh);
        if (indexCount != 6 && indexCount != 12) return false;

        // FBX が Read/Write 無効でも動作するよう、CPU の頂点配列ではなく元の Collider を調べます。
        // 四隅・辺・中央の全てで元の面に当たる矩形だけを立体化します。
        for (int row = -1; row <= 1; row++)
        for (int column = -1; column <= 1; column++)
        {
            Vector3 point = bounds.center;
            point[first] += bounds.extents[first] * row * 0.999f;
            point[second] += bounds.extents[second] * column * 0.999f;
            Vector3 offset = Vector3.zero;
            offset[axis] = bounds.extents[axis] + 0.5f / worldScale;
            Vector3 start = surface.transform.TransformPoint(point + offset);
            Vector3 end = surface.transform.TransformPoint(point - offset);
            Vector3 span = end - start;
            if (!surface.Raycast(new Ray(start, span.normalized), out _, span.magnitude) &&
                !surface.Raycast(new Ray(end, -span.normalized), out _, span.magnitude)) return false;
        }

        size[axis] = WallThickness / worldScale;
        bounds.size = size;
        return true;
    }
}
