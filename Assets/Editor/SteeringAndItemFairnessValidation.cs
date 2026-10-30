using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Production UI and item application tests, without changing the saved race scene.</summary>
public static class SteeringAndItemFairnessValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        string[] settings = { "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset" };
        byte[][] previous = Array.ConvertAll(settings, File.ReadAllBytes);
        try
        {
            ValidateItems();
            foreach (int player in new[] { 0, 1 }) foreach (int width in new[] { 1280, 1920 }) CaptureWarning(player, width);
            Debug.Log("STEERING_ITEM_FAIRNESS_PASS: both players at 720p/1080p, no text overflow or missing glyphs; threshold, release, input transparency, maintenance suppression; forward targets, finished/disabled exclusions, all three afflictions and last-place recovery.");
        }
        finally { for (int i=0; i<settings.Length; i++) File.WriteAllBytes(settings[i],previous[i]); }
    }
    public static void RunNpcTargets()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        typeof(NpcRaceValidation).GetMethod("ValidateItemTargets", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,null);
    }
    private static void ValidateItems()
    {
        var previous = Gmanager.Control;
        var host = new GameObject("Item fairness QA");
        var cars = new List<GameObject>();
        try
        {
            var manager = host.AddComponent<Gmanager>(); Gmanager.Control = manager; manager.state = Gmanager.State.Game;
            var entries = (List<RaceParticipant>)manager.Participants;
            for (int i = 0; i < 4; i++)
            {
                var car = new GameObject("Rank " + (i + 1), typeof(Rigidbody), typeof(CarItemEffects)); cars.Add(car);
                var effects = car.GetComponent<CarItemEffects>();
                typeof(CarItemEffects).GetMethod("Awake", Private).Invoke(effects, null);
                entries.Add(new RaceParticipant { Index=i, IsCpu=true, Car=car, Body=car.GetComponent<Rigidbody>(), Effects=effects, Position=i+1 });
            }
            for (int i = 0; i < 4; i++)
            {
                Require(manager.TryGetItemContext(entries[i].Body, out int rank, out var target) && rank == i+1, "Item context rank");
                Require(target == (i == 0 ? null : entries[i-1].Effects), "Only the nearest ranked car ahead may be selected");
            }
            entries[2].IsCpu=false;
            manager.TryGetItemContext(entries[3].Body,out _,out var cpuTarget);
            Require(cpuTarget==entries[1].Effects,"Skip a closer human and choose the nearest CPU ahead.");
            entries[3].Effects.UseItem(RaceItemType.Confuse,entries[2].Effects);
            Require(!entries[2].Effects.IsConfused,"Even a stale explicit human target must be rejected.");
            entries[2].IsCpu=true;
            var last = entries[3].Effects;
            Require(!last.ApplyOil(entries[0].Effects), "Last-place oil protection");
            Require(!last.ApplyConfuse(entries[0].Effects), "Last-place confuse protection");
            Require(!last.ApplySpin(Vector3.right, entries[0].Effects), "Last-place shield spin protection");
            Require(!last.IsOiled && !last.IsConfused && !last.IsSpinning, "Protected cars must not receive debuff state/UI events");
            var third = entries[2].Effects;
            Require(third.ApplyOil(entries[0].Effects), "Third place still receives oil"); third.ClearAll();
            Require(third.ApplyConfuse(entries[0].Effects), "Third place still receives confuse"); third.ClearAll();
            Require(third.ApplySpin(Vector3.right, entries[0].Effects), "Third place still receives shield spin"); third.ClearAll();
            third.UseItem(RaceItemType.Rocket, null);
            Require(third.ApplyConfuse(entries[0].Effects), "Prepare a pre-existing affliction");
            third.UseItem(RaceItemType.Oil, null);
            entries[2].Position=4; entries[3].Position=3;
            typeof(Gmanager).GetMethod("UpdateParticipantOrder", Private).Invoke(manager, null);
            Require(!third.IsConfused && third.IsRocketActive, "Dropping to last clears harm while preserving rocket");
            Require((float)typeof(CarItemEffects).GetField("oilDropTimeRemaining", Private).GetValue(third) >= 0f, "Last-place relief must preserve outgoing oil");
            entries[1].Effects.enabled=false;
            manager.TryGetItemContext(entries[3].Body, out _, out var fallback);
            Require(fallback == entries[0].Effects, "Disabled targets must be skipped");
            entries[0].Result = new RaceResultRecord { didFinish=true };
            manager.TryGetItemContext(entries[3].Body, out _, out fallback);
            Require(fallback == null, "Do not fall back to attacking behind when all cars ahead are finished/disabled");
        }
        finally
        {
            Gmanager.Control = previous;
            foreach (var car in cars) Object.DestroyImmediate(car);
            Object.DestroyImmediate(host);
        }
    }
    private static void CaptureWarning(int player, int width)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var pipeline=GraphicsSettings.defaultRenderPipeline; var quality=QualitySettings.renderPipeline;
        RenderTexture target=null; Texture2D capture=null;
        try
        {
            GraphicsSettings.defaultRenderPipeline=null; QualitySettings.renderPipeline=null;
            var cameraObject=new GameObject("Warning camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.GetComponent<Camera>(); camera.scene=scene; camera.orthographic=true; camera.transform.position=new Vector3(0,0,-10);
            var host=new GameObject("Player warning canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)); SceneManager.MoveGameObjectToScene(host,scene);
            var canvas=host.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
            var scaler=host.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1920,1080);
            ArcadeUI.Bind(host.transform,player);
            var background=RacingUITheme.Rect(host.transform,"Backdrop",Vector2.zero,Vector2.one).gameObject.AddComponent<RawImage>();
            background.texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/ArcadeReference/Race.jpg"); background.raycastTarget=false;
            var warning=host.AddComponent<SteeringLimitWarningUI>(); warning.Configure(null,player);
            warning.Present(134f,false); Require(!warning.IsWarningVisible,"Below threshold");
            warning.Present(player==0?135f:-135f,false); Require(warning.IsWarningVisible,"Both physical directions");
            warning.Present(130f,false); Require(warning.IsWarningVisible,"Hysteresis");
            warning.Present(124f,false); Require(!warning.IsWarningVisible,"Release");
            warning.Present(150f,true); Require(!warning.IsWarningVisible,"Maintenance must take priority");
            warning.Present(150f,false); warning.Present(null,false); Require(!warning.IsWarningVisible,"Stale/disconnected telemetry");
            warning.Present(player==0?150f:-150f,false);
            var wheel=host.GetComponentInChildren<SteeringReturnGraphic>();
            Require(wheel.ReturnDirection==(player==0?1:-1),"Return arrow must oppose the physical wheel angle.");
            wheel.SetAnimation(wheel.ReturnDirection,0);float initial=wheel.WheelDegrees;
            wheel.SetAnimation(wheel.ReturnDirection,.8f);Require(Mathf.Abs(wheel.WheelDegrees)<Mathf.Abs(initial),"Wheel must animate toward neutral.");
            target=new RenderTexture(width,width*9/16,24); camera.targetTexture=target; target.Create();
            camera.Render();
            Canvas.ForceUpdateCanvases();
            foreach(var text in host.GetComponentsInChildren<TMP_Text>())
            { text.ForceMeshUpdate(); Require(!text.isTextOverflowing,"Warning text overflow: " + width + " " + text.text + " rect=" + text.rectTransform.rect + " preferred=" + text.preferredWidth + "," + text.preferredHeight); Require(text.font.HasCharacters(text.text),"Missing warning glyphs"); }
            foreach(var graphic in host.GetComponentsInChildren<Graphic>()) Require(!graphic.raycastTarget,"Warning must not intercept input");
            camera.Render(); var old=RenderTexture.active; RenderTexture.active=target;
            try { capture=new Texture2D(width,width*9/16,TextureFormat.RGB24,false); capture.ReadPixels(new Rect(0,0,width,width*9/16),0,0); capture.Apply(); }
            finally { RenderTexture.active=old; }
            Directory.CreateDirectory("Artifacts/SteeringFairItems");
            File.WriteAllBytes($"Artifacts/SteeringFairItems/warning-p{player+1}-{width}.png",capture.EncodeToPNG());
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline=pipeline; QualitySettings.renderPipeline=quality;
            if(capture!=null)Object.DestroyImmediate(capture);
            foreach (var cam in scene.GetRootGameObjects()) if(cam.TryGetComponent<Camera>(out var c)) c.targetTexture=null;
            if(target!=null)Object.DestroyImmediate(target);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
}
