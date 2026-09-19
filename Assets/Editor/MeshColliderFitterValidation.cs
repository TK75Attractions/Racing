using System;
using UnityEditor;
using UnityEngine;

// Unity -batchmode -executeMethod MeshColliderFitterValidation.Run -quit でも実行できます。
public static class MeshColliderFitterValidation
{
    [MenuItem("Racing/Validate Mesh Colliders")]
    public static void Run()
    {
        GameObject root = new GameObject("Mesh collider validation");
        try
        {
            // 校舎と同じく、回転と拡大を持たせた親の下で検証します。
            root.transform.position = new Vector3(679f, 0f, 848f);
            root.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
            root.transform.localScale = new Vector3(3f, 3f, 3f);

            GameObject sphere = CreatePart(root.transform, PrimitiveType.Sphere, "Dome", Vector3.zero);
            GameObject hidden = CreatePart(root.transform, PrimitiveType.Cube, "Hidden", new Vector3(4f, 0f, 0f));
            hidden.SetActive(false);

            Require(MeshColliderFitter.CountTriangles(root) > 0, "Meshes must be counted before fitting.");
            Require(MeshColliderFitter.Add(root, convex: false) == 1,
                "Only the visible part must receive a mesh collider.");
            MeshCollider collider = sphere.GetComponent<MeshCollider>();
            Require(collider != null, "The visible part must carry a mesh collider.");
            Require(collider.sharedMesh == sphere.GetComponent<MeshFilter>().sharedMesh,
                "The collider must use the part's own mesh.");
            Require(!collider.convex, "The default fit must stay non-convex for static scenery.");
            Require(hidden.GetComponent<MeshCollider>() == null, "Hidden parts must stay without collision.");

            Require(MeshColliderFitter.Add(root, convex: false) == 0, "Re-fitting must not add a second collider.");
            Require(sphere.GetComponents<MeshCollider>().Length == 1, "A part must never hold duplicate colliders.");

            // 形に沿っているか。球の外だが箱の内側になる位置を、上から撃ち抜けることを確かめます。
            Physics.SyncTransforms();
            Bounds bounds = sphere.GetComponent<Renderer>().bounds;
            Vector3 center = bounds.center;
            Vector3 corner = center + new Vector3(bounds.extents.x * 0.9f, 0f, bounds.extents.z * 0.9f);
            Require(Physics.Raycast(center + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 100f),
                "A ray through the middle must hit the mesh collider.");
            Require(hit.collider == collider, "The hit must come from the generated mesh collider.");
            Require(!Physics.Raycast(corner + Vector3.up * 20f, Vector3.down, 100f),
                "A box-shaped fit must not remain: the corner of the bounds must stay open.");

            Require(MeshColliderFitter.Add(root, convex: true) == 0, "Switching to convex must reuse the collider.");
            Require(collider.convex, "Convex mode must be applied to the existing collider.");

            Require(MeshColliderFitter.Clear(root) == 1, "Clearing must report the removed colliders.");
            Require(sphere.GetComponent<MeshCollider>() == null, "Clearing must remove every mesh collider.");
            Require(MeshColliderFitter.Clear(root) == 0, "Clearing twice must be harmless.");

            GameObject empty = new GameObject("Empty target");
            empty.transform.SetParent(root.transform);
            Require(MeshColliderFitter.CountTriangles(empty) == 0, "An object without meshes must count zero.");
            Require(MeshColliderFitter.Add(empty, convex: false) == 0, "An object without meshes must add nothing.");

            Debug.Log("Mesh collider validation passed.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static GameObject CreatePart(Transform parent, PrimitiveType type, string name, Vector3 localPosition)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        // 既定で付く Collider は、生成した当たり判定だけを検証するために外します。
        UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        return part;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
