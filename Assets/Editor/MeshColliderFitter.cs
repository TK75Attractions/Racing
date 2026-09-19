using UnityEditor;
using UnityEngine;

/// <summary>
/// 選択したオブジェクトのメッシュをそのまま当たり判定にします。
/// 箱では形に合わない校舎などに使います。FBXのインポート設定は変更しないため、
/// 同じモデルの他の配置や非アクティブな複製には影響しません。
/// </summary>
public static class MeshColliderFitter
{
    // これを超える三角形数では、生成に時間がかかることを先に知らせます。
    private const long HeavyTriangleWarning = 300000;

    [MenuItem("Racing/Fit Mesh Colliders/Add To Selection")]
    private static void AddToSelection() => AddToSelection(convex: false);

    [MenuItem("Racing/Fit Mesh Colliders/Add To Selection (Convex)")]
    private static void AddConvexToSelection() => AddToSelection(convex: true);

    [MenuItem("Racing/Fit Mesh Colliders/Clear From Selection")]
    private static void ClearFromSelection()
    {
        GameObject target = Selection.activeGameObject;
        if (target == null)
        {
            EditorUtility.DisplayDialog("Mesh Colliders", "対象のオブジェクトをヒエラルキーで選択してください。", "OK");
            return;
        }

        int removed = Clear(target);
        Debug.Log($"Removed {removed} mesh collider(s) from {target.name}.", target);
    }

    [MenuItem("Racing/Fit Mesh Colliders/Add To Selection", true)]
    [MenuItem("Racing/Fit Mesh Colliders/Add To Selection (Convex)", true)]
    [MenuItem("Racing/Fit Mesh Colliders/Clear From Selection", true)]
    private static bool HasSelection() => Selection.activeGameObject != null;

    private static void AddToSelection(bool convex)
    {
        GameObject target = Selection.activeGameObject;
        if (target == null)
        {
            EditorUtility.DisplayDialog("Mesh Colliders", "対象のオブジェクトをヒエラルキーで選択してください。", "OK");
            return;
        }

        long triangles = CountTriangles(target);
        if (triangles == 0)
        {
            EditorUtility.DisplayDialog("Mesh Colliders",
                $"{target.name} には当たり判定に使えるメッシュがありません。", "OK");
            return;
        }

        if (triangles > HeavyTriangleWarning && !EditorUtility.DisplayDialog("Mesh Colliders",
            $"{target.name} は約 {triangles:N0} 三角形あります。\n" +
            "生成に時間がかかり、メモリも多く使います。続けますか？",
            "続ける", "やめる"))
        {
            return;
        }

        int added = Add(target, convex);
        Debug.Log($"Added {added} mesh collider(s) to {target.name} " +
            $"({triangles:N0} triangles, convex: {convex}).", target);
    }

    /// <summary>付けた当たり判定の個数を返します。エディタ検証からも直接呼び出せます。</summary>
    public static int Add(GameObject target, bool convex)
    {
        if (target == null) return 0;

        int added = 0;
        // 非表示にしてあるパーツは、見た目どおりという意味で対象から外します。
        foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>(false))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;

            MeshCollider collider = filter.GetComponent<MeshCollider>();
            if (collider == null)
            {
                collider = Undo.AddComponent<MeshCollider>(filter.gameObject);
                added++;
            }
            else
            {
                Undo.RecordObject(collider, "Fit Mesh Colliders");
            }

            collider.sharedMesh = mesh;
            collider.convex = convex;
            EditorUtility.SetDirty(collider);
        }

        if (added > 0) EditorUtility.SetDirty(target);
        return added;
    }

    /// <summary>取り除いた当たり判定の個数を返します。</summary>
    public static int Clear(GameObject target)
    {
        if (target == null) return 0;

        MeshCollider[] colliders = target.GetComponentsInChildren<MeshCollider>(true);
        foreach (MeshCollider collider in colliders)
        {
            Undo.DestroyObjectImmediate(collider);
        }

        if (colliders.Length > 0) EditorUtility.SetDirty(target);
        return colliders.Length;
    }

    /// <summary>当たり判定に使われる三角形の総数です。重さの目安に使います。</summary>
    public static long CountTriangles(GameObject target)
    {
        if (target == null) return 0;

        long triangles = 0;
        foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>(false))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                triangles += mesh.GetIndexCount(submesh) / 3;
            }
        }

        return triangles;
    }
}
