using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Unity -batchmode -executeMethod LightningValidation.Run -quit でも実行できます。
public static class LightningValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Racing/Validate Lightning")]
    public static void Run()
    {
        ValidatePath();
        ValidateShader();
        ValidateScreenFlash();
        ValidateFinalLapTrigger();
        ValidateOvertakeTrigger();
        Debug.Log("Lightning validation passed.");
    }

    private static void ValidatePath()
    {
        Vector3[] buffer = new Vector3[129];
        Vector3 from = new Vector3(1f, 2f, 3f);
        Vector3 to = new Vector3(11f, 2f, 3f);
        for (int subdivisions = 1; subdivisions <= 7; subdivisions++)
        {
            int count = LightningEffects.BuildPath(buffer, from, to, subdivisions, 0.3f);
            Require(count == (1 << subdivisions) + 1, "A path must have one more point than its segments.");
            Require(buffer[0] == from && buffer[count - 1] == to, "A path must connect both end points exactly.");
            for (int index = 0; index < count; index++)
            {
                Require(Vector3.Distance(buffer[index], (from + to) * 0.5f) < 10f,
                    "A path must stay near the line between its end points.");
            }
        }

        LightningEffects.BuildPath(buffer, from, to, 4, 0f);
        for (int index = 0; index <= 16; index++)
        {
            Near(buffer[index].y, 2f, "A path without roughness must be straight.");
            Near(buffer[index].z, 3f, "A path without roughness must be straight.");
        }

        Require(LightningEffects.Instance == null, "Edit mode must not create runtime lightning objects.");
        Require(!LightningEffects.Strike(new LightningAnchor(from), new LightningAnchor(to), default),
            "Strikes outside play mode must be ignored safely.");
    }

    private static void ValidateShader()
    {
        Require(Resources.Load<Shader>("LightningBolt") != null, "The lightning shader must be loadable from Resources.");
    }

    /// <summary>
    /// バッチモードでは描画が一度も走らず VolumeManager が未初期化のままなので、カメラを1回描画して初期化します。
    /// </summary>
    public static void EnsureVolumeManagerInitialized()
    {
        if (VolumeManager.instance.isInitialized) return;
        GameObject cameraObject = new GameObject("Volume initialization camera");
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.targetTexture = RenderTexture.GetTemporary(16, 16, 16);
            camera.Render();
            RenderTexture.ReleaseTemporary(camera.targetTexture);
            camera.targetTexture = null;
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
        }
    }

    private static void ValidateScreenFlash()
    {
        EnsureVolumeManagerInitialized();
        GameObject root = new GameObject("Lightning flash validation");
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
            manager.SetColorAdjustments(0.2f, 0f, 0f, 0f, Color.white);

            Camera[] cameras = new Camera[2];
            for (int index = 0; index < cameras.Length; index++)
            {
                GameObject cameraObject = new GameObject($"ValidationCamera{index}");
                cameraObject.transform.SetParent(root.transform);
                cameras[index] = cameraObject.AddComponent<Camera>();
                cameras[index].GetUniversalAdditionalCameraData().volumeLayerMask = 1;
            }

            manager.ConfigurePlayerCameras(cameras);
            Set(manager, "lightningFlashExposure", 1.6f);
            Set(manager, "lightningFlashFadeSeconds", 0.4f);
            var p1Data = cameras[0].GetUniversalAdditionalCameraData();
            var p2Data = cameras[1].GetUniversalAdditionalCameraData();

            manager.FlashLightning(0, 1f, Color.white);
            manager.TickDriftBoost(0.016f);
            Near(manager.GetLightningFlashWeight(0), 1f, "A flash must light the screen on its first frame.");
            Near(manager.GetLightningFlashWeight(1), 0f, "A flash must stay on its own player's screen.");
            VolumeManager.instance.Update(stack, cameras[0].transform, p1Data.volumeLayerMask);
            Near(stack.GetComponent<ColorAdjustments>().postExposure.value, 1.8f,
                "A full flash must add its exposure to the normal look.");
            VolumeManager.instance.Update(stack, cameras[1].transform, p2Data.volumeLayerMask);
            Near(stack.GetComponent<ColorAdjustments>().postExposure.value, 0.2f,
                "Player two must keep the normal exposure.");

            manager.FlashLightning(0, 0.3f, Color.white);
            manager.TickDriftBoost(0.2f);
            Require(manager.GetLightningFlashWeight(0) < 1f && manager.GetLightningFlashWeight(0) > 0.3f,
                "A weaker flash must not cut a stronger one short.");
            manager.TickDriftBoost(1f);
            manager.TickDriftBoost(0.016f);
            Near(manager.GetLightningFlashWeight(0), 0f, "A flash must fade out completely.");

            manager.FlashLightning(1, 1f, Color.white);
            manager.TickDriftBoost(0.016f);
            manager.ResetDriftBoosts();
            Near(manager.GetLightningFlashWeight(1), 0f, "Reset must immediately clear the flash.");

            Set(manager, "lightningFlashEnabled", false);
            manager.FlashLightning(0, 1f, Color.white);
            manager.TickDriftBoost(0.016f);
            Near(manager.GetLightningFlashWeight(0), 0f, "Disabling the flash must keep the screen untouched.");

            Require(root.GetComponentsInChildren<Volume>().Length == 9,
                "Flash volumes must be created once per player.");
            // エディタ検証では OnDestroy が呼ばれないため、破棄時の後片付けを直接呼びます。
            typeof(VManager).GetMethod("OnDestroy", PrivateInstance).Invoke(manager, null);
            Require(p1Data.volumeLayerMask.value == 1, "Disposal must restore the camera configuration.");
            Require(root.GetComponentsInChildren<Volume>(true).Length == 1, "Disposal must remove the flash volumes.");
        }
        finally
        {
            Object.DestroyImmediate(root);
            VolumeManager.instance.DestroyStack(stack);
            foreach (VolumeComponent component in shared.components) Object.DestroyImmediate(component);
            Object.DestroyImmediate(shared);
        }
    }

    private static void ValidateFinalLapTrigger()
    {
        GameObject root = new GameObject("Final lap lightning validation");
        try
        {
            RaceLightningDirector director = root.AddComponent<RaceLightningDirector>();
            director.SetCar(0, root.transform);
            bool[] announced = (bool[])typeof(RaceLightningDirector)
                .GetField("finalLapAnnounced", PrivateInstance).GetValue(director);

            director.UpdateLap(0, 1, 3);
            Require(!announced[0], "The second lap of three must not be the final lap.");
            director.UpdateLap(0, 2, 3);
            Require(announced[0], "Entering the last lap must trigger the final lap strike.");
            Require(!announced[1], "One player's final lap must not affect the other.");

            director.ResetRace();
            Require(!announced[0], "A new race must allow the final lap strike again.");
            director.UpdateLap(1, 0, 1);
            Require(!announced[1], "A one-lap race has no final lap announcement.");
            director.UpdateLap(1, 3, 3);
            Require(announced[1], "Finishing must consume the announcement so it never fires late.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void ValidateOvertakeTrigger()
    {
        GameObject root = new GameObject("Overtake lightning validation");
        GameObject carOne = new GameObject("Overtake car one");
        GameObject carTwo = new GameObject("Overtake car two");
        try
        {
            RaceLightningDirector director = root.AddComponent<RaceLightningDirector>();
            director.SetCar(0, carOne.transform);
            director.SetCar(1, carTwo.transform);
            carTwo.transform.position = new Vector3(0f, 0f, 5f);
            FieldInfo nextOvertake = typeof(RaceLightningDirector).GetField("nextOvertakeTime", PrivateInstance);

            director.UpdateOvertake(10f, 0);
            Near((float)nextOvertake.GetValue(director), 0f, "The first leader must not count as an overtake.");
            director.UpdateOvertake(1f, 1);
            Near((float)nextOvertake.GetValue(director), 0f, "The race start must not count as an overtake.");
            director.UpdateOvertake(10f, 0);
            Require((float)nextOvertake.GetValue(director) > 10f, "A lead change between close cars must strike.");
            director.UpdateOvertake(11f, 1);
            Require((float)nextOvertake.GetValue(director) < 13f + 0.001f && (float)nextOvertake.GetValue(director) > 12f,
                "Back-and-forth passes must respect the cooldown.");
            director.UpdateOvertake(11.5f, -1);
            director.UpdateOvertake(20f, 0);
            Require((float)nextOvertake.GetValue(director) > 20f, "An unknown leader must not reset the last leader.");

            carTwo.transform.position = new Vector3(0f, 0f, 100f);
            director.UpdateOvertake(30f, 1);
            Require((float)nextOvertake.GetValue(director) < 30f, "Distant lead changes such as respawns must be ignored.");
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(carOne);
            Object.DestroyImmediate(carTwo);
        }
    }

    private static void Set(object target, string fieldName, object value)
    {
        target.GetType().GetField(fieldName, PrivateInstance).SetValue(target, value);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new System.Exception(message);
    }

    private static void Near(float actual, float expected, string message)
    {
        if (Mathf.Abs(actual - expected) > 0.001f) throw new System.Exception($"{message} Expected {expected}, got {actual}.");
    }
}
