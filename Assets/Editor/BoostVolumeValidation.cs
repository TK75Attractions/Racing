using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class BoostVolumeValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Racing/Validate Boost Volumes")]
    public static void Run()
    {
        GameObject root = new GameObject("Boost volume validation");
        VolumeProfile shared = ScriptableObject.CreateInstance<VolumeProfile>();
        VolumeStack stack = VolumeManager.instance.CreateStack();
        try
        {
            var originalBloom = shared.Add<Bloom>();
            originalBloom.intensity.Override(0.7f);
            var originalDistortion = shared.Add<LensDistortion>();
            originalDistortion.intensity.Override(-0.51f);
            Volume volume = root.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 500f;
            volume.sharedProfile = shared;
            VManager manager = root.AddComponent<VManager>();
            manager.Init();
            VolumeProfile runtime = volume.profile;
            manager.Init();
            Require(runtime == volume.profile && runtime != shared, "Init must be idempotent and isolate the shared profile.");
            manager.SetBloom(0.9f);
            Near(originalBloom.intensity.value, 0.7f, "SetBloom must not modify the saved profile.");
            Require(runtime.TryGet(out Bloom runtimeBloom), "Runtime profile must contain URP bloom.");
            Near(runtimeBloom.intensity.value, 0.9f, "URP setters must update the runtime profile.");

            Camera[] cameras = new Camera[2];
            for (int index = 0; index < cameras.Length; index++)
            {
                GameObject cameraObject = new GameObject($"ValidationCamera{index}");
                cameraObject.transform.SetParent(root.transform);
                cameras[index] = cameraObject.AddComponent<Camera>();
                cameras[index].GetUniversalAdditionalCameraData().volumeLayerMask = 1;
                cameras[index].GetUniversalAdditionalCameraData().renderPostProcessing = false;
            }
            manager.ConfigurePlayerCameras(cameras);
            int p1Layer = LayerMask.NameToLayer(VManager.PlayerOneVolumeLayer);
            int p2Layer = LayerMask.NameToLayer(VManager.PlayerTwoVolumeLayer);
            Require(p1Layer >= 0 && p2Layer >= 0, "Dedicated player layers must exist.");
            var p1Data = cameras[0].GetUniversalAdditionalCameraData();
            var p2Data = cameras[1].GetUniversalAdditionalCameraData();
            Require((p1Data.volumeLayerMask & (1 << p2Layer)) == 0 &&
                (p2Data.volumeLayerMask & (1 << p1Layer)) == 0, "Each camera must exclude the other player's boost.");
            Require(p1Data.renderPostProcessing && p2Data.renderPostProcessing, "Both main cameras must enable post processing.");

            manager.SetDriftBoost(0, 1f);
            manager.TickDriftBoost(0.04f);
            Near(manager.GetDriftBoostWeight(0), 0.5f, "Boost must fade in smoothly.");
            Near(manager.GetDriftBoostWeight(1), 0f, "Player two must remain unaffected.");
            manager.TickDriftBoost(0.04f);
            VolumeManager.instance.Update(stack, cameras[0].transform, p1Data.volumeLayerMask);
            Near(stack.GetComponent<Bloom>().intensity.value, 1.4f, "Player one must receive boosted bloom.");
            Near(stack.GetComponent<LensDistortion>().intensity.value, -0.63f, "Boost must add distortion to the existing look.");
            Require(stack.GetComponent<MotionBlur>().intensity.value > 0f, "Boost must enable URP motion blur.");
            VolumeManager.instance.Update(stack, cameras[1].transform, p2Data.volumeLayerMask);
            Near(stack.GetComponent<Bloom>().intensity.value, 0.9f, "Player two must retain normal bloom.");
            manager.SetDriftBoost(1, 0.5f);
            manager.TickDriftBoost(1f);
            Near(manager.GetDriftBoostWeight(0), 1f, "Simultaneous boosts must be independent.");
            Near(manager.GetDriftBoostWeight(1), 0.5f, "Partial charge must produce partial effect strength.");
            manager.SetDriftBoost(0, 0f);
            manager.TickDriftBoost(0.125f);
            Near(manager.GetDriftBoostWeight(0), 0.5f, "Boost must fade out smoothly.");
            manager.TickDriftBoost(0.125f);
            VolumeManager.instance.Update(stack, cameras[0].transform, p1Data.volumeLayerMask);
            Near(stack.GetComponent<LensDistortion>().intensity.value, -0.51f, "Expired boost must restore the normal look.");

            ValidateGameManagerRouting(root, manager);
            manager.ResetDriftBoosts();
            Near(manager.GetDriftBoostWeight(1), 0f, "Reset must immediately clear all effects.");
            manager.ConfigurePlayerCameras(cameras);
            Require(root.GetComponentsInChildren<Volume>().Length == 3, "Reconfiguration must not leak boost volumes.");
            UnityEngine.Object.DestroyImmediate(manager);
            Require(p1Data.volumeLayerMask.value == 1 && !p1Data.renderPostProcessing,
                "Disposal must restore the camera configuration.");
            Require(volume.sharedProfile == shared, "Disposal must retain the original volume asset.");
            Near(originalDistortion.intensity.value, -0.51f, "Boost must never modify the saved look.");
            DriftValidation.Run();
            Debug.Log("Boost volume validation passed.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            VolumeManager.instance.DestroyStack(stack);
            foreach (VolumeComponent component in shared.components) UnityEngine.Object.DestroyImmediate(component);
            UnityEngine.Object.DestroyImmediate(shared);
        }
    }

    private static void ValidateGameManagerRouting(GameObject root, VManager volumeManager)
    {
        GameObject controller = new GameObject("Validation Gmanager");
        controller.transform.SetParent(root.transform);
        // Edit-mode validation does not enter the scene-starting Awake path.
        Gmanager game = controller.AddComponent<Gmanager>();
        game.VManager = volumeManager;
        game.state = Gmanager.State.Game;
        GameObject car = new GameObject("Validation boost car");
        car.transform.SetParent(root.transform);
        DebugMover mover = car.AddComponent<DebugMover>();
        typeof(DebugMover).GetMethod("StartDriftBoost", PrivateInstance).Invoke(mover, new object[] { 9f });
        Near(mover.DriftBoostVisualIntensity, 1f, "Full-charge acceleration must report full visual strength.");
        Array players = (Array)typeof(Gmanager).GetField("players", PrivateInstance).GetValue(game);
        Type playerType = players.GetType().GetElementType();
        object player = Activator.CreateInstance(playerType, true);
        playerType.GetField("mover").SetValue(player, mover);
        players.SetValue(player, 0);
        volumeManager.ResetDriftBoosts();
        typeof(Gmanager).GetMethod("LateUpdate", PrivateInstance).Invoke(game, null);
        volumeManager.TickDriftBoost(1f);
        Near(volumeManager.GetDriftBoostWeight(0), 1f, "Gmanager must forward the car's boost to its display.");
        Near(volumeManager.GetDriftBoostWeight(1), 0f, "Missing cars must not receive effects.");
        mover.SuppressInputAfterRespawn();
        Near(mover.DriftBoostVisualIntensity, 0f, "Respawn must stop reporting boost visuals.");
        typeof(Gmanager).GetMethod("LateUpdate", PrivateInstance).Invoke(game, null);
        volumeManager.TickDriftBoost(1f);
        Near(volumeManager.GetDriftBoostWeight(0), 0f, "Respawn must end the screen effect through Gmanager.");
        volumeManager.SetDriftBoost(0, 1f);
        volumeManager.TickDriftBoost(1f);
        game.state = Gmanager.State.Result;
        typeof(Gmanager).GetMethod("LateUpdate", PrivateInstance).Invoke(game, null);
        Near(volumeManager.GetDriftBoostWeight(0), 0f, "Result screens must immediately clear boost effects.");
    }

    private static void Near(float actual, float expected, string message) =>
        Require(Mathf.Abs(actual - expected) < 0.0001f, $"{message} Expected {expected}, got {actual}.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
