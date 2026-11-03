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
            MeshFilter[] rebuiltVisuals = root.GetComponentsInChildren<MeshFilter>(true);
            Require(rebuiltVisuals.Length == 2 &&
                    Array.FindAll(rebuiltVisuals, f => f.sharedMesh != null && f.sharedMesh.name == "RaceBarrierVisualMesh").Length == 1 &&
                    Array.FindAll(rebuiltVisuals, f => f.sharedMesh != null && f.sharedMesh.name == "RaceBarrierSolidFrame").Length == 1,
                "Multiple visual walls remain after rebuilding.");
            Require(root.GetComponentsInChildren<MeshCollider>(true).Length == 1,
                "Multiple collision walls remain after rebuilding.");

            path.enabled = false;
            Require(root.GetComponentsInChildren<MeshCollider>(true).Length == 0,
                "Disabling the path left an invisible collider behind.");

            ValidateProfiles();
            ValidateAnchoredIntervals();
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

    private static void ValidateProfiles()
    {
        var root = new GameObject("Profile geometry validation");
        try
        {
            RaceBarrierPath path = root.AddComponent<RaceBarrierPath>();
            foreach (int side in new[] { -1, 1 })
            foreach (float weight in new[] { 0f, .25f, .5f, .75f, 1f })
            {
                // Both directions of transition share vertices with the constant-profile portions.
                var points = new List<Vector3>(); var weights = new List<float>();
                for (int row = 0; row <= 120; row++)
                {
                    points.Add(Vector3.forward * (row * .5f));
                    float t = Mathf.Clamp01((row * .5f - 10f) / 20f);
                    if (row > 60) t = Mathf.Clamp01((60f - row * .5f) / 20f);
                    weights.Add(weight * t * t * (3f - 2f * t));
                }
                path.SetWorldPath(points, side, false, null, null, true, weights);
                Mesh visual = root.GetComponentInChildren<MeshFilter>().sharedMesh;
                Mesh collider = root.GetComponentInChildren<MeshCollider>().sharedMesh;
                Vector3[] front = visual.vertices, shell = collider.vertices;
                for (int row = 0; row < points.Count; row++)
                for (int section = 0; section <= 32; section++)
                {
                    int index = row * 33 + section;
                    Require(Vector3.Distance(front[index], shell[row * 66 + section]) < .0001f,
                        "Profile visual/collider surfaces must coincide.");
                    Require(front[index].y >= -.0001f, "A profile folded below its ground contact.");
                    if (section > 0)
                    {
                        Require(front[index].y > front[index - 1].y,
                            "Corresponding profile vertices must remain ordered upwards.");
                        Require(shell[row * 66 + 33 + section].y > shell[row * 66 + 32 + section].y,
                            "The collider back surface must not fold over at a blended section.");
                    }
                    if (weights[row] < .99999f) continue;
                    Require(side * front[index].x <= .1001f && front[index].y <= 6.101f,
                        "Upright profile must be narrow and at least six metres tall.");
                    Require(side * shell[row * 66 + 33 + section].x <= .201f,
                        "Upright collider must stay within the 0.2 m outer envelope.");
                }
                ValidateMeshFaces(visual, side, false);
                ValidateMeshFaces(collider, side, true);
                Require(visual.bounds.max.y >= 6.1f, "Blended walls cannot lower the containment height.");
                Physics.SyncTransforms();
                MeshCollider collision = root.GetComponentInChildren<MeshCollider>();
                Require(collision.Raycast(new Ray(new Vector3(-side * 3f, 4f, 30f), Vector3.right * side), out var hit, 20f),
                    "A vehicle-height ray must hit every profile from the road side.");
                if (weight == 1f) Require(Mathf.Abs(hit.point.x - side * .1f) < .002f,
                    "Upright collision must be on the vertical face.");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void ValidateMeshFaces(Mesh mesh, int side, bool closedShell)
    {
        Vector3[] vertices = mesh.vertices;
        int[] indices = mesh.triangles;
        var edges = new Dictionary<ulong, int>();
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 normal = Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]], vertices[indices[i + 2]] - vertices[indices[i]]);
            Require(normal.sqrMagnitude > 1e-14f, "Profile transitions contain a degenerate triangle.");
            // Front faces are the first 32 quads of each row, and must face the road.
            if (!closedShell) Require(normal.x * side < 0f, "A profile transition triangle faces away from the road.");
            for (int j = 0; j < 3; j++)
            {
                uint a = (uint)indices[i + j], b = (uint)indices[i + (j + 1) % 3];
                ulong key = ((ulong)Math.Min(a, b) << 32) | Math.Max(a, b);
                edges.TryGetValue(key, out int count); edges[key] = count + 1;
            }
        }
        if (closedShell) foreach (int count in edges.Values) Require(count == 2, "A collider shell has an open or non-manifold edge.");
    }

    private static void ValidateAnchoredIntervals()
    {
        var root = new GameObject("Profile anchor validation");
        try
        {
            RaceBarrierProfiles profiles = root.AddComponent<RaceBarrierProfiles>();
            profiles.reverseRaceDirection = true;
            profiles.sections.Add(new RaceBarrierProfiles.Section { landmark = root.transform,
                localStart = new Vector3(10, 0, 80), localEnd = new Vector3(10, 0, 30), entryMeters = 10, exitMeters = 20 });
            foreach (float shift in new[] { 0f, 25f })
            {
                var centers = new[] { new Vector3(0, 0, -shift), new Vector3(0, 0, 100) };
                var resolved = profiles.Resolve(centers, false);
                Require(resolved.Count == 1, "Landmark interval must survive changes to course length.");
                var section = resolved[0];
                Require(section.Weight(60 + shift, 1) == 1f && section.Weight(60 + shift, -1) == 0f,
                    "Race-left must select the east edge when course samples run backwards.");
                Require(Mathf.Abs(section.Weight(85 + shift, 1) - .5f) < .001f &&
                    Mathf.Abs(section.Weight(20 + shift, 1) - .5f) < .001f,
                    "Anchored entry/exit must follow geometry rather than a fixed progress.");
            }
            profiles.sections[0].followEntryCorner = true;
            foreach (float north in new[] { 100f, 160f })
            {
                var corner = new[] { Vector3.zero, new Vector3(0, 0, north), new Vector3(-100, 0, north) };
                var resolved = profiles.Resolve(corner, false);
                Require(resolved.Count == 1 && resolved[0].Weight(north - 10f, 1) == 1f &&
                    resolved[0].Weight(north + 10f, 1) == 0f && resolved[0].Weight(north, 1) > 0f &&
                    resolved[0].Weight(north, 1) < 1f,
                    "The transition must move with the entry corner when the start straight moves north.");
            }
            // An interval crossing the raw seam has the same weight at 0 and total.
            var seam = new RaceBarrierProfiles.ResolvedSection { side = RaceBarrierProfiles.Side.Left,
                start = -10, length = 20, entry = 5, exit = 5, total = 100, loop = true };
            Require(seam.Weight(0, -1) == 1 && seam.Weight(100, -1) == 1,
                "Closed-course seam must not reset the profile.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
