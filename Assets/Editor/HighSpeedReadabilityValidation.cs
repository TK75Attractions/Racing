using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class HighSpeedReadabilityValidation
{
    public static void Run()
    {
        FastRacePaceValidation.Run();
        BoostVolumeValidation.Run();
        Gmanager previous = Gmanager.Control;
        GameObject owner = new GameObject("Readability feature owner"); owner.SetActive(false);
        GameObject root = new GameObject("Readability volumes");
        VolumeProfile shared = ScriptableObject.CreateInstance<VolumeProfile>();
        VolumeStack stack = VolumeManager.instance.CreateStack();
        try
        {
            var game = owner.AddComponent<Gmanager>(); Gmanager.Control = game;
            var features = owner.AddComponent<RaceFeatureController>(); features.Configure(game);
            typeof(Gmanager).GetField("<Features>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, features);
            var baseBlur = shared.Add<MotionBlur>(); baseBlur.intensity.Override(.02f);
            Volume volume = root.AddComponent<Volume>(); volume.sharedProfile = shared; volume.isGlobal = true; volume.priority = 500;
            VManager visuals = root.AddComponent<VManager>(); visuals.Init();
            var cameras = new Camera[2];
            for (int i = 0; i < 2; i++)
            {
                var cameraRoot = new GameObject("Readability P" + i); cameraRoot.transform.SetParent(root.transform);
                cameras[i] = cameraRoot.AddComponent<Camera>();
                cameras[i].GetUniversalAdditionalCameraData().volumeLayerMask = 1;
            }
            visuals.ConfigurePlayerCameras(cameras);
            visuals.SetRaceSpeedVisual(0, 1); visuals.SetDriftBoost(0, 1); visuals.TickDriftBoost(1);
            UpdateStack(stack, cameras[0]);
            Require(stack.GetComponent<MotionBlur>().intensity.value <= .1801f, "Full-speed boost must keep the bounded blur.");
            Require(Mathf.Abs(stack.GetComponent<LensDistortion>().intensity.value) <= .0451f, "Boost must not overwhelm the road with distortion.");
            Require(stack.GetComponent<ChromaticAberration>().intensity.value <= .0401f, "Boost colour separation must remain readable.");
            Require(stack.GetComponent<Bloom>().intensity.value > .8f, "Bloom and speed cues must remain present.");
            UpdateStack(stack, cameras[1]);
            Near(stack.GetComponent<MotionBlur>().intensity.value, .02f, "P1 boost must not change P2's volume stack.");
            features.SetFeature(RaceFeature.ClearSpeedPresentation, false); visuals.TickDriftBoost(1);
            UpdateStack(stack, cameras[0]);
            Near(stack.GetComponent<MotionBlur>().intensity.value, .7f, "Live OFF must restore the authored full-speed boost blur.");
            Near(stack.GetComponent<LensDistortion>().intensity.value, -.14f, "Live OFF must restore authored distortion.");
            features.SetFeature(RaceFeature.ClearSpeedPresentation, true); visuals.SetDriftBoost(0, 0);
            visuals.SetDriftCharge(0, 1, Color.cyan, true); visuals.TickDriftBoost(1);
            UpdateStack(stack, cameras[0]);
            Require(stack.GetComponent<ChromaticAberration>().intensity.value <= .0401f, "Drift charge must not bypass the colour-separation bound.");
            Require(features.Settings.IsEnabled(RaceFeature.HighSpeedCornering) && features.Settings.IsEnabled(RaceFeature.FastAiPace),
                "Visual switches must not change driving physics or AI pace.");
            Near(baseBlur.intensity.value, .02f, "Readability must never edit the saved volume profile.");
            Debug.Log("HIGH_SPEED_READABILITY_PASS: actual URP stacks, full speed + boost/charge, P2 isolation, live OFF/ON and saved profile.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(owner); Gmanager.Control = previous;
            VolumeManager.instance.DestroyStack(stack);
            foreach (VolumeComponent component in shared.components) UnityEngine.Object.DestroyImmediate(component);
            UnityEngine.Object.DestroyImmediate(shared);
        }
    }
    private static void UpdateStack(VolumeStack stack, Camera camera) =>
        VolumeManager.instance.Update(stack, camera.transform, camera.GetUniversalAdditionalCameraData().volumeLayerMask);
    private static void Near(float actual, float expected, string reason) => Require(Mathf.Abs(actual - expected) < .0001f, reason + " Actual=" + actual);
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
