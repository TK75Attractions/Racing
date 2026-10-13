using System;
using UnityEngine;

/// <summary>Copies only visible meshes. Preview vehicles never instantiate driving scripts or colliders.</summary>
public static class CarSelectionModel
{
    public static GameObject Create(GameObject prefab, Transform parent, bool includeWheels = true)
    {
        GameObject root = new GameObject(prefab.name + " Display Model");
        root.transform.SetParent(parent, false);
        CarSelectionMaterialSet materials = root.AddComponent<CarSelectionMaterialSet>();
        Matrix4x4 toLocal = prefab.transform.worldToLocalMatrix;
        foreach (MeshRenderer source in prefab.GetComponentsInChildren<MeshRenderer>(true))
        {
            MeshFilter filter = source.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !IsActive(source.transform, prefab.transform)) continue;
            string name = source.name;
            // RMCar26 contains four LODs; the showroom and body replacement need only the best mesh.
            if (name.Contains("_LOD") && !name.EndsWith("_LOD0", StringComparison.Ordinal)) continue;
            if (!includeWheels && IsWheel(name)) continue;
            GameObject mesh = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            mesh.transform.SetParent(root.transform, false);
            Matrix4x4 matrix = toLocal * source.transform.localToWorldMatrix;
            mesh.transform.localPosition = matrix.GetColumn(3);
            mesh.transform.localRotation = matrix.rotation;
            mesh.transform.localScale = matrix.lossyScale;
            mesh.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            Material[] compatible = source.sharedMaterials;
            for (int i = 0; i < compatible.Length; i++) compatible[i] = materials.Convert(compatible[i]);
            mesh.GetComponent<MeshRenderer>().sharedMaterials = compatible;
        }
        if (includeWheels && prefab.name == "SportCar_1")
        {
            GameObject wheelPrefab = CarSelectionCatalog.Load().wheelModel;
            foreach (string name in new[] { "FL", "FR", "RL", "RR" })
            {
                Transform anchor = prefab.transform.Find(name);
                if (anchor == null || wheelPrefab == null) continue;
                GameObject wheel = Create(wheelPrefab, root.transform);
                wheel.transform.localPosition = anchor.localPosition;
                wheel.transform.localRotation = anchor.localRotation;
            }
        }
        return root;
    }

    public static Bounds Bounds(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        Bounds bounds = new Bounds(model.transform.position, Vector3.zero);
        bool first = true;
        foreach (Renderer renderer in renderers)
        {
            if (!renderer.enabled) continue;
            if (first) { bounds = renderer.bounds; first = false; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    public static void RestoreRaceBody(GameObject car)
    {
        Transform replacement = car.transform.Find("SelectedVehicleBody");
        if (replacement != null)
        {
            replacement.gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(replacement.gameObject);
            else UnityEngine.Object.DestroyImmediate(replacement.gameObject);
        }
        foreach (Transform child in car.GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.StartsWith("SportCar_1", StringComparison.Ordinal)) continue;
            foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>()) renderer.enabled = true;
            break;
        }
    }

    public static void ReplaceRaceBody(GameObject car, GameObject prefab)
    {
        Transform body = null;
        foreach (Transform child in car.GetComponentsInChildren<Transform>(true))
            if (child.name.StartsWith("SportCar_1", StringComparison.Ordinal)) { body = child; break; }
        if (body == null || prefab == null) return;
        Bounds original = Bounds(body.gameObject);
        foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>())
            if (!IsWheel(renderer.name)) renderer.enabled = false;
        GameObject replacement = Create(prefab, car.transform, false);
        replacement.name = "SelectedVehicleBody";
        Bounds bounds = Bounds(replacement);
        float scale = original.size.z / Mathf.Max(.01f, bounds.size.z);
        replacement.transform.localScale = Vector3.one * scale;
        bounds = Bounds(replacement);
        replacement.transform.position += original.center - bounds.center;
        foreach (Transform child in replacement.GetComponentsInChildren<Transform>()) child.gameObject.layer = body.gameObject.layer;
    }

    private static bool IsActive(Transform source, Transform root)
    {
        for (Transform node = source; node != null; node = node.parent)
        {
            if (!node.gameObject.activeSelf) return false;
            if (node == root) return true;
        }
        return true;
    }

    private static bool IsWheel(string name) => name == "FL" || name == "FR" || name == "RL" || name == "RR" || name.IndexOf("Wheel", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("Tire", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Rotor", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("Brake", StringComparison.OrdinalIgnoreCase) >= 0;
}
