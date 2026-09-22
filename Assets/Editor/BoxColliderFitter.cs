using UnityEditor;
using UnityEngine;

/// <summary>
/// 選択したオブジェクトの見た目の範囲に合わせて、箱の当たり判定を生成します。
/// 校舎のように三角形数の多いモデルへ MeshCollider を付けずに済ませるための開発用ツールです。
/// 箱は対象の向きに沿って作るため、回転して置いた建物にもそのまま使えます。
/// </summary>
public static class BoxColliderFitter
{
    public const string GeneratedRootName = "GeneratedColliders";

    [MenuItem("Racing/Fit Box Colliders/Whole Object")]
    private static void FitWholeObject() => FitSelection(perPart: false);

    [MenuItem("Racing/Fit Box Colliders/Per Child Part")]
    private static void FitPerChildPart() => FitSelection(perPart: true);

    [MenuItem("Racing/Fit Box Colliders/Clear Generated")]
    private static void ClearSelection()
    {
        GameObject target = Selection.activeGameObject;
        if (target == null)
        {
            EditorUtility.DisplayDialog("Box Colliders", "対象のオブジェクトをヒエラルキーで選択してください。", "OK");
            return;
        }

        if (!Clear(target))
        {
            EditorUtility.DisplayDialog("Box Colliders", $"{target.name} に生成済みの当たり判定はありません。", "OK");
            return;
        }

        Debug.Log($"Removed generated box colliders from {target.name}.", target);
    }

    [MenuItem("Racing/Fit Box Colliders/Whole Object", true)]
    [MenuItem("Racing/Fit Box Colliders/Per Child Part", true)]
    [MenuItem("Racing/Fit Box Colliders/Clear Generated", true)]
    private static bool HasSelection() => Selection.activeGameObject != null;

    private static void FitSelection(bool perPart)
    {
        GameObject target = Selection.activeGameObject;
        if (target == null)
        {
            EditorUtility.DisplayDialog("Box Colliders", "対象のオブジェクトをヒエラルキーで選択してください。", "OK");
            return;
        }

        int created = Fit(target, perPart);
        if (created == 0)
        {
            EditorUtility.DisplayDialog("Box Colliders",
                $"{target.name} には範囲を測れる Renderer がありません。", "OK");
            return;
        }

        Selection.activeGameObject = target;
        Debug.Log($"Fitted {created} box collider(s) to {target.name}. " +
            $"位置と大きさは {GeneratedRootName} の下で自由に調整できます。", target);
    }

    /// <summary>生成した当たり判定の個数を返します。エディタ検証からも直接呼び出せます。</summary>
    public static int Fit(GameObject target, bool perPart)
    {
        if (target == null) return 0;

        Transform generated = EnsureGeneratedRoot(target);
        // 作り直しても増殖しないよう、前回の生成物は先に消します。
        for (int index = generated.childCount - 1; index >= 0; index--)
        {
            Undo.DestroyObjectImmediate(generated.GetChild(index).gameObject);
        }

        int created = 0;
        if (perPart)
        {
            foreach (Transform child in target.transform)
            {
                if (child == generated) continue;
                if (!TryGetLocalBounds(child.gameObject, target.transform, generated, out Bounds childBounds)) continue;
                CreateBox(generated, childBounds, child.name);
                created++;
            }
        }

        if (created == 0 &&
            TryGetLocalBounds(target, target.transform, generated, out Bounds bounds))
        {
            CreateBox(generated, bounds, target.name);
            created = 1;
        }

        if (created == 0)
        {
            Undo.DestroyObjectImmediate(generated.gameObject);
            return 0;
        }

        EditorUtility.SetDirty(target);
        return created;
    }

    /// <summary>生成済みの当たり判定をまとめて削除します。削除するものがあれば true を返します。</summary>
    public static bool Clear(GameObject target)
    {
        if (target == null) return false;
        Transform generated = target.transform.Find(GeneratedRootName);
        if (generated == null) return false;

        Undo.DestroyObjectImmediate(generated.gameObject);
        EditorUtility.SetDirty(target);
        return true;
    }

    private static Transform EnsureGeneratedRoot(GameObject target)
    {
        Transform generated = target.transform.Find(GeneratedRootName);
        if (generated != null) return generated;

        GameObject rootObject = new GameObject(GeneratedRootName);
        Undo.RegisterCreatedObjectUndo(rootObject, "Fit Box Colliders");
        Undo.SetTransformParent(rootObject.transform, target.transform, "Fit Box Colliders");
        rootObject.transform.localPosition = Vector3.zero;
        rootObject.transform.localRotation = Quaternion.identity;
        rootObject.transform.localScale = Vector3.one;
        rootObject.layer = target.layer;
        return rootObject.transform;
    }

    // 対象の座標系で見た目の範囲を測ります。生成済みの当たり判定は対象から除きます。
    private static bool TryGetLocalBounds(GameObject source, Transform space, Transform generated, out Bounds bounds)
    {
        bounds = new Bounds();
        bool hasBounds = false;
        Matrix4x4 worldToSpace = space.worldToLocalMatrix;

        foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
        {
            if (generated != null && renderer.transform.IsChildOf(generated)) continue;

            Mesh mesh = GetMesh(renderer);
            // メッシュがあればその向きのまま、無ければワールドのAABBから測ります。
            Bounds sourceBounds = mesh != null ? mesh.bounds : renderer.bounds;
            Matrix4x4 matrix = mesh != null
                ? worldToSpace * renderer.transform.localToWorldMatrix
                : worldToSpace;
            Encapsulate(ref bounds, ref hasBounds, sourceBounds, matrix);
        }

        return hasBounds;
    }

    private static void Encapsulate(ref Bounds bounds, ref bool hasBounds, Bounds source, Matrix4x4 matrix)
    {
        Vector3 center = source.center;
        Vector3 extents = source.extents;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 offset = new Vector3(
                (corner & 1) == 0 ? -extents.x : extents.x,
                (corner & 2) == 0 ? -extents.y : extents.y,
                (corner & 4) == 0 ? -extents.z : extents.z);
            Vector3 point = matrix.MultiplyPoint3x4(center + offset);
            if (!hasBounds)
            {
                bounds = new Bounds(point, Vector3.zero);
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(point);
        }
    }

    private static Mesh GetMesh(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }

    private static void CreateBox(Transform generated, Bounds localBounds, string sourceName)
    {
        GameObject boxObject = new GameObject($"Box_{sourceName}");
        Undo.RegisterCreatedObjectUndo(boxObject, "Fit Box Colliders");
        Undo.SetTransformParent(boxObject.transform, generated, "Fit Box Colliders");
        // 生成ルートは対象と同じ座標系なので、測った値をそのまま使えます。
        boxObject.transform.localPosition = localBounds.center;
        boxObject.transform.localRotation = Quaternion.identity;
        boxObject.transform.localScale = Vector3.one;
        boxObject.layer = generated.gameObject.layer;

        BoxCollider box = Undo.AddComponent<BoxCollider>(boxObject);
        box.center = Vector3.zero;
        box.size = localBounds.size;
    }
}
