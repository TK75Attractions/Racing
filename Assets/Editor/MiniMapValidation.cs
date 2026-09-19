using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ミニマップの投影とUI構築を検証します。メニューおよびUnity -batchmodeから実行できます。
/// </summary>
public static class MiniMapValidation
{
    private static readonly Vector2 SampleMapSize = new Vector2(300f, 210f);
    private const float SamplePadding = 14f;

    [MenuItem("Racing/Validate MiniMap")]
    public static void Run()
    {
        RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        if (course == null)
        {
            Debug.LogError("MINI_MAP_VALIDATION_FAIL: RaceCourse was not found in the open scene.");
            return;
        }

        Validate(course);
    }

    public static void RunBatch()
    {
        if (!Application.isBatchMode)
        {
            throw new InvalidOperationException("RunBatch must be started with Unity -batchmode.");
        }

        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Run();
    }

    private static void Validate(RaceCourse course)
    {
        GameObject canvasRoot = null;
        try
        {
            List<Vector3> inner = new List<Vector3>();
            List<Vector3> outer = new List<Vector3>();
            course.CopyCourseBandWorld(inner, outer);
            Require(inner.Count >= 2 && inner.Count == outer.Count,
                $"Course band was not generated (inner={inner.Count}, outer={outer.Count}).");

            MiniMapProjector projector = new MiniMapProjector();
            Require(projector.Build(inner, outer, SampleMapSize, SamplePadding, 0f),
                "The projector could not fit the course into the mini map rect.");
            Require(projector.PixelsPerMeter > 0f, "Projection scale is not positive.");

            // 投影した全点がパディングの内側に収まることを確認します。
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            AccumulateProjected(projector, inner, ref min, ref max);
            AccumulateProjected(projector, outer, ref min, ref max);

            float halfWidthLimit = (SampleMapSize.x * 0.5f) - SamplePadding + 0.01f;
            float halfHeightLimit = (SampleMapSize.y * 0.5f) - SamplePadding + 0.01f;
            Require(min.x >= -halfWidthLimit && max.x <= halfWidthLimit,
                $"Projected course overflows horizontally (x: {min.x:F2}..{max.x:F2}).");
            Require(min.y >= -halfHeightLimit && max.y <= halfHeightLimit,
                $"Projected course overflows vertically (y: {min.y:F2}..{max.y:F2}).");

            // アスペクト比を保っていること（縦横で別倍率になっていないこと）を確認します。
            Vector2 worldSize = GetWorldSizeXZ(inner, outer);
            Vector2 projectedSize = max - min;
            Require(Mathf.Abs((projectedSize.x / projectedSize.y) - (worldSize.x / worldSize.y)) < 0.001f,
                "The projection distorts the course aspect ratio.");

            // ワールド+Zがマップ上方向、ワールドYaw90度（+X向き）がマップ右向きになることを確認します。
            Vector2 north = projector.ToLocal(new Vector3(0f, 0f, 1f)) - projector.ToLocal(Vector3.zero);
            Require(north.y > 0f && Mathf.Abs(north.x) < 0.001f, "World +Z is not mapped to mini map up.");
            Require(Mathf.Abs(projector.ToLocalAngle(0f)) < 0.001f, "Yaw 0 does not point the marker up.");
            Require(Mathf.Abs(projector.ToLocalAngle(90f) + 90f) < 0.001f,
                "Yaw 90 does not point the marker to the right.");

            // OnPlay配下のノード生成を、実行時と同じ手順で確認します。
            // シーンのOnPlayは書き換えず、同じ大きさの使い捨てノードで再現します。
            RectTransform sceneOnPlay = FindSceneOnPlay();
            canvasRoot = new GameObject("MiniMapValidationCanvas", typeof(RectTransform), typeof(Canvas));
            GameObject onPlay = new GameObject("OnPlay", typeof(RectTransform));
            onPlay.transform.SetParent(canvasRoot.transform, false);
            RectTransform onPlayRect = (RectTransform)onPlay.transform;
            onPlayRect.sizeDelta = sceneOnPlay != null ? sceneOnPlay.rect.size : new Vector2(1920f, 1080f);

            UIMiniMap miniMap = new UIMiniMap();
            miniMap.Init(onPlay.transform, course, 0);
            Require(miniMap.HasCourse, "UIMiniMap failed to build the course band.");

            Transform mapRoot = onPlay.transform.Find("MiniMap");
            Require(mapRoot != null, "The MiniMap node was not created under OnPlay.");
            RectTransform mapRect = mapRoot as RectTransform;
            Require(mapRect.anchorMin == new Vector2(0f, 1f) &&
                    mapRect.anchorMax == new Vector2(0f, 1f) &&
                    mapRect.pivot == new Vector2(0f, 1f),
                "The mini map is not anchored to the top-left corner.");
            Require(mapRect.anchoredPosition.x > 0f && mapRect.anchoredPosition.y < 0f,
                "The mini map is placed outside the visible area.");
            Require(mapRoot.Find("Border") != null && mapRoot.Find("Track") != null,
                "The course band graphics were not created.");
            Require(mapRoot.Find("Marker_P1") != null && mapRoot.Find("Marker_P2") != null,
                "Car markers were not created.");
            Require(mapRoot.GetComponentsInChildren<Graphic>(true).Length == 5,
                "Unexpected number of mini map graphics.");
            foreach (Graphic graphic in mapRoot.GetComponentsInChildren<Graphic>(true))
            {
                Require(!graphic.raycastTarget, $"{graphic.name} still blocks raycasts.");
            }

            // 自車マーカーが常に相手より前面になることを確認します。
            Require(mapRoot.Find("Marker_P1").GetSiblingIndex() > mapRoot.Find("Marker_P2").GetSiblingIndex(),
                "The own-car marker is not drawn on top.");

            // 車を割り当てていない間はマーカーを隠します。
            miniMap.UpdateMarkers();
            Require(!mapRoot.Find("Marker_P1").gameObject.activeSelf,
                "Markers are visible without a car assigned.");

            GameObject dummyCar = new GameObject("MiniMapValidationCar");
            dummyCar.transform.SetPositionAndRotation(inner[0], Quaternion.Euler(0f, 90f, 0f));
            miniMap.SetCars(dummyCar.transform, null);
            RectTransform markerRect = mapRoot.Find("Marker_P1") as RectTransform;
            Require(markerRect.gameObject.activeSelf, "The own-car marker stayed hidden after assignment.");
            Require(Vector2.Distance(markerRect.anchoredPosition, projector.ToLocal(inner[0])) < 0.01f,
                "The marker position does not match the projection.");
            Require(Mathf.Abs(Mathf.DeltaAngle(markerRect.localEulerAngles.z, -90f)) < 0.01f,
                "The marker heading does not match the car yaw.");
            UnityEngine.Object.DestroyImmediate(dummyCar);

            // 既存HUDと重ならないことを、シーンの実配置から確認します。
            if (sceneOnPlay != null)
            {
                Bounds mapBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(onPlayRect, mapRect);
                foreach (string hudName in new[] { "Position", "Lap", "Time", "Speed" })
                {
                    Transform hud = sceneOnPlay.Find(hudName);
                    if (hud == null) continue;
                    if (!TryGetGraphicBounds(sceneOnPlay, hud, out Bounds hudBounds)) continue;

                    Require(!Overlaps(mapBounds, hudBounds),
                        $"The mini map overlaps the {hudName} HUD element " +
                        $"(map x:{mapBounds.min.x:F0}..{mapBounds.max.x:F0} y:{mapBounds.min.y:F0}..{mapBounds.max.y:F0}, " +
                        $"{hudName} x:{hudBounds.min.x:F0}..{hudBounds.max.x:F0} y:{hudBounds.min.y:F0}..{hudBounds.max.y:F0}).");
                }
            }

            Debug.Log($"MINI_MAP_VALIDATION_PASS: points={inner.Count}, " +
                      $"scale={projector.PixelsPerMeter:F3}px/m, " +
                      $"projected={projectedSize.x:F1}x{projectedSize.y:F1}px in " +
                      $"{SampleMapSize.x}x{SampleMapSize.y}px");
        }
        catch (Exception exception)
        {
            Debug.LogError($"MINI_MAP_VALIDATION_FAIL: {exception.Message}");
            throw;
        }
        finally
        {
            if (canvasRoot != null) UnityEngine.Object.DestroyImmediate(canvasRoot);
        }
    }

