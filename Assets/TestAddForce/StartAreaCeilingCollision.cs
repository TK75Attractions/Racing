using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Keep the start-side building roof solid, including when a chassis reaches its underside.</summary>
public static class StartAreaCeilingCollision
{
    private const string ProxyName = "Start building roof collision";
    public static BoxCollider EnsureForScene(Scene scene)
    {
        if (!scene.IsValid()) return null;
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == ProxyName) return root.GetComponent<BoxCollider>();
        Transform roof = null;
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "1016こうしゃ") { roof = root.transform.Find("Cube.026"); break; }
        if (roof == null || !roof.gameObject.activeInHierarchy) return null;
        var source = roof.GetComponent<MeshCollider>();
        if (source == null || !source.enabled || source.isTrigger || source.sharedMesh == null) return null;
        // This authored roof is a closed, axis-aligned rectangular slab. Do not fill complex geometry.
        Physics.SyncTransforms();
        var bounds = source.bounds;
        if (source.sharedMesh.vertexCount != 36 || bounds.size.y > 2f || bounds.size.x < 20f || bounds.size.z < 10f ||
            !AxisAligned(roof.right) || !AxisAligned(roof.up) || !AxisAligned(roof.forward)) return null;
        var proxy = new GameObject(ProxyName);
        SceneManager.MoveGameObjectToScene(proxy, scene);
        proxy.layer = source.gameObject.layer;
        proxy.transform.position = bounds.center;
        // A world-space proxy avoids inheriting the FBX's huge/negative scale.
        var box = proxy.AddComponent<BoxCollider>();
        box.size = bounds.size;
        box.sharedMaterial = source.sharedMaterial;
        box.contactOffset = source.contactOffset;
        box.includeLayers = source.includeLayers; box.excludeLayers = source.excludeLayers;
        box.layerOverridePriority = source.layerOverridePriority;
        source.enabled = false;
        return box;
    }
    private static bool AxisAligned(Vector3 direction) => Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z)) > .999f;
}
