using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Capture the production P1 night camera stack without UI; no runtime background camera.</summary>
public static class TitleBackgroundBuilder
{
    private const string Output = "Assets/Resources/UI/Neon/TitleBackground.png";

    [MenuItem("Racing/UI/Build Title Background from P1 Start View (Play Mode)")]
    public static void Build()
    {
        Gmanager manager = Gmanager.Control;
        RaceEnvironmentController environment = RaceEnvironmentController.Active;
        if (!Application.isPlaying || manager == null || environment == null ||
            environment.CurrentTimeOfDay != RaceEnvironmentController.TimeOfDay.Night ||
            (manager.state != Gmanager.State.Countdown && manager.state != Gmanager.State.Game))
            throw new InvalidOperationException("Start a night race and pause at the start gate before capturing the title.");
        Transform car = manager.GetPlayerCarTransform(0);
        RaceCourse course = manager.course;
        if (car == null || course == null || !course.TryGetSampleAtProgress(0f, out var start) ||
            Vector3.Distance(car.position, start.position) > 100f)
            throw new InvalidOperationException("P1 must be at the start gate, with the car and lanterns visible in the game camera.");
        Camera camera = manager.GetPlayerCaptureCamera(0);
        Camera view = manager.GetPlayerViewCamera(0);
        if (camera == null || view == null || !view.GetUniversalAdditionalCameraData().renderPostProcessing)
            throw new InvalidOperationException("The production night camera stack and post processing are required.");

        var stack = new List<Camera> { camera };
        stack.AddRange(camera.GetUniversalAdditionalCameraData().cameraStack);
        var previous = new RenderTexture[stack.Count];
        Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        var enabled = new bool[canvases.Length];
        var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        Texture2D photo = null;
        RenderTexture active = RenderTexture.active;
        for (int i = 0; i < stack.Count; i++) if (stack[i] != null) previous[i] = stack[i].targetTexture;
        for (int i = 0; i < canvases.Length; i++) enabled[i] = canvases[i].enabled;
        try
        {
            target.Create();
            for (int i = 0; i < canvases.Length; i++) canvases[i].enabled = false;
            for (int i = 0; i < stack.Count; i++) if (stack[i] != null) stack[i].targetTexture = target;
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new InvalidOperationException("The production camera stack does not support StandardRequest.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            photo = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            photo.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            photo.Apply();
            File.WriteAllBytes(Output, photo.EncodeToPNG());
            AssetDatabase.ImportAsset(Output, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(Output);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            Debug.Log("TITLE_BACKGROUND_BUILT: " + Output + "; inspect the gate, lanterns, car and title text contrast before committing the image.");
        }
        finally
        {
            RenderTexture.active = active;
            for (int i = 0; i < stack.Count; i++) if (stack[i] != null) stack[i].targetTexture = previous[i];
            for (int i = 0; i < canvases.Length; i++) if (canvases[i] != null) canvases[i].enabled = enabled[i];
            if (photo != null) UnityEngine.Object.DestroyImmediate(photo);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