    private static RectTransform FindSceneOnPlay()
    {
        Gmanager manager = UnityEngine.Object.FindFirstObjectByType<Gmanager>();
        Transform managerRoot = manager != null ? manager.transform.parent : null;
        Transform canvas = managerRoot != null ? managerRoot.Find("MainCanvas") : null;
        return canvas != null ? canvas.Find("OnPlay") as RectTransform : null;
    }

    /// <summary>
    /// 実際に描画される Graphic だけを対象に、HUD要素の占有範囲を求めます。
    /// 空のコンテナ用 RectTransform を含めると範囲が不当に広がるため、除外しています。
    /// </summary>
    private static bool TryGetGraphicBounds(RectTransform root, Transform target, out Bounds bounds)
    {
        bounds = new Bounds();
        bool hasAny = false;

        foreach (Graphic graphic in target.GetComponentsInChildren<Graphic>(true))
        {
            Bounds graphicBounds =
                RectTransformUtility.CalculateRelativeRectTransformBounds(root, graphic.rectTransform);
            if (!hasAny)
            {
                bounds = graphicBounds;
                hasAny = true;
                continue;
            }

            bounds.Encapsulate(graphicBounds);
        }

        return hasAny;
    }

    private static bool Overlaps(Bounds a, Bounds b)
    {
        return a.min.x < b.max.x && b.min.x < a.max.x &&
               a.min.y < b.max.y && b.min.y < a.max.y;
    }

    private static void AccumulateProjected(
        MiniMapProjector projector,
        List<Vector3> path,
        ref Vector2 min,
        ref Vector2 max)
    {
        for (int index = 0; index < path.Count; index++)
        {
            Vector2 point = projector.ToLocal(path[index]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
    }

    private static Vector2 GetWorldSizeXZ(List<Vector3> inner, List<Vector3> outer)
    {
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        AccumulateWorld(inner, ref min, ref max);
        AccumulateWorld(outer, ref min, ref max);
        return max - min;
    }

    private static void AccumulateWorld(List<Vector3> path, ref Vector2 min, ref Vector2 max)
    {
        for (int index = 0; index < path.Count; index++)
        {
            Vector2 point = new Vector2(path[index].x, path[index].z);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
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
