using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Unity -batchmode -nographics -executeMethod VisualEffectsValidation.Run -quit でも実行できます。
public static class VisualEffectsValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string VolumeProfilePath = "Assets/Scenes/SampleScene/VManager Profile.asset";
    private const string PipelineAssetPath = "Assets/Settings/PC_RPAsset.asset";

    [MenuItem("Racing/Validate Visual Effects")]
    public static void Run()
    {
        ValidateImageQualitySettings();
        ValidateSceneAntialiasing();
        ValidateCameraShake();
        ValidateCollisionClassification();
        ValidateTireSlip();
        ValidateTireMarks();
        ValidateShaders();
        Debug.Log("Visual effects validation passed.");
    }

    private static void ValidateImageQualitySettings()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        Require(profile != null, "The race volume profile must exist.");
        Require(profile.TryGet(out Tonemapping tonemapping) && tonemapping.active,
            "The race volume profile must include an active Tonemapping override.");
        Require(tonemapping.mode.overrideState && tonemapping.mode.value == TonemappingMode.Neutral,
            "Race tonemapping must use the Neutral mode so neon hues are preserved.");

        UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
        Require(pipeline != null, "The PC URP asset must exist.");
        Require(pipeline.supportsHDR, "HDR rendering must stay enabled for bloom and tonemapping.");
        Require(pipeline.colorGradingMode == ColorGradingMode.HighDynamicRange,
            "Color grading must run in HDR so tonemapping receives unclipped values.");
    }

    private static void ValidateSceneAntialiasing()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            Camera baseCamera = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.name == "BackImageCamera") baseCamera = camera;
                }
            }

            Require(baseCamera != null, "SampleScene must contain the BackImageCamera stack base.");
            var data = baseCamera.GetUniversalAdditionalCameraData();
            Require(data.renderType == CameraRenderType.Base, "BackImageCamera must remain the base camera of the stack.");
            // カメラスタックでは、Baseカメラのアンチエイリアス設定がOverlayを含むスタック全体に使われます。
            Require(data.antialiasing == AntialiasingMode.SubpixelMorphologicalAntiAliasing,
                "The stack base camera must use SMAA so every player view is anti-aliased.");
            Require(data.antialiasingQuality == AntialiasingQuality.High, "SMAA must use the High quality preset.");
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void ValidateCameraShake()
    {
        GameObject root = new GameObject("Camera shake validation");
        try
        {
            Transform pivot = new GameObject("VisualEffectPivot").transform;
            pivot.SetParent(root.transform, false);
            RaceSpeedVisualController controller = root.AddComponent<RaceSpeedVisualController>();
            controller.Configure(0, null, null, null, null, pivot, null);

            Set(controller, "highSpeedShakePosition", 0f);
            Set(controller, "boostShakePosition", 0f);
            Set(controller, "highSpeedShakeRotation", 0f);
            Set(controller, "highSpeedPullback", 0f);
            Set(controller, "boostPullback", 0f);
            Set(controller, "impactShakeRotation", 0f);
            Set(controller, "impactShakePosition", 0f);

            Set(controller, "boostShakeRotation", 0f);
            ApplyCamera(controller, 0f, 1f, 0f);
            Require(Quaternion.Angle(pivot.localRotation, Quaternion.identity) < 0.0001f,
                "Boost rotation shake must not use the boost position setting.");

            Set(controller, "boostShakeRotation", 20f);
            ApplyCamera(controller, 0f, 1f, 0f);
            Require(Quaternion.Angle(pivot.localRotation, Quaternion.identity) > 0.001f,
                "Boost rotation shake must follow its own rotation setting.");
            Set(controller, "boostShakeRotation", 0f);

            controller.AddImpact(1f);
            Near(controller.CurrentImpact01, 0f, "Impacts must be ignored while gameplay visuals are inactive.");

            controller.SetGameplayActive(true);
            controller.AddImpact(0.6f);
            Near(controller.CurrentImpact01, 0.6f, "An impact must set the shake strength.");
            controller.AddImpact(0.2f);
            Near(controller.CurrentImpact01, 0.6f, "A weaker impact must not cut a stronger shake short.");
            controller.AddImpact(4f);
            Near(controller.CurrentImpact01, 1f, "Impact strength must be clamped to one.");

            Set(controller, "impactShakeRotation", 20f);
            ApplyCamera(controller, 0f, 0f, 0f);
            Require(Quaternion.Angle(pivot.localRotation, Quaternion.identity) > 0.001f,
                "An impact must rotate the camera pivot.");

            controller.SetGameplayActive(false);
            Near(controller.CurrentImpact01, 0f, "Leaving gameplay must clear the impact shake.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void ValidateCollisionClassification()
    {
        GameObject car = new GameObject("Collision spark validation");
        try
        {
            CarCollisionSparks sparks = car.AddComponent<CarCollisionSparks>();
            Require(sparks.IsWallNormal(Vector3.right), "A vertical surface must count as a wall.");
            Require(sparks.IsWallNormal(new Vector3(1f, 0.5f, 0f).normalized), "A steep surface must count as a wall.");
            Require(!sparks.IsWallNormal(Vector3.up), "The ground must never make wall sparks.");
            Require(!sparks.IsWallNormal(new Vector3(0.3f, 1f, 0f).normalized), "A gentle slope must not count as a wall.");

            Near(sparks.EvaluateImpactStrength(1f), 0f, "A light touch must not shake the camera.");
            Near(sparks.EvaluateImpactStrength(7.5f), 0.5f, "Impact strength must scale with the impact speed.");
            Near(sparks.EvaluateImpactStrength(40f), 1f, "Impact strength must be clamped to one.");
        }
        finally
        {
            Object.DestroyImmediate(car);
        }
    }

    private static void ValidateTireSlip()
    {
        GameObject car = new GameObject("Tire slip validation");
        try
        {
            TireEffectsVisual tires = car.AddComponent<TireEffectsVisual>();
            // 引数: 接地, 水平速度, 横滑り, 前輪か, ドリフト中か, ペダル, 前進速度
            Near(tires.EvaluateSlip(false, 30f, 20f, false, true, -1f, 30f), 0f, "Airborne tires must not leave marks.");
            Near(tires.EvaluateSlip(true, 1f, 20f, false, true, -1f, 1f), 0f, "A crawling car must not leave marks.");
            Near(tires.EvaluateSlip(true, 30f, 1f, false, false, 0f, 30f), 0f, "Normal grip must not leave marks.");
            Near(tires.EvaluateSlip(true, 30f, 9f, true, false, 0f, 30f), 1f, "A full lateral slide must reach full slip.");

            float middle = tires.EvaluateSlip(true, 30f, 6f, true, false, 0f, 30f);
            Require(middle > 0f && middle < 1f, "A partial slide must give a partial slip.");

            Near(tires.EvaluateSlip(true, 30f, 0f, false, true, 0f, 30f), 0.65f, "Drifting rear tires must always slip.");
            Near(tires.EvaluateSlip(true, 30f, 0f, true, true, 0f, 30f), 0f, "Drifting must not force front tire marks.");

            Near(tires.EvaluateSlip(true, 30f, 0f, true, false, -1f, 30f), 0.8f, "Hard braking must leave marks.");
            Near(tires.EvaluateSlip(true, 30f, 0f, true, false, -0.3f, 30f), 0f, "Light braking must not leave marks.");
            Near(tires.EvaluateSlip(true, 5f, 0f, true, false, -1f, -5f), 0f, "Reversing must not count as braking.");
        }
        finally
        {
            Object.DestroyImmediate(car);
        }
    }

    private static void ValidateTireMarks()
    {
        TireMarkRenderer.ClearAll();
        TireMarkRenderer marks = TireMarkRenderer.GetOrCreate();
        try
        {
            Require(TireMarkRenderer.GetOrCreate() == marks, "Tire marks must share one renderer.");
            Set(marks, "maxSegments", 64);
            for (int index = 0; index < 10; index++) AddSegment(marks, index);
            Require(marks.SegmentCount == 10, "Every added segment must be kept until the buffer is full.");

            for (int index = 10; index < 200; index++) AddSegment(marks, index);
            Require(marks.SegmentCount == marks.MaxSegments, "Old segments must be overwritten when the buffer is full.");

            Mesh mesh = marks.GetComponent<MeshFilter>().sharedMesh;
            Require(mesh != null && mesh.vertexCount == marks.MaxSegments * 4,
                "The mark mesh must hold four vertices for every segment.");
            Require(marks.GetComponent<MeshRenderer>().sharedMaterial != null, "The mark mesh must have a material.");

            TireMarkRenderer.ClearAll();
            Require(marks.SegmentCount == 0, "Clearing must remove every tire mark.");
        }
        finally
        {
            Object.DestroyImmediate(marks.gameObject);
        }
    }

    private static void ValidateShaders()
    {
        foreach (string shaderName in new[] { "TireMark", "TireSmoke", "CarCollisionSparks" })
        {
            Shader shader = Resources.Load<Shader>(shaderName);
            Require(shader != null, $"The {shaderName} shader must be loadable from Resources for builds.");
            Require(!ShaderUtil.ShaderHasError(shader), $"The {shaderName} shader must compile without errors.");
        }
    }

    private static void AddSegment(TireMarkRenderer marks, int index)
    {
        Vector3 from = new Vector3(0f, 0f, index);
        Vector3 to = new Vector3(0f, 0f, index + 1f);
        marks.AddSegment(from + Vector3.left * 0.1f, from + Vector3.right * 0.1f, 1f,
            to + Vector3.left * 0.1f, to + Vector3.right * 0.1f, 1f);
    }

    private static void ApplyCamera(RaceSpeedVisualController controller, float speed01, float boost01, float kick01) =>
        typeof(RaceSpeedVisualController).GetMethod("ApplyCamera", PrivateInstance)
            .Invoke(controller, new object[] { speed01, boost01, kick01 });

    private static void Set(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, PrivateInstance);
        Require(field != null, $"{target.GetType().Name}.{name} must exist.");
        field.SetValue(target, value);
    }

    private static void Near(float actual, float expected, string message) =>
        Require(Mathf.Abs(actual - expected) < 0.0001f, $"{message} Expected {expected}, got {actual}.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
