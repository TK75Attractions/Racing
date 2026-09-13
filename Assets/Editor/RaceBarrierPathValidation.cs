using System;
using System.Collections.Generic;
using System.Reflection;
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
            MeshFilter visual = root.GetComponentInChildren<MeshFilter>(true);
            MeshCollider collision = root.GetComponentInChildren<MeshCollider>(true);
            Require(visual != null && visual.sharedMesh != null, "Visual mesh was not generated.");
            Require(collision != null && collision.sharedMesh != null, "Curved collider mesh was not generated.");
            MeshRenderer wallRenderer = visual.GetComponent<MeshRenderer>();
            Require(wallRenderer != null && wallRenderer.sharedMaterial != null &&
                    wallRenderer.sharedMaterial.shader.name == "Racing/Race Barrier Hex Frost",
                "The default hex-grid frost material was not applied.");
            Texture hexMask = wallRenderer.sharedMaterial.GetTexture("_GridMask");
            Require(hexMask != null && hexMask.width == 672 && hexMask.height == 672,
                "The repeatable hex-grid texture is missing from the material.");
            Require(wallRenderer.sharedMaterial.HasProperty("_GridColor") &&
                    wallRenderer.sharedMaterial.HasProperty("_CellTint"),
                "The grid and frost colors are not editable on the material.");
            Require(wallRenderer.sharedMaterial.GetFloat("_GridTiling") > 0f,
                "The grid texture repeat value is invalid.");
            Require(!collision.convex, "Static wall collider must remain non-convex.");
            Require(root.GetComponentsInChildren<BoxCollider>(true).Length == 0,
                "Old vertical BoxCollider segments are still present.");
            Require(collision.sharedMesh.bounds.max.y >= 6f, "Wall height is below the new minimum.");
            int lowerSectionCount = 16;
            int sectionCount = lowerSectionCount * 2;
            Vector3[] visualVertices = visual.sharedMesh.vertices;
            Vector3[] colliderVertices = collision.sharedMesh.vertices;
            Require(visualVertices.Length >= sectionCount + 1,
                "The wall section is not smoothly subdivided.");
            Require(Vector3.Distance(visualVertices[0], Vector3.zero) < 0.001f,
                "The wall does not begin at the ground contact point.");
            Require(visualVertices[1].y / visualVertices[1].x < 0.05f,
                "The wall entrance is too steep for a vehicle to climb.");
            Require(Mathf.Abs(visualVertices[lowerSectionCount].x - 6f * Mathf.Sqrt(3f)) < 0.02f &&
                    Mathf.Abs(visualVertices[lowerSectionCount].y - 6f) < 0.02f,
                "The wall is not a 60-degree circular arc at the requested height.");
            Require(visualVertices[sectionCount].y > visualVertices[lowerSectionCount].y + 6f &&
                    visualVertices[sectionCount].x < visualVertices[lowerSectionCount + 5].x - 2f,
                "The upper wall does not curl back toward the road.");
            Require(visualVertices[sectionCount].x < visualVertices[sectionCount - 1].x,
                "The wall top is not directed back toward the road.");
            Require(colliderVertices[sectionCount + 1].y < -0.5f,
                "Collider thickness creates a step above the road at the wall base.");
            for (int i = 0; i <= sectionCount; i++)
            {
                Require(Vector3.Distance(visualVertices[i], colliderVertices[i]) < 0.001f,
                    "Visual and collider arc surfaces do not match.");
            }
            for (int i = 0; i < preview.Count; i++)
            {
                Require(Vector3.Distance(visualVertices[i * (sectionCount + 1)], preview[i]) < 0.001f &&
                        Vector3.Distance(colliderVertices[i * (sectionCount + 1) * 2], preview[i]) < 0.001f,
                    "A ground contact point drifted away from the manually placed path.");
            }

            Physics.SyncTransforms();
            Ray roadSideRay = new Ray(new Vector3(-3f, 4f, 5f), Vector3.right);
            Require(collision.Raycast(roadSideRay, out RaycastHit hit, 20f),
                "A vehicle-height ray did not hit the road-facing curved wall.");
            Require(hit.point.x > 8f && hit.point.x < 10f,
                "Collision happened away from the curved wall surface.");
            Ray underLipRay = new Ray(new Vector3(9f, 8f, 5f), Vector3.up);
            Require(collision.Raycast(underLipRay, out RaycastHit lipHit, 8f) && lipHit.point.y > 10f,
                "The return arc does not provide a collision surface above the vehicle.");

            // 既存シーンに保存された低い制御点も、最低高さで再生成されることを確認する。
            SerializedObject serializedPath = new SerializedObject(path);
            SerializedProperty points = serializedPath.FindProperty("controlPoints");
            for (int i = 0; i < points.arraySize; i++)
            {
                points.GetArrayElementAtIndex(i).FindPropertyRelative("wallHeight").floatValue = 2.5f;
                points.GetArrayElementAtIndex(i).FindPropertyRelative("outwardFlare").floatValue = 1.5f;
            }
            serializedPath.FindProperty("wallSectionSegments").intValue = 4;
            serializedPath.ApplyModifiedPropertiesWithoutUndo();
            path.Rebuild();
            collision = root.GetComponentInChildren<MeshCollider>(true);
            Require(collision.sharedMesh.bounds.max.y >= 6f,
                "Existing low control points were not raised to the minimum height.");
            Require(collision.sharedMesh.bounds.max.x >= 6f * Mathf.Sqrt(3f) - 0.02f,
                "Existing wall data was not converted to the climbable circular arc.");
            Require(root.GetComponentInChildren<MeshFilter>(true).sharedMesh.vertexCount >=
                    preview.Count * (sectionCount + 1),
                "Existing coarse wall sections were not upgraded to a smooth arc.");

            // ドメイン再読み込みで非シリアライズ参照を失っても、古い生成物を残さない。
            Transform previousRoot = root.transform.Find("Generated");
            Require(previousRoot != null, "Generated root was not found before reload simulation.");
            GameObject duplicate = UnityEngine.Object.Instantiate(previousRoot.gameObject, root.transform);
            duplicate.name = "Generated";
            foreach (string fieldName in new[] { "generatedRoot", "generatedMesh", "generatedColliderMesh" })
            {
                FieldInfo field = typeof(RaceBarrierPath).GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Require(field != null, $"Missing generated field: {fieldName}");
                field.SetValue(path, null);
            }
            path.Rebuild();
            Require(previousRoot == null && duplicate == null,
                "Stale generated roots survived a rebuild after reference loss.");
            Require(root.GetComponentsInChildren<MeshFilter>(true).Length == 1,
                "Multiple visual walls remain after rebuilding.");
            Require(root.GetComponentsInChildren<MeshCollider>(true).Length == 1,
                "Multiple collision walls remain after rebuilding.");

            path.enabled = false;
            Require(root.GetComponentsInChildren<MeshCollider>(true).Length == 0,
                "Disabling the path left an invisible collider behind.");

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
