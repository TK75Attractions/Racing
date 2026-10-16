using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Physical regression for banked propulsion and the slope-aware camera.</summary>
public static class SlopeTubeDrivingValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 Origin = new Vector3(-5000f, 5000f, -5000f);

    [MenuItem("Racing/Validate Slope Camera And Tube Driving")]
    public static void Run()
    {
        SimulationMode previousMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;
        try
        {
            float referenceSpeed = ValidateBank(0f);
            foreach (float bank in new[] { -90f, -80f, -60f, 60f, 80f, 90f })
            {
                float speed = ValidateBank(bank);
                Require(Mathf.Abs(speed - referenceSpeed) < .1f,
                    $"Bank {bank}: propulsion must match level road ({speed:F3} vs {referenceSpeed:F3}).");
            }
            ValidateCamera();
            typeof(DrivingFeelValidation).GetMethod("ValidateStableCamera", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null);
            SteeringValidation.Run();
            DriftValidation.Run();
            VisualEffectsValidation.Run();
            Debug.Log("SLOPE_TUBE_DRIVING_VALIDATION_PASS");
        }
        finally { Physics.simulationMode = previousMode; }
    }

    private static float ValidateBank(float bank)
    {
        GameObject surface = new GameObject("Validation tube surface");
        GameObject car = null;
        Mesh mesh = null;
        PhysicsMaterial material = null;
        try
        {
            Quaternion bankRotation = Quaternion.AngleAxis(bank, Vector3.forward);
            surface.transform.SetPositionAndRotation(Origin, bankRotation);
            mesh = new Mesh { vertices = new[] { new Vector3(-100, 0, -100), new Vector3(-100, 0, 100),
                new Vector3(100, 0, -100), new Vector3(100, 0, 100) }, triangles = new[] { 0, 1, 2, 2, 1, 3 } };
            mesh.RecalculateNormals();
            MeshCollider collider = surface.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            TubeDriveSurface.Ensure(collider);
            Require(TubeDriveSurface.Contains(collider), "Configured tube must be marked as drivable.");
            material = new PhysicsMaterial { dynamicFriction = 0f, staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum };
            collider.sharedMaterial = material;
            Quaternion rotation = bankRotation * Quaternion.Euler(0, 25, 0);
            car = CreateCar(rotation, out DebugMover mover, out Rigidbody body);
            foreach (SphereCollider wheel in car.GetComponentsInChildren<SphereCollider>()) wheel.sharedMaterial = material;
            Vector3 normal = bankRotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            car.transform.position = Origin + normal * .3f;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            Physics.SyncTransforms();
            foreach (GroundCheck wheel in car.GetComponentsInChildren<GroundCheck>())
                Require(wheel.CheckNow(), $"Bank {bank}: tire must retain contact. wheel={wheel.transform.position}, " +
                    $"car={car.transform.position}, body={body.position}, surface={collider.bounds}, " +
                    $"hits={Physics.RaycastAll(wheel.transform.position, -normal, 1f).Length}");

            // Unmarked buildings must still reject wheel drive on steep walls.
            if (Mathf.Abs(bank) >= 80f)
            {
                surface.GetComponent<TubeDriveSurface>().enabled = false;
                foreach (GroundCheck wheel in car.GetComponentsInChildren<GroundCheck>())
                    Require(!wheel.CheckNow(), "Unmarked vertical walls must not become drivable.");
                surface.GetComponent<TubeDriveSurface>().enabled = true;
            }

            body.linearVelocity = forward * 10f;
            Set(mover, "appliedPedalInput", 1f);
            for (int step = 0; step < 50; step++)
            {
                Invoke(mover, "ApplyTireForces");
                Invoke(mover, "ApplyVelocityResistance");
                Physics.Simulate(.02f);
                car.transform.SetPositionAndRotation(body.position, body.rotation);
                Physics.SyncTransforms();
                foreach (TireForce tire in car.GetComponentsInChildren<TireForce>())
                    Require(tire.IsGrounded, $"Bank {bank}: propulsion contact dropped at step {step}.");
            }
            float speed = Vector3.Dot(body.linearVelocity, forward);
            Require(speed > 20f, $"Bank {bank}: pedal must accelerate on the tube side ({speed:F3}).");

            // A real banked drift must not redirect velocity into/out of the surface.
            car.transform.position = Origin + normal * .3f;
            body.linearVelocity = bankRotation * Vector3.forward * 20f;
            body.angularVelocity = Vector3.zero;
            body.constraints = RigidbodyConstraints.None;
            Set(mover, "isDrifting", true);
            Set(mover, "driftGripBlend", 1f);
            Physics.SyncTransforms();
            Invoke(mover, "ApplyDriftSpinProtection", .02f);
            Physics.Simulate(.02f);
            Require(Mathf.Abs(Vector3.Dot(body.linearVelocity, normal)) < .02f,
                $"Bank {bank}: drift must preserve surface-normal velocity.");
            Require(Vector3.ProjectOnPlane(body.angularVelocity, normal).magnitude < .02f,
                $"Bank {bank}: drift steering must rotate about the road normal.");
            Require(Mathf.Abs(body.linearVelocity.magnitude - 20f) < .02f,
                $"Bank {bank}: spin protection must preserve speed.");
            Debug.Log($"TUBE_BANK_PASS: bank={bank}, finalSpeed={speed:F3}, driftSpeed={body.linearVelocity.magnitude:F3}");
            return speed;
        }
        finally
        {
            if (car != null) UnityEngine.Object.DestroyImmediate(car);
            UnityEngine.Object.DestroyImmediate(surface);
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static GameObject CreateCar(Quaternion rotation, out DebugMover mover, out Rigidbody body)
    {
        var car = new GameObject("Slope/tube regression car");
        car.transform.SetPositionAndRotation(Origin, rotation);
        body = car.AddComponent<Rigidbody>();
        body.useGravity = false;
        for (int i = 0; i < 4; i++)
        {
            var wheel = new GameObject("Wheel");
            wheel.transform.SetParent(car.transform, false);
            wheel.transform.localPosition = new Vector3(i % 2 == 0 ? -.6f : .6f, 0, i < 2 ? 1f : -1f);
            wheel.AddComponent<SphereCollider>().radius = .3f;
            TireForce tire = wheel.AddComponent<TireForce>();
            Set(tire, "isFrontTire", i < 2);
            Invoke(wheel.GetComponent<GroundCheck>(), "Awake");
            Invoke(tire, "Awake");
        }
        mover = car.AddComponent<DebugMover>();
        Invoke(mover, "Awake");
        return car;
    }

    private static void ValidateCamera()
    {
        GameObject car = CreateCar(Quaternion.identity, out _, out _);
        GameObject rig = new GameObject("Slope camera regression");
        GameObject road = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            var camera = rig.AddComponent<RaceDirectionCameraController>();
            road.transform.localScale = new Vector3(100, 1, 100);
            foreach (float slope in new[] { -20f, 0f, 20f })
            {
                Quaternion rotation = Quaternion.Euler(-slope, 0, 0);
                road.transform.SetPositionAndRotation(Origin - rotation * Vector3.up * .5f, rotation);
                car.transform.SetPositionAndRotation(Origin + rotation * Vector3.up * .3f, rotation);
                Physics.SyncTransforms();
                CheckWheels(car);
                camera.SetCar(car.transform);
                Vector3 direction = camera.CameraTarget.forward;
                Require(Vector3.Angle(direction, rotation * Vector3.forward) < .05f,
                    $"Slope {slope}: camera must pitch along the road.");
                Vector3 view = (camera.LookTarget.position - camera.CameraTarget.position).normalized;
                float relativePitch = Mathf.Asin(view.y) * Mathf.Rad2Deg - slope;
                Require(relativePitch > -16f && relativePitch < -12f,
                    $"Slope {slope}: view must retain road visibility ({relativePitch:F2}).");
                float height = car.transform.position.y;
                Vector3 start = car.transform.position;
                for (int frame = 1; frame <= 60; frame++)
                {
                    // Actual slope travel plus wheel vibration: no steady ascent lag.
                    car.transform.position = start + rotation * Vector3.forward * (frame * .1f) +
                        Vector3.up * (frame % 2 == 0 ? .04f : -.04f);
                    Physics.SyncTransforms();
                    CheckWheels(car);
                    Invoke(camera, "UpdateCameraTarget", false, 1f / 60f);
                    float filtered = (float)Get(camera, "smoothedHeight");
                    float expected = height + (rotation * Vector3.forward).y * frame * .1f;
                    Require(Mathf.Abs(filtered - expected) < .02f, "Slope elevation must track without vibration or sustained lag.");
                    Require(Mathf.Abs(camera.CameraTarget.right.y) < .001f, "Camera must keep a level horizon.");
                }
                float pitch = camera.CameraTarget.eulerAngles.x;
                car.transform.position += Vector3.up * 3f;
                car.transform.rotation = Quaternion.Euler(50, 0, 70);
                Physics.SyncTransforms();
                CheckWheels(car);
                for (int frame = 0; frame < 60; frame++) Invoke(camera, "UpdateCameraTarget", false, 1f / 60f);
                Require(Mathf.Abs(Mathf.DeltaAngle(camera.CameraTarget.eulerAngles.x, pitch)) < .05f,
                    "Airborne body pitch/roll must not whip the camera.");
                Debug.Log($"SLOPE_CAMERA_PASS: slope={slope}, relativeViewPitch={relativePitch:F2}");
            }
            // Fast vertical vibration with no support retains the original height filtering.
            road.SetActive(false);
            car.transform.SetPositionAndRotation(Origin, Quaternion.identity);
            CheckWheels(car);
            camera.SetCar(car.transform);
            for (int frame = 0; frame < 180; frame++)
            {
                car.transform.position = Origin + Vector3.up * (frame % 2 == 0 ? .12f : -.12f);
                Invoke(camera, "UpdateCameraTarget", false, 1f / 60f);
                Require(Mathf.Abs((float)Get(camera, "smoothedHeight") - Origin.y) < .03f, "Camera must attenuate vertical jitter.");
            }
            car.transform.position = Origin + new Vector3(100, 12, 100);
            Invoke(camera, "UpdateCameraTarget", false, 1f / 60f);
            Require(Mathf.Abs((float)Get(camera, "smoothedHeight") - car.transform.position.y) < .001f,
                "Respawn must snap height immediately.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rig);
            UnityEngine.Object.DestroyImmediate(car);
            UnityEngine.Object.DestroyImmediate(road);
        }
    }

    private static void CheckWheels(GameObject car)
    {
        foreach (GroundCheck wheel in car.GetComponentsInChildren<GroundCheck>()) wheel.CheckNow();
    }
    private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
