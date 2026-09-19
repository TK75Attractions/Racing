using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Unity -batchmode -executeMethod DriftChargeValidation.Run -quit でも実行できます。
public static class DriftChargeValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Racing/Validate Drift Charge")]
    public static void Run()
    {
        ValidateTiers();
        ValidateCarColors();
        ValidateScreenEffect();
        Debug.Log("Drift charge validation passed.");
    }

    private static void ValidateTiers()
    {
        GameObject car = new GameObject("Drift charge tier validation");
        try
        {
            DebugMover mover = car.AddComponent<DebugMover>();
            Require(mover.MaxDriftChargeTier == 3, "The default setup must provide three charge tiers.");

            SetCharge(mover, 0f);
            Require(mover.DriftChargeTier == 0, "An empty charge must stay at the lowest tier.");
            Require(!mover.IsDriftChargeFull, "An empty charge must not report a full charge.");

            SetCharge(mover, 0.33f * 3f);
            Require(mover.DriftChargeTier == 0, "Just below a threshold must keep the previous tier.");
            SetCharge(mover, 0.34f * 3f);
            Require(mover.DriftChargeTier == 1, "Reaching a threshold must raise the tier.");
            SetCharge(mover, 0.66f * 3f);
            Require(mover.DriftChargeTier == 1, "Mid charge must not skip a tier.");
            SetCharge(mover, 0.67f * 3f);
            Require(mover.DriftChargeTier == 2, "The second threshold must raise the tier again.");
            SetCharge(mover, 3f);
            Require(mover.DriftChargeTier == 3, "A maximum charge must reach the last tier.");
            Require(mover.IsDriftChargeFull, "A maximum charge must report a full charge.");
            Near(mover.NormalizedDriftCharge, 1f, "A maximum charge must be fully normalized.");

            mover.SuppressInputAfterRespawn();
            Require(mover.DriftChargeTier == 0 && !mover.IsDriftChargeFull,
                "Respawn must drop the charge display back to the lowest tier.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(car);
        }
    }

    private static void ValidateCarColors()
    {
        GameObject car = new GameObject("Drift charge visual validation");
        try
        {
            DebugMover mover = car.AddComponent<DebugMover>();
            DriftChargeVisual visual = car.AddComponent<DriftChargeVisual>();
            typeof(DriftChargeVisual).GetField("mover", PrivateInstance).SetValue(visual, mover);
            Color[] colors = (Color[])typeof(DriftChargeVisual)
                .GetField("tierColors", PrivateInstance).GetValue(visual);
            Require(colors != null && colors.Length >= mover.MaxDriftChargeTier + 1,
                "Every charge tier must have its own spark color.");

            for (int tier = 0; tier <= mover.MaxDriftChargeTier; tier++)
            {
                SetCharge(mover, tier == 0 ? 0f : ThresholdCharge(mover, tier));
                Require(mover.DriftChargeTier == tier, $"Tier {tier} must be reachable from its threshold.");
                Require(visual.CurrentTierColor == colors[tier], $"Tier {tier} must report its own color.");
            }

            for (int tier = 1; tier <= mover.MaxDriftChargeTier; tier++)
            {
                Require(colors[tier] != colors[tier - 1], "Neighbouring tiers must be told apart by color.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(car);
        }
    }

    private static void ValidateScreenEffect()
    {
        GameObject root = new GameObject("Drift charge volume validation");
        VolumeProfile shared = ScriptableObject.CreateInstance<VolumeProfile>();
        VolumeStack stack = VolumeManager.instance.CreateStack();
        try
        {
            Volume volume = root.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 500f;
            volume.sharedProfile = shared;
            VManager manager = root.AddComponent<VManager>();
            manager.Init();
            manager.SetVignette(0.3f);

            Camera[] cameras = new Camera[2];
            for (int index = 0; index < cameras.Length; index++)
            {
                GameObject cameraObject = new GameObject($"ValidationCamera{index}");
                cameraObject.transform.SetParent(root.transform);
                cameras[index] = cameraObject.AddComponent<Camera>();
                cameras[index].GetUniversalAdditionalCameraData().volumeLayerMask = 1;
            }

            manager.ConfigurePlayerCameras(cameras);
            // 脈動と最大weightは時間依存のため、検証では固定値にします。
            Set(manager, "chargeMaxWeight", 1f);
            Set(manager, "chargeFullPulseDepth", 0f);
            var p1Data = cameras[0].GetUniversalAdditionalCameraData();
            var p2Data = cameras[1].GetUniversalAdditionalCameraData();

            Color tint = new Color(0.25f, 0.65f, 1f, 1f);
            manager.SetDriftCharge(0, 0.5f, tint, false);
            manager.TickDriftBoost(0.036f);
            Near(manager.GetDriftChargeWeight(0), 0.2f, "Charge visuals must follow the charge smoothly.");
            Near(manager.GetDriftChargeWeight(1), 0f, "Player two must stay unaffected by player one's charge.");
            manager.TickDriftBoost(1f);
            Near(manager.GetDriftChargeWeight(0), 0.5f, "Half a charge must settle at half strength.");

            manager.SetDriftCharge(0, 1f, tint, true);
            manager.TickDriftBoost(1f);
            Near(manager.GetDriftChargeWeight(0), 1f, "A full charge must reach the configured maximum.");
            VolumeManager.instance.Update(stack, cameras[0].transform, p1Data.volumeLayerMask);
            Vignette stackVignette = stack.GetComponent<Vignette>();
            Near(stackVignette.intensity.value, 0.5f, "Charge must add its vignette to the normal look.");
            Require(stackVignette.color.value == tint, "The screen edges must use the tier color.");
            VolumeManager.instance.Update(stack, cameras[1].transform, p2Data.volumeLayerMask);
            Near(stack.GetComponent<Vignette>().intensity.value, 0.3f, "Player two must keep the normal look.");

            manager.SetDriftCharge(0, 0f, tint, false);
            manager.TickDriftBoost(1f);
            Near(manager.GetDriftChargeWeight(0), 0f, "Releasing the charge must clear the screen effect.");

            ValidateGameManagerRouting(root, manager, tint);

            manager.SetDriftCharge(0, 1f, tint, false);
            manager.TickDriftBoost(1f);
            manager.ResetDriftBoosts();
            Near(manager.GetDriftChargeWeight(0), 0f, "Reset must immediately clear the charge effect.");

            Set(manager, "driftChargeEffectsEnabled", false);
            manager.SetDriftCharge(0, 1f, tint, false);
            manager.TickDriftBoost(1f);
            Near(manager.GetDriftChargeWeight(0), 0f, "Disabling charge visuals must keep the screen untouched.");

            // 通常Volume 1個と、プレイヤーごとの加速・速度・チャージ Volume 3個ずつ。
            Require(root.GetComponentsInChildren<Volume>().Length == 7,
                "Charge volumes must be created once per player.");
            UnityEngine.Object.DestroyImmediate(manager);
            Require(p1Data.volumeLayerMask.value == 1, "Disposal must restore the camera configuration.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            VolumeManager.instance.DestroyStack(stack);
            foreach (VolumeComponent component in shared.components) UnityEngine.Object.DestroyImmediate(component);
            UnityEngine.Object.DestroyImmediate(shared);
        }
    }

    private static void ValidateGameManagerRouting(GameObject root, VManager volumeManager, Color tint)
    {
        GameObject controller = new GameObject("Validation Gmanager");
        controller.transform.SetParent(root.transform);
        // エディタ検証ではシーン開始時の Awake を通らないため、必要な参照だけを直接与えます。
        Gmanager game = controller.AddComponent<Gmanager>();
        game.VManager = volumeManager;
        game.state = Gmanager.State.Game;

        GameObject car = new GameObject("Validation charge car");
        car.transform.SetParent(root.transform);
        DebugMover mover = car.AddComponent<DebugMover>();
        DriftChargeVisual visual = car.AddComponent<DriftChargeVisual>();
        typeof(DriftChargeVisual).GetField("mover", PrivateInstance).SetValue(visual, mover);
        Color[] colors = (Color[])typeof(DriftChargeVisual)
            .GetField("tierColors", PrivateInstance).GetValue(visual);
        Set(visual, "tierColors", new[] { colors[0], colors[1], colors[2], tint });

        Array players = (Array)typeof(Gmanager).GetField("players", PrivateInstance).GetValue(game);
        Type playerType = players.GetType().GetElementType();
        object player = Activator.CreateInstance(playerType, true);
        playerType.GetField("mover").SetValue(player, mover);
        playerType.GetField("chargeVisual").SetValue(player, visual);
        players.SetValue(player, 0);

        volumeManager.ResetDriftBoosts();
        SetCharge(mover, 3f);
        typeof(Gmanager).GetMethod("LateUpdate", PrivateInstance).Invoke(game, null);
        volumeManager.TickDriftBoost(1f);
        Near(volumeManager.GetDriftChargeWeight(0), 1f, "Gmanager must forward the car's charge to its display.");
        Near(volumeManager.GetDriftChargeWeight(1), 0f, "Missing cars must not receive charge effects.");

        mover.SuppressInputAfterRespawn();
        typeof(Gmanager).GetMethod("LateUpdate", PrivateInstance).Invoke(game, null);
        volumeManager.TickDriftBoost(1f);
        Near(volumeManager.GetDriftChargeWeight(0), 0f, "Respawn must end the charge effect through Gmanager.");

        SetCharge(mover, 3f);
        typeof(Gmanager).GetMethod("LateUpdate", PrivateInstance).Invoke(game, null);
        volumeManager.TickDriftBoost(1f);
        game.state = Gmanager.State.Result;
        typeof(Gmanager).GetMethod("LateUpdate", PrivateInstance).Invoke(game, null);
        Near(volumeManager.GetDriftChargeWeight(0), 0f, "Result screens must immediately clear charge effects.");
    }

    private static float ThresholdCharge(DebugMover mover, int tier)
    {
        float[] thresholds = (float[])typeof(DebugMover)
            .GetField("driftChargeTierThresholds", PrivateInstance).GetValue(mover);
        float maxCharge = (float)typeof(DebugMover)
            .GetField("maxDriftCharge", PrivateInstance).GetValue(mover);
        return thresholds[tier - 1] * maxCharge;
    }

    private static void SetCharge(DebugMover mover, float charge) =>
        typeof(DebugMover).GetField("driftCharge", PrivateInstance).SetValue(mover, charge);

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, PrivateInstance).SetValue(target, value);

    private static void Near(float actual, float expected, string message) =>
        Require(Mathf.Abs(actual - expected) < 0.0001f, $"{message} Expected {expected}, got {actual}.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
