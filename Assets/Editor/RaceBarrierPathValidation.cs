using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// RaceBarrierPathの最小生成検証です。メニューおよびUnity -batchmodeから実行できます。
/// </summary>
public static class RaceBarrierPathValidation
{
    [MenuItem("Racing/Validate Barrier Path")]
    public static void Run()
    {
        GameObject root = new GameObject("Barrier path validation");
        try
        {
            RaceBarrierPath path = root.AddComponent<RaceBarrierPath>();
            path.AddControlPoint();
            path.AddControlPoint();
            path.Rebuild();

            List<Vector3> preview = new List<Vector3>();
            path.CopyPreviewPathWorld(preview);
            Require(preview.Count > 1, "Preview path was not generated.");
            Require(root.GetComponentInChildren<MeshFilter>(true) != null, "Visual mesh was not generated.");
            Require(root.GetComponentsInChildren<BoxCollider>(true).Length > 0, "Collider chain was not generated.");

            Debug.Log("RACE_BARRIER_PATH_VALIDATION_PASS");
        }
        catch (Exception exception)
        {
            Debug.LogError($"RACE_BARRIER_PATH_VALIDATION_FAIL: {exception.Message}");
            throw;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
