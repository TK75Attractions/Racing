using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Physical seam crossings with the production car, plus open-edge launch protection.</summary>
public static class RoadSeamValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void RunAllFixes()
    {
        Run();
        RaceCourseValidation.Run();
        ModernUIValidation.Run();
        PlayerAudioValidation.Run();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        MultiDisplayValidation.Run();
        GoalSensor goal = UnityEngine.Object.FindFirstObjectByType<GoalSensor>();
        Transform gate = goal.transform.Find("CircuitFinishGate");
        Require(gate != null && gate.GetComponentsInChildren<MeshRenderer>().Length == 5,
            "The actual finish must contain the complete FINISH gate.");
        Require(gate.GetComponentsInChildren<Collider>().Length == 0, "Finish decoration must not introduce road seams.");
        Debug.Log("REQUESTED_RACING_FIXES_VALIDATION_PASS");
    }

    [MenuItem("Racing/Validate Small Road Seams")]
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch editor.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        SimulationMode previous = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        try
        {
            foreach (float height in new[] { .03f, .08f, .15f })
            foreach (float speed in new[] { 15f, 30f, 45f })
                CrossSeam(height, speed);
            ValidateOpenEdge();
            Debug.Log("ROAD_SEAM_VALIDATION_PASS: 9 real-car step crossings and open-edge takeoff.");
        }
        finally { Physics.simulationMode = previous; }
    }

    private static void CrossSeam(float height, float speed)
    {
        GameObject root = new GameObject("Road seam " + height + " at " + speed);
        try
        {
            Road(root, "Before", new Vector3(0, -.5f, -15), new Vector3(30, 1, 30));
            Road(root, "After", new Vector3(0, height - .5f, 30), new Vector3(30, 1, 60));
            Rigidbody body = Car(root, new Vector3(0, 0, -8));
            CarStabilityController assist = body.GetComponent<CarStabilityController>();
            for (int i = 0; i < 50; i++) { Call(assist, "FixedUpdate"); Physics.Simulate(.02f); }
            body.linearVelocity = Vector3.forward * speed;
            float initialY = body.position.y, peak = initialY, upwardSpeed = 0f;
            for (int i = 0; i < 80; i++)
            {
                Call(assist, "FixedUpdate");
                Physics.Simulate(.02f);
                peak = Mathf.Max(peak, body.position.y);
                upwardSpeed = Mathf.Max(upwardSpeed, body.linearVelocity.y);
            }
            Debug.Log($"ROAD_SEAM height={height:F2} speed={speed:F0} rise={peak-initialY:F3} maxVy={upwardSpeed:F3} z={body.position.z:F2}");
            Require(body.position.z > 8f, "Car must travel across the seam.");
            Require(peak - initialY < height + .35f, "A shallow seam launched the car.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void ValidateOpenEdge()
    {
        GameObject root = new GameObject("Open edge");
        try
        {
            Road(root, "Takeoff", new Vector3(0, -.5f, -15), new Vector3(30, 1, 30));
            Rigidbody body = Car(root, new Vector3(0, 0, -1));
            body.linearVelocity = new Vector3(0, 6, 30);
            Call(body.GetComponent<CarStabilityController>(), "FixedUpdate");
            Require(Mathf.Abs(body.linearVelocity.y - 6f) < .001f, "Seam absorption must preserve a jump over missing road.");
            body.position += Vector3.up * 10f;
            body.linearVelocity = new Vector3(0, 6, 30);
            Call(body.GetComponent<CarStabilityController>(), "FixedUpdate");
            Require(Mathf.Abs(body.linearVelocity.y - 6f) < .001f, "Airborne momentum must be preserved.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static Rigidbody Car(GameObject root, Vector3 position)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab");
        GameObject car = UnityEngine.Object.Instantiate(prefab, root.transform);
        car.transform.SetPositionAndRotation(position, Quaternion.identity);
        Rigidbody body = car.GetComponent<Rigidbody>(); body.isKinematic = false;
        CarStabilityController assist = car.GetComponent<CarStabilityController>(); Call(assist, "Awake");
        foreach (GroundCheck ground in car.GetComponentsInChildren<GroundCheck>()) Call(ground, "Awake");
        Physics.SyncTransforms();
        float bottom = float.MaxValue;
        foreach (TireForce tire in car.GetComponentsInChildren<TireForce>())
            bottom = Mathf.Min(bottom, tire.GetComponent<SphereCollider>().bounds.min.y);
        body.position += Vector3.up * (.03f - bottom);
        Physics.SyncTransforms();
        return body;
    }

    private static void Road(GameObject root, string name, Vector3 position, Vector3 size)
    {
        GameObject road = GameObject.CreatePrimitive(PrimitiveType.Cube); road.name = name;
        road.transform.SetParent(root.transform); road.transform.position = position; road.transform.localScale = size;
        road.GetComponent<Collider>().sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/TestAddForce/New Physics Material.physicMaterial");
    }
    private static void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
