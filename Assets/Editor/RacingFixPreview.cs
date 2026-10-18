using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public static class RacingFixPreview
{
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch editor.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        GameObject root = new GameObject("UI preview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        GameObject view = new GameObject("UI preview camera", typeof(Camera));
        RenderTexture target = new RenderTexture(1920, 1080, 24);
        Texture2D image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Camera camera = view.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true; camera.orthographicSize = 5; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.015f, .02f, .04f); camera.targetTexture = target;
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1f;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            RectTransform title = RacingUITheme.Rect(root.transform, "Title", Vector2.zero, Vector2.one);
            RectTransform play = RacingUITheme.Rect(root.transform, "OnPlay", Vector2.zero, Vector2.one);
            RectTransform result = RacingUITheme.Rect(root.transform, "Result", Vector2.zero, Vector2.one);
            var transition = root.AddComponent<ScreenTransitionController>();
            transition.Initialize(title, play, result, "RACING", "", 0);
            var results = new ResultUIManager(); results.Init(result, 1);
            var session = new RaceSessionResult();
            session.SetPlayerResult(0, new RaceResultRecord {playerNumber=1,finishPosition=1,didFinish=true,totalRaceTime=92.461f});
            session.SetPlayerResult(1, new RaceResultRecord {playerNumber=2,finishPosition=2,didFinish=true,totalRaceTime=94.012f});
            results.ShowResults(session); transition.ApplyStateImmediate(Gmanager.State.Result);
            for (int selected = 0; selected < 2; selected++)
            {
                results.SetMenuState(selected, 0f);
                foreach (PedalButtonFeedback button in root.GetComponentsInChildren<PedalButtonFeedback>())
                    for (int i = 0; i < 60; i++) button.Tick(1f/60f);
                Save(camera, target, image, "selection-" + selected);
            }
            transition.ApplyStateImmediate(Gmanager.State.Countdown);
            for (int count = 3; count >= 0; count--)
            {
                if (count > 0) transition.ShowCountdown(count); else transition.ShowGo();
                Save(camera, target, image, "countdown-" + count);
            }
            Debug.Log("RACING_FIX_PREVIEWS_PASS: /private/tmp/racing-fix-previews");
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(view);
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
        }
    }
    private static void Save(Camera camera, RenderTexture target, Texture2D image, string name)
    {
        Canvas.ForceUpdateCanvases();
        var request = new RenderPipeline.StandardRequest {destination=target};
        if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("Render request unavailable");
        RenderPipeline.SubmitRenderRequest(camera, request);
        RenderTexture.active = target; image.ReadPixels(new Rect(0,0,1920,1080),0,0); image.Apply();
        Directory.CreateDirectory("/private/tmp/racing-fix-previews");
        File.WriteAllBytes("/private/tmp/racing-fix-previews/" + name + ".png", image.EncodeToPNG());
    }
}
