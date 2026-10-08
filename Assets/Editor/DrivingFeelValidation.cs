using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DrivingFeelValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Racing/Validate Driving Feel")]
    public static void Run()
    {
        MiniMapValidation.Run();
        CarStabilityValidation.RunRegressionChecks();
        ValidateStableCamera();
        ValidateVisualBranches();
        Debug.Log("DRIVING_FEEL_VALIDATION_PASS");
    }

    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Run();
    }

    public static void RunFullBatch()
    {
        RunBatch();
        CarStabilityValidation.RunBatch();
    }

    private static void ValidateStableCamera()
    {
        GameObject car = new GameObject("Camera stability car");
        GameObject rig = new GameObject("Camera stability rig");
        try
        {
            Rigidbody body = car.AddComponent<Rigidbody>();
            body.useGravity = false;
            var controller = rig.AddComponent<RaceDirectionCameraController>();
            controller.SetCar(car.transform);
            Require(body.interpolation == RigidbodyInterpolation.Interpolate, "Followed car must interpolate between physics frames.");
            var tick = typeof(RaceDirectionCameraController).GetMethod("UpdateCameraTarget", PrivateInstance);
            float maxHeightDeviation = 0f;
            for (int frame = 0; frame < 180; frame++)
            {
                car.transform.position = new Vector3(frame * .25f, frame % 2 == 0 ? .12f : -.12f, 0f);
                car.transform.rotation = Quaternion.Euler(frame % 2 == 0 ? 8f : -8f, 0f, 5f);
                tick.Invoke(controller, new object[] { false, 1f / 60f });
                maxHeightDeviation = Mathf.Max(maxHeightDeviation, Mathf.Abs(controller.LookTarget.position.y));
                Require(Mathf.Abs(controller.LookTarget.position.x - car.transform.position.x) < .001f,
                    "Horizontal following must not lag behind the moving car.");
                Require(Vector3.Dot(controller.CameraTarget.up, Vector3.up) > .9999f,
                    "Car pitch and roll must not tilt the camera horizon.");
                Require(Mathf.Abs(controller.CameraTarget.position.y - controller.LookTarget.position.y - 2f) < .001f,
                    "Follow and aim must share the same filtered height.");
            }
            Require(maxHeightDeviation < .03f, $"Camera must attenuate rapid vertical jitter: {maxHeightDeviation:F4}m.");
            car.transform.position = new Vector3(100f, 12f, 100f);
            tick.Invoke(controller, new object[] { false, 1f / 60f });
            Require(Vector3.Distance(controller.LookTarget.position, car.transform.position) < .001f,
                "Respawn must snap the camera to its new height.");
            Debug.Log($"CAMERA_STABILITY_PASS: rawJitter=0.12m filteredJitter={maxHeightDeviation:F4}m");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rig);
            UnityEngine.Object.DestroyImmediate(car);
        }
    }

    private static void ValidateVisualBranches()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab");
        GameObject car = UnityEngine.Object.Instantiate(prefab);
        try
        {
            var motion = car.GetComponent<CarBodyMotion>() ?? car.AddComponent<CarBodyMotion>();
            typeof(CarBodyMotion).GetMethod("Awake", PrivateInstance).Invoke(motion, null);
            var visuals = (System.Collections.Generic.List<Transform>)typeof(CarBodyMotion)
                .GetField("visuals", PrivateInstance).GetValue(motion);
            Require(visuals.Count > 0, "The race car must have a visual body branch for motion.");
            foreach (Transform visual in visuals)
                Require(visual.GetComponentInChildren<Collider>(true) == null &&
                    visual.GetComponentInChildren<TireForce>(true) == null,
                    "Visual bounce must never move tire physics or colliders.");
            Debug.Log($"BODY_MOTION_PASS: visualBranches={visuals.Count}");
        }
        finally { UnityEngine.Object.DestroyImmediate(car); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
