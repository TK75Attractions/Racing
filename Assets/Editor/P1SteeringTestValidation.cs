using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class P1SteeringTestValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex => 0;
        public string DeviceId => "Steering test QA";
        public bool IsConnected { get; set; } = true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    [MenuItem("Racing/Validation/P1 Steering Test")]
    public static void Run()
    {
        var normal = new Input { CurrentState = new DriveInputState { pedal = .2f, steering = -3f } };
        var serial = new Input { CurrentState = new DriveInputState { pedal = -1f, steering = 12f } };
        var keys = new Input { CurrentState = new DriveInputState { pedal = 1f, steering = -30f, readyPressed = true, resetPressed = true } };
        var source = new P1SteeringTestInputSource(normal, keys) { Serial = serial };
        Require(source.CurrentState.pedal == .2f && source.CurrentState.steering == -3f, "Normal mode must preserve the original source.");
        source.Mode = P1SteeringTestMode.KeyboardPedal;
        Require(source.CurrentState.pedal == 1f && source.CurrentState.steering == 12f && source.CurrentState.readyPressed && source.CurrentState.resetPressed,
            "Only ESP32 steering and keyboard pedal/buttons may reach the car.");
        source.Mode = P1SteeringTestMode.ConstantPedal; source.ConstantPedal = .65f;
        Require(source.CurrentState.pedal == 1f, "Menus and practice must still use the keyboard pedal.");
        source.IsRacing = true;
        var gate = new PlayerDriveInputGate(source);
        Require(gate.CurrentState.pedal == .65f, "Constant pedal must drive during a race.");
        gate.SetBlocked(true); Require(gate.CurrentState.pedal == 0f, "Pause must still block constant input."); gate.SetBlocked(false);
        serial.IsConnected = false;
        Require(!source.IsConnected && gate.CurrentState.pedal == 0f && gate.NeedsPedalRelease, "Serial loss must stop even a constant pedal.");
        serial.IsConnected = true;
        Require(gate.CurrentState.pedal == 0f && gate.NeedsPedalRelease, "Reconnection cannot reuse the held constant pedal.");
        keys.CurrentState = new DriveInputState { pedal = -1f };
        Require(gate.CurrentState.pedal == 0f && !gate.NeedsPedalRelease, "S releases the pedal and clears the existing gate.");
        keys.CurrentState = DriveInputState.Neutral;
        Require(gate.CurrentState.pedal == .65f, "Releasing S resumes the chosen constant input.");
        source.ConstantPedal = 4f; Require(source.CurrentState.pedal == 1f, "Constant pedal is bounded.");
        source.ConstantPedal = float.NaN; Require(source.CurrentState.pedal == 0f, "Nonfinite pedal must not reach physics.");
        keys.IsConnected = false; Require(!source.IsConnected && source.CurrentState.pedal == 0f, "Without keyboard release controls the debug source must stop.");
        source.Mode = P1SteeringTestMode.Normal;
        Require(source.IsConnected && source.CurrentState.pedal == .2f, "OFF must restore the original source.");
        source.Dispose();
        Debug.Log("P1_STEERING_TEST_LOGIC_PASS: independent axes, normal restore, constant race-only, bounds, pause, disconnect, reconnect release and resume.");
        string port = Environment.GetEnvironmentVariable("RACING_STEERING_TEST_PTY");
        if (!string.IsNullOrEmpty(port)) ValidatePtyAndPhysics(port);
    }

    private static void ValidatePtyAndPhysics(string port)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        Gmanager previous = Gmanager.Control;
        bool enabled = SessionState.GetBool(SerialSimulatorSession.EnabledKey, false);
        string oldPort = SessionState.GetString(SerialSimulatorSession.PortKey, "");
        float divisor = SessionState.GetFloat(SerialSimulatorSession.DivisorKey, 6.5f);
        var physicsMode = Physics.simulationMode;
        GameObject root = new GameObject("P1 steering input QA"); root.SetActive(false);
        GameObject car = null, floor = null;
        TwoPlayerSerialInputSource transport = null;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
        try
        {
            SessionState.SetBool(SerialSimulatorSession.EnabledKey, true);
            SessionState.SetString(SerialSimulatorSession.PortKey, port);
            SessionState.SetFloat(SerialSimulatorSession.DivisorKey, 6.5f);
            var race = root.AddComponent<Gmanager>(); Gmanager.Control = race;
            InputManager input = root.AddComponent<InputManager>(); race.IManager = input;
            string startFile = Environment.GetEnvironmentVariable("RACING_STEERING_TEST_START");
            if (!string.IsNullOrEmpty(startFile)) System.IO.File.WriteAllText(startFile, "start");
            input.Init();
            transport = input.SharedSerialInputSource;
            DateTime deadline = DateTime.UtcNow.AddSeconds(6);
            while (!input.IsPlayerConnected(0) && DateTime.UtcNow < deadline) { input.UpdateInput(.01f); Thread.Sleep(10); }
            Require(input.IsPlayerConnected(0), "Real PTY must complete the production RGP session and fresh TEL.");
            IDriveInputSource p1 = input.GetPlayerInputSource(0), p2 = input.GetPlayerInputSource(1);
            input.SetP1SteeringTestMode(P1SteeringTestMode.KeyboardPedal);
            Step(input, keyboard, Key.W, Key.A);
            Require(Mathf.Abs(input.GetInputState(0).steering - 12f) < .001f && input.GetInputState(0).pedal == 1f,
                "PTY angle 78 / 6.5 must override A; zero ESP32 pedal must not override W.");
            Require(Mathf.Abs(input.GetInputState(1).steering + 6f) < .001f && input.GetInputState(1).pedal == .5f,
                "P2 must retain its original serial steering and pedal.");
            Step(input, keyboard, Key.S, Key.D);
            Require(input.GetInputState(0).pedal == -1f && input.GetInputState(0).steering == 12f, "Keyboard braking must retain the ESP32 angle.");
            input.SetP1SteeringTestMode(P1SteeringTestMode.ConstantPedal); input.SetP1ConstantPedal(.65f);
            foreach (Gmanager.State state in new[] { Gmanager.State.Title, Gmanager.State.Tutorial, Gmanager.State.Countdown, Gmanager.State.Result })
            {
                race.state = state; Step(input, keyboard);
                Require(input.GetInputState(0).pedal == 0f, "Constant pedal must not confirm menus, skip practice or drive during countdown/results.");
            }
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Vector3 origin = new Vector3(5000, 5000, 5000);
            floor.transform.position = origin - Vector3.up * .5f; floor.transform.localScale = new Vector3(300, 1, 300);
            car = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab"), origin + Vector3.up * .36f, Quaternion.identity);
            Rigidbody body = car.GetComponent<Rigidbody>(); body.useGravity = false;
            foreach (TireForce tire in car.GetComponentsInChildren<TireForce>()) { Call(tire.GetComponent<GroundCheck>(), "Awake"); Call(tire, "Awake"); }
            DebugMover mover = car.GetComponent<DebugMover>(); Call(mover, "Awake"); Call(car.GetComponent<CarStabilityController>(), "Awake");
            var gate = new PlayerDriveInputGate(p1); mover.SetInputSource(gate);
            var entries = (List<RaceParticipant>)typeof(Gmanager).GetField("participants", Private).GetValue(race);
            entries.Add(new RaceParticipant { Index = 0, Car = car, Body = body });
            race.state = Gmanager.State.Game; Step(input, keyboard);
            Require(input.GetInputState(0).pedal == .65f, "The real source must apply constant input during racing.");
            input.SetP1SteeringTestMode(P1SteeringTestMode.KeyboardPedal); Step(input, keyboard, Key.W);
            Require(ReferenceEquals(p1, input.GetPlayerInputSource(0)) && gate.NeedsPedalRelease && gate.CurrentState.pedal == 0f,
                "A live mode change keeps the existing adapter/gate and waits for release.");
            Step(input, keyboard); _ = gate.CurrentState;
            input.SetP1SteeringTestMode(P1SteeringTestMode.ConstantPedal); Step(input, keyboard, Key.W, Key.S);
            Require(input.GetInputState(0).pedal == 0f && gate.CurrentState.pedal == 0f && !gate.NeedsPedalRelease,
                "S must override constant input even when W is held and clear the live-switch gate.");
            Step(input, keyboard);
            Physics.simulationMode = SimulationMode.Script; Physics.SyncTransforms();
            Vector3 start = body.position;
            for (int i = 0; i < 80; i++)
            {
                input.UpdateInput(.02f); Call(mover, "FixedUpdate"); Call(car.GetComponent<CarStabilityController>(), "FixedUpdate");
                Physics.Simulate(.02f); Physics.SyncTransforms();
            }
            float distance = Vector3.Distance(start, body.position), heading = Mathf.Abs(Mathf.DeltaAngle(0f, body.rotation.eulerAngles.y));
            Require(distance > 3f && body.linearVelocity.magnitude > 3f && heading > 5f, "The production car must accelerate and turn using the PTY wheel and constant pedal through its gate.");
            input.SetP1SteeringTestMode(P1SteeringTestMode.Normal); Step(input, keyboard, Key.W);
            Require(input.GetInputState(0).pedal == 0f && ReferenceEquals(p2, input.GetPlayerInputSource(1)), "OFF restores the original zero serial pedal and leaves P2's adapter unchanged.");
            input.SetP1SteeringTestMode(P1SteeringTestMode.ConstantPedal); Step(input, keyboard);
            input.SharedSerialInputSource.Dispose();
            Require(!input.IsPlayerConnected(0) && input.GetInputState(0).pedal == 0f && gate.CurrentState.pedal == 0f,
                "Real transport closure must cancel constant input.");
            Debug.Log($"P1_STEERING_TEST_PTY_PASS: real RGP input, keyboard W/S and ignored A/D, P2 unchanged, preparation neutral, live switching/release, S priority, production physics distance={distance:F2}m heading={heading:F2}deg speed={body.linearVelocity.magnitude:F2}m/s, normal restore and port closure.");

            // Also enable the same mode after starting with serial entirely disabled.
            input.SetP1SteeringTestMode(P1SteeringTestMode.Normal);
            SessionState.SetBool(SerialSimulatorSession.EnabledKey, false);
            typeof(InputManager).GetField("esp32Serial", Private).SetValue(input, new Esp32SerialConfiguration().WithSimulatorPort(port, 6.5f));
            input.isDebugMode = true; input.Init();
            Require(input.SharedSerialInputSource == null, "Keyboard-only startup must not open a serial port.");
            p1 = input.GetPlayerInputSource(0); p2 = input.GetPlayerInputSource(1);
            input.SetP1SteeringTestMode(P1SteeringTestMode.KeyboardPedal);
            transport = input.SharedSerialInputSource;
            deadline = DateTime.UtcNow.AddSeconds(6);
            while (!input.IsPlayerConnected(0) && DateTime.UtcNow < deadline) { input.UpdateInput(.01f); Thread.Sleep(10); }
            Step(input, keyboard, Key.W, Key.A, Key.UpArrow);
            Require(input.IsPlayerConnected(0) && input.GetInputState(0).steering == 12f && input.GetInputState(0).pedal == 1f &&
                input.GetInputState(1).pedal == 1f && ReferenceEquals(p1, input.GetPlayerInputSource(0)) && ReferenceEquals(p2, input.GetPlayerInputSource(1)),
                "Live activation must open serial for P1 while preserving both adapters and P2 keyboard input.");
            input.SetP1SteeringTestMode(P1SteeringTestMode.Normal); Step(input, keyboard, Key.W, Key.A);
            Require(input.GetInputState(0).steering == -10f && input.GetInputState(0).pedal == 1f, "Normal restores the original keyboard source after live activation.");
            Debug.Log("P1_STEERING_TEST_KEYBOARD_START_PASS: lazy serial opening, stable adapters, P2 keyboard unchanged and original keyboard restored.");
        }
        finally
        {
            transport?.Dispose();
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(car); UnityEngine.Object.DestroyImmediate(floor);
            InputSystem.RemoveDevice(keyboard); Gmanager.Control = previous; Physics.simulationMode = physicsMode;
            SessionState.SetBool(SerialSimulatorSession.EnabledKey, enabled); SessionState.SetString(SerialSimulatorSession.PortKey, oldPort); SessionState.SetFloat(SerialSimulatorSession.DivisorKey, divisor);
        }
    }
    private static void Step(InputManager input, Keyboard keyboard, params Key[] keys)
    { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.Update(); input.UpdateInput(.02f); }
    private static void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
