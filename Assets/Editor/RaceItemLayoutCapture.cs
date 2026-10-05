using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>アイテム自動配置の確認用に、俯瞰図と各配置地点の走行視点を PNG で書き出します。</summary>
public static class RaceItemLayoutCapture
{
    public static void Capture(List<RaceItemLayoutBuilder.Placement> placements, string directory)
    {
        Directory.CreateDirectory(directory);
        CaptureOverview(placements, Path.Combine(directory, "overview.png"));
        for (int i = 0; i < placements.Count; i++)
        {
            RaceItemLayoutBuilder.Placement placement = placements[i];
            string label = placement.isRow ? "row" : "crystal";
            Vector3 eye = placement.position - placement.forward * 24f + Vector3.up * 7f;
            Vector3 target = placement.position + Vector3.up * 1.2f;
            RenderView(eye, target, 55f, false, 0f, 1280, 720,
                Path.Combine(directory, $"{i + 1:00}_{label}_{placement.distanceFromStart:0}m.png"));
        }
    }

    private static void CaptureOverview(List<RaceItemLayoutBuilder.Placement> placements, string path)
    {
        RaceCourse course = Object.FindFirstObjectByType<RaceCourse>();
        var centerLine = new List<Vector3>();
        course.CopyCenterPathWorld(centerLine);
        Bounds bounds = new Bounds(centerLine[0], Vector3.zero);
        foreach (Vector3 point in centerLine) bounds.Encapsulate(point);
        bounds.Expand(new Vector3(80f, 0f, 80f));

        // 木や屋根に隠れないよう、高い柱で配置位置を示します（撮影後に削除）。
        var markers = new List<GameObject>();
        Material rowMaterial = ItemVisualFactory.CreateEmissiveMaterial("RowMarker", new Color(1f, 0.2f, 0.8f), 3f);
        Material crystalMaterial = ItemVisualFactory.CreateEmissiveMaterial("CrystalMarker", new Color(0.2f, 0.9f, 1f), 3f);
        Material lineMaterial = ItemVisualFactory.CreateEmissiveMaterial("CourseLineMarker", new Color(1f, 0.9f, 0.1f), 2f);
        foreach (RaceItemLayoutBuilder.Placement placement in placements)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.transform.position = placement.position + Vector3.up * 60f;
            marker.transform.localScale = new Vector3(placement.isRow ? 16f : 10f, 60f, placement.isRow ? 16f : 10f);
            marker.GetComponent<Renderer>().sharedMaterial = placement.isRow ? rowMaterial : crystalMaterial;
            markers.Add(marker);
        }
        for (int i = 0; i < centerLine.Count - 1; i += 4)
        {
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dot.transform.position = centerLine[i] + Vector3.up * 100f;
            dot.transform.localScale = new Vector3(3f, 1f, 3f);
            dot.GetComponent<Renderer>().sharedMaterial = lineMaterial;
            markers.Add(dot);
        }

        try
        {
            float size = Mathf.Max(bounds.extents.z, bounds.extents.x * 1200f / 1600f);
            RenderView(bounds.center + Vector3.up * 600f, bounds.center, 0f, true, size, 1600, 1200, path);
        }
        finally
        {
            foreach (GameObject marker in markers) Object.DestroyImmediate(marker);
            Object.DestroyImmediate(rowMaterial);
            Object.DestroyImmediate(crystalMaterial);
            Object.DestroyImmediate(lineMaterial);
        }
    }

    private static void RenderView(Vector3 eye, Vector3 target, float fieldOfView, bool orthographic, float orthoSize,
        int width, int height, string path)
    {
        GameObject cameraObject = new GameObject("ItemLayoutCaptureCamera");
        RenderTexture texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            Vector3 up = orthographic ? Vector3.forward : Vector3.up;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, up));
            camera.orthographic = orthographic;
            camera.orthographicSize = orthoSize;
            camera.fieldOfView = orthographic ? 60f : fieldOfView;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 3000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.45f, 0.6f, 0.8f);
            camera.targetTexture = texture;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(image);
            texture.Release();
            Object.DestroyImmediate(texture);
        }
    }
}
