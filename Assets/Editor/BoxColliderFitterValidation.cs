using System;
using UnityEditor;
using UnityEngine;

// Unity -batchmode -executeMethod BoxColliderFitterValidation.Run -quit でも実行できます。
public static class BoxColliderFitterValidation
{
    [MenuItem("Racing/Validate Box Colliders")]
    public static void Run()
    {
        GameObject root = new GameObject("Box collider validation");
        try
        {
            // 校舎と同じく、回転と拡大を持たせた親の下で検証します。
            root.transform.position = new Vector3(679f, 0.01f, 848f);
            root.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
            root.transform.localScale = new Vector3(3f, 3f, 3f);

            GameObject wing = CreatePart(root.transform, "Wing", new Vector3(-2f, 0f, 0f));
            GameObject hall = CreatePart(root.transform, "Hall", new Vector3(3f, 0f, 0f));

            Require(BoxColliderFitter.Fit(root, perPart: false) == 1, "Whole object mode must create a single box.");
            Transform generated = root.transform.Find(BoxColliderFitter.GeneratedRootName);
            Require(generated != null, "Fitting must create the generated root under the target.");
            Require(generated.childCount == 1, "Whole object mode must leave exactly one box.");

            BoxCollider whole = generated.GetChild(0).GetComponent<BoxCollider>();
            Require(whole != null, "Every generated object must carry a box collider.");
            Bounds expected = wing.GetComponent<Renderer>().bounds;
            expected.Encapsulate(hall.GetComponent<Renderer>().bounds);
            NearVector(whole.bounds.center, expected.center, "The box must cover the whole building.");
            NearVector(whole.bounds.size, expected.size, "The box must match the visible size.");
            Require(generated.GetChild(0).localRotation == Quaternion.identity,
                "Boxes must follow the target's own axes, not the world axes.");

            // 親の拡大を打ち消しているか。見た目1辺1の部品が、スケール3の親の下で3になる。
            Require(BoxColliderFitter.Fit(root, perPart: true) == 2, "Per part mode must create one box per child.");
            Require(generated.childCount == 2, "Re-fitting must replace the previous boxes, not add to them.");
            BoxCollider wingBox = FindBox(generated, "Box_Wing");
            NearVector(wingBox.bounds.size, wing.GetComponent<Renderer>().bounds.size,
                "A part box must match that part's visible size.");
            NearVector(wingBox.bounds.center, wing.GetComponent<Renderer>().bounds.center,
                "A part box must sit on that part.");
            Near(wingBox.size.x, 1f, "The parent scale must be compensated in local size.");

            Require(BoxColliderFitter.Fit(root, perPart: true) == 2, "Repeated fitting must stay stable.");
            Require(generated.childCount == 2, "Repeated fitting must not accumulate boxes.");
            NearVector(FindBox(generated, "Box_Wing").bounds.size, wing.GetComponent<Renderer>().bounds.size,
                "Generated boxes must not be measured as part of the building.");

            // 実際に当たるか。生成した箱だけが残るよう、部品の既定Colliderは外してあります。
            Physics.SyncTransforms();
            Vector3 above = hall.GetComponent<Renderer>().bounds.center + Vector3.up * 20f;
            Require(Physics.Raycast(above, Vector3.down, out RaycastHit hit, 100f),
                "The generated box must be hit by physics queries.");
            Require(hit.collider.transform.IsChildOf(generated), "The hit must come from the generated box.");

            Require(BoxColliderFitter.Clear(root), "Clearing must report the removal.");
            Require(root.transform.Find(BoxColliderFitter.GeneratedRootName) == null,
                "Clearing must remove every generated box.");
            Require(!BoxColliderFitter.Clear(root), "Clearing twice must be harmless.");

            GameObject empty = new GameObject("Empty target");
            empty.transform.SetParent(root.transform);
            Require(BoxColliderFitter.Fit(empty, perPart: false) == 0, "An object without renderers must create nothing.");
            Require(empty.transform.Find(BoxColliderFitter.GeneratedRootName) == null,
                "A failed fit must not leave an empty generated root.");

            Debug.Log("Box collider validation passed.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static GameObject CreatePart(Transform parent, string name, Vector3 localPosition)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        // 既定で付く Collider は、生成した箱だけを検証するために外します。
        UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        return part;
    }

    private static BoxCollider FindBox(Transform generated, string name)
    {
        Transform box = generated.Find(name);
        Require(box != null, $"{name} must exist after fitting per part.");
        return box.GetComponent<BoxCollider>();
    }

    private static void NearVector(Vector3 actual, Vector3 expected, string message) =>
        Require((actual - expected).sqrMagnitude < 0.0001f, $"{message} Expected {expected}, got {actual}.");

    private static void Near(float actual, float expected, string message) =>
        Require(Mathf.Abs(actual - expected) < 0.001f, $"{message} Expected {expected}, got {actual}.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
