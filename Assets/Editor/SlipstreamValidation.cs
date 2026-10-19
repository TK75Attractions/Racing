using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class SlipstreamValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex => 0;
        public string DeviceId => "slipstream-validation";
        public bool IsConnected { get; set; } = true;
        public DriveInputState CurrentState => new DriveInputState { pedal = 1f };
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    [MenuItem("Racing/Validation/Slipstream Safety")]
    public static void Run()
    {
        Gmanager previous = Gmanager.Control;
        var root = new GameObject("Slipstream validation"); root.SetActive(false);
        try
        {
            Gmanager race = root.AddComponent<Gmanager>(); Gmanager.Control = race;
            RaceFeatureController features = root.AddComponent<RaceFeatureController>(); features.Configure(race);
            Set(race, "<Features>k__BackingField", features);
            race.state = Gmanager.State.Game;
            var courseObject = new GameObject("Straight course"); courseObject.transform.SetParent(root.transform);
            RaceCourse course = courseObject.AddComponent<RaceCourse>();
            var data = new SerializedObject(course);
            data.FindProperty("closedLoop").boolValue = false;
            data.FindProperty("generateRoad").boolValue = false;
            data.FindProperty("cornerRoundingDistance").floatValue = 0;
            data.FindProperty("slopeBlendDistance").floatValue = 0;
            SerializedProperty points = data.FindProperty("waypoints"); points.arraySize = 2;
            for (int i = 0; i < 2; i++)
            {
                SerializedProperty point = points.GetArrayElementAtIndex(i);
                point.FindPropertyRelative("position").vector2Value = new Vector2(0, i * 400);
                point.FindPropertyRelative("height").floatValue = 0;
                point.FindPropertyRelative("width").floatValue = 30;
            }
            data.ApplyModifiedPropertiesWithoutUndo(); course.RebuildCache(); race.course = course;
            var ground = new GameObject("Ground"); ground.transform.SetParent(root.transform);
            ground.transform.position = new Vector3(0, -.5f, 200);
            ground.AddComponent<BoxCollider>().size = new Vector3(40, 1, 400);
            var car = new GameObject("Chaser"); car.transform.SetParent(root.transform);
            car.transform.position = new Vector3(0, .35f, 100);
            Rigidbody body = car.AddComponent<Rigidbody>(); body.useGravity = false;
            for (int i = 0; i < 4; i++)
            {
                var wheel = new GameObject("Wheel"); wheel.transform.SetParent(car.transform, false);
                wheel.transform.localPosition = new Vector3(i % 2 == 0 ? -.7f : .7f, 0, i < 2 ? 1f : -1f);
                wheel.AddComponent<SphereCollider>().radius = .35f;
                TireForce tire = wheel.AddComponent<TireForce>();
                Set(tire, "isFrontTire", i < 2); Set(tire, "isGrounded", true);
                Invoke(wheel.GetComponent<GroundCheck>(), "Awake"); Invoke(tire, "Awake");
            }
            DebugMover mover = car.AddComponent<DebugMover>(); Invoke(mover, "Awake");
            var input = new Input(); mover.SetInputSource(input);
            var targetObject = new GameObject("Target"); targetObject.transform.SetParent(root.transform);
            targetObject.transform.position = new Vector3(0, .35f, 110);
            Rigidbody targetBody = targetObject.AddComponent<Rigidbody>(); targetBody.useGravity = false;
            var own = new RaceParticipant { Index = 0, Car = car, Body = body, Progress = 100 };
            var target = new RaceParticipant { Index = 2, IsCpu = true, Car = targetObject, Body = targetBody, Progress = 110 };
            var participants = (List<RaceParticipant>)typeof(Gmanager).GetField("participants", Private).GetValue(race);
            participants.Add(own); participants.Add(target);
            SlipstreamController controller = car.AddComponent<SlipstreamController>(); controller.Configure(race, own);
            root.SetActive(true); Physics.SyncTransforms();
            body.linearVelocity = targetBody.linearVelocity = Vector3.forward * 10f;
            Require(mover.HasGroundedWheels, "The fixture must have grounded wheels.");
            for (int i = 0; i < 46; i++) controller.Tick(.02f);
            Require(controller.SuccessfulBoosts == 1 && mover.IsSlipstreamBoosting, "A sustained same-road chase must award a bounded boost.");
            features.SetFeature(RaceFeature.Slipstream, false);
            Require(!mover.IsSlipstreamBoosting && controller.PursuitProgress == 0, "OFF must cancel active and pending boosts immediately.");
            features.SetFeature(RaceFeature.Slipstream, true);
            targetBody.position += Vector3.up * 8; Physics.SyncTransforms();
            for (int i = 0; i < 50; i++) controller.Tick(.02f);
            Require(controller.SuccessfulBoosts == 1, "Stacked roads cannot draft each other.");
            targetBody.position -= Vector3.up * 8;
            var wall = new GameObject("Occluding wall"); wall.transform.SetParent(root.transform);
            wall.transform.position = new Vector3(0, 1, 105); wall.AddComponent<BoxCollider>().size = new Vector3(10, 4, 1);
            Physics.SyncTransforms();
            for (int i = 0; i < 50; i++) controller.Tick(.02f);
            Require(controller.SuccessfulBoosts == 1, "Walls must block pursuit.");
            UnityEngine.Object.DestroyImmediate(wall);
            input.IsConnected = false;
            for (int i = 0; i < 50; i++) controller.Tick(.02f);
            Require(controller.SuccessfulBoosts == 1 && !mover.IsSlipstreamBoosting, "Disconnects clear all pursuit state.");
            input.IsConnected = true;
            targetObject.transform.rotation = Quaternion.Euler(0, 180, 0);
            targetBody.rotation = targetObject.transform.rotation; Physics.SyncTransforms();
            for (int i = 0; i < 50; i++) controller.Tick(.02f);
            Require(controller.SuccessfulBoosts == 1, "An oncoming car cannot be a target.");
            Debug.Log("SLIPSTREAM_VALIDATION_PASS: real ground queries, pursuit, live OFF, stacked roads, occlusion, disconnect, reverse direction.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); Gmanager.Control = previous; }
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
