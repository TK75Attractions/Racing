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
    private sealed class Input : IDriveInputSource, IDriveInputStatusSource
    {
        public int PlayerIndex => 0;
        public string DeviceId => "Steering test QA";
        public DriveConnectionState ConnectionState { get; set; } = DriveConnectionState.Ready;
        public bool IsConnected { get => ConnectionState == DriveConnectionState.Ready; set => ConnectionState = value ? DriveConnectionState.Ready : DriveConnectionState.Unavailable; }
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
        source.ConstantPedal = .65f; keys.IsConnected = false;
        Require(source.IsConnected && source.CurrentState.pedal == .65f, "Constant pedal must work without a keyboard when ESP32 steering is connected.");
        source.Mode = P1SteeringTestMode.KeyboardPedal;
        Require(!source.IsConnected && source.ConnectionState == DriveConnectionState.KeyboardMissing, "W/S mode must still require a keyboard.");
        source.Mode = P1SteeringTestMode.ConstantPedal;
        source.PauseConstantPedal();
        Require(gate.CurrentState.pedal == 0f && source.ConstantPedalPaused, "F8 release must stop the constant pedal without a keyboard.");
        gate.RequirePedalRelease(); source.RequestConstantResume(); gate.SetBlocked(true);
        Require(source.CurrentState.pedal == 0f && gate.CurrentState.pedal == 0f && source.ResumePending, "Raw reads and paused gates cannot acknowledge a resume.");
        gate.SetBlocked(false);
        Require(gate.CurrentState.pedal == 0f && !gate.NeedsPedalRelease && !source.ResumePending && gate.CurrentState.pedal == .65f,
            "F8 resume must send neutral through the existing gate before driving.");
        source.RequestConstantResume(); serial.IsConnected = false;
        Require(gate.CurrentState.pedal == 0f && !source.ResumePending, "Communication loss must cancel an unconsumed resume request.");
        serial.IsConnected = true;
        Require(gate.CurrentState.pedal == 0f && gate.NeedsPedalRelease, "Reconnection still needs an explicit resume.");
        source.RequestConstantResume(); _ = gate.CurrentState;
        Require(gate.CurrentState.pedal == .65f, "An explicit F8 resume must work after reconnection without a keyboard.");
        source.Mode = P1SteeringTestMode.Normal;
        Require(source.IsConnected && source.CurrentState.pedal == .2f, "OFF must restore the original source.");
        source.Dispose();
        RacingHUDFontBuilder.Build();
        ValidateNotices();
        Debug.Log("P1_STEERING_TEST_LOGIC_PASS: independent axes, normal restore, constant race-only, bounds, keyboard optional, pause, disconnect and explicit neutral-acknowledged resume.");
        string port = Environment.GetEnvironmentVariable("RACING_STEERING_TEST_PTY");
        if (!string.IsNullOrEmpty(port)) ValidatePtyAndPhysics(port);
    }

    public static void RunAll()
    {
        Run();
        string wheelPort = Environment.GetEnvironmentVariable("RACING_WHEEL_ONLY_TEST_PTY");
        if (!string.IsNullOrEmpty(wheelPort)) ValidateWheelOnly(wheelPort);
        SkillGameplayValidation.Run();
        RaceFeatureValidation.Run();
    }

    private static void ValidateWheelOnly(string port)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        bool enabled = SessionState.GetBool(SerialSimulatorSession.EnabledKey, false);
        string oldPort = SessionState.GetString(SerialSimulatorSession.PortKey, "");
        float oldDivisor = SessionState.GetFloat(SerialSimulatorSession.DivisorKey, 6.5f);
        Gmanager previous = Gmanager.Control;
        GameObject root = new GameObject("Wheel-only QA"); root.SetActive(false);
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
        InputManager input = null;
        try
        {
            SessionState.SetBool(SerialSimulatorSession.EnabledKey, true); SessionState.SetString(SerialSimulatorSession.PortKey, port);
            SessionState.SetFloat(SerialSimulatorSession.DivisorKey, 6.5f);
            Gmanager race = root.AddComponent<Gmanager>(); Gmanager.Control = race;
            input = root.AddComponent<InputManager>(); race.IManager = input;
            input.isP1SerialP2KeyboardDebugMode = true;
            input.SetP1SteeringTestMode(P1SteeringTestMode.Normal);
            var car = new GameObject("Wheel-only input target", typeof(Rigidbody)); car.transform.SetParent(root.transform);
            var entries = (List<RaceParticipant>)typeof(Gmanager).GetField("participants", Private).GetValue(race);
            entries.Add(new RaceParticipant { Index = 0, Car = car, Body = car.GetComponent<Rigidbody>() });
            race.state = Gmanager.State.Game;
            System.IO.File.WriteAllText(Environment.GetEnvironmentVariable("RACING_WHEEL_ONLY_TEST_START"), "start");
            input.Init();
            DateTime deadline = DateTime.UtcNow.AddSeconds(6);
            while (!input.IsPlayerConnected(0) && DateTime.UtcNow < deadline) { input.UpdateInput(.01f); Thread.Sleep(10); }
            input.SharedSerialInputSource.TrySendCommand("GET_STATUS", Array.Empty<string>(), out _, out _);
            while (input.SharedSerialInputSource.Session.AdsStatus != "Fault" && DateTime.UtcNow < deadline)
            { input.UpdateInput(.01f); Thread.Sleep(10); }
            Step(input, keyboard, Key.W, Key.UpArrow, Key.RightArrow);
            Debug.Log($"P1_WHEEL_ONLY_STATE: status={DriveInputStatus.Of(input.GetPlayerInputSource(0))} mode={input.SharedSerialInputSource.Session.InputMode} ADS={input.SharedSerialInputSource.Session.AdsStatus} p1={input.GetInputState(0).steering}/{input.GetInputState(0).pedal} p2={input.GetInputState(1).steering}/{input.GetInputState(1).pedal}");
            Require(input.SteeringTestMode == P1SteeringTestMode.KeyboardPedal && input.IsPlayerConnected(0) && input.GetInputState(0).steering == 12 && input.GetInputState(0).pedal == 1 &&
                input.GetInputState(1).steering == 10 && input.GetInputState(1).pedal == 1 && input.SharedSerialInputSource.Session.AdsStatus == "Fault",
                "With both ESP32 pedals and P2 unavailable, P1 wheel/W and P2 keyboard must work without inventing ADS health.");
            input.SetP1SteeringTestMode(P1SteeringTestMode.ConstantPedal); input.SetP1ConstantPedal(.65f);
            var gate = new PlayerDriveInputGate(input.GetPlayerInputSource(0)); _ = gate.CurrentState;
            Require(gate.CurrentState.pedal == .65f && gate.CurrentState.steering == 12, "Constant pedal must also work in wheel-only mode.");
            bool stopped = false, reconnected = false; deadline = DateTime.UtcNow.AddSeconds(6);
            while (DateTime.UtcNow < deadline)
            {
                Step(input, keyboard, Key.UpArrow);
                if (!input.IsPlayerConnected(0)) stopped |= gate.CurrentState.pedal == 0 && gate.NeedsPedalRelease;
                else if (stopped)
                {
                    reconnected = gate.CurrentState.pedal == 0 && gate.NeedsPedalRelease;
                    break;
                }
                Thread.Sleep(10);
            }
            Require(stopped && reconnected && input.GetInputState(1).pedal == 1, "P1 loss/reconnect must keep its gate latched and leave P2 keyboard available.");
            input.ResumeP1ConstantPedal(); _ = gate.CurrentState;
            Require(gate.CurrentState.pedal == .65f, "Explicit resume must restore the wheel-only constant pedal after recovery.");
            Debug.Log("P1_STEERING_TEST_WHEEL_ONLY_PASS: real PTY, ADS/pedals/P2 absent, W/S and constant input, P2 keyboard, actual P1 outage, held-input reconnect latch and F8 resume.");
        }
        finally
        {
            input?.SharedSerialInputSource?.Dispose(); InputSystem.RemoveDevice(keyboard); UnityEngine.Object.DestroyImmediate(root);
            Gmanager.Control = previous; SessionState.SetBool(SerialSimulatorSession.EnabledKey, enabled); SessionState.SetString(SerialSimulatorSession.PortKey, oldPort);
            SessionState.SetFloat(SerialSimulatorSession.DivisorKey, oldDivisor);
        }
    }

    private static void ValidateNotices()
    {
        var root = new GameObject("Input notices QA", typeof(RectTransform), typeof(Canvas));
        try
        {
            var ui = new OnPlayUIManager(); ui.Init(root.transform, null, 0);
            var normal = new Input(); var wheel = new Input(); var keys = new Input { IsConnected = false };
            var source = new P1SteeringTestInputSource(normal, keys) { Serial = wheel, Mode = P1SteeringTestMode.ConstantPedal, IsRacing = true };
            var gate = new PlayerDriveInputGate(source);
            var notice = root.transform.Find("ModernHUD/DriverNotice");
            var label = notice.Find("Text").GetComponent<TMPro.TMP_Text>();
            ui.UpdateDriverNotice(gate, null, true);
            Require(!notice.gameObject.activeSelf, "A connected constant pedal without a keyboard must not show a connection error.");
            foreach (DriveConnectionState status in Enum.GetValues(typeof(DriveConnectionState)))
            {
                if (status == DriveConnectionState.Ready) continue;
                wheel.ConnectionState = status; ui.UpdateDriverNotice(gate, null, true);
                Require(notice.gameObject.activeSelf && label.text == DriverInputNotice.Connection(wheel), "HUD must describe the actual wheel status: " + status);
                if (status == DriveConnectionState.SerialPreparing || status == DriveConnectionState.TelemetryWaiting || status == DriveConnectionState.TelemetryStale)
                    Require(!label.text.Contains("接続を確認"), "Preparation and telemetry waits must not claim a missing cable.");
                foreach (bool bold in new[] { false, true }) Require(RacingUIFontCatalog.GetHUD(bold).HasCharacters(label.text), "Input status glyphs must exist in both static HUD fonts.");
            }
            wheel.ConnectionState = DriveConnectionState.Ready; gate.RequirePedalRelease();
            ui.UpdateDriverNotice(gate, null, true);
            Require(label.text == "S / F8で常時ペダルを再開", "The constant latch must describe a usable resume action.");
            source.RequestConstantResume(); _ = gate.CurrentState; ui.UpdateDriverNotice(gate, null, true);
            Require(!notice.gameObject.activeSelf, "Resume must clear the stale notice.");
            source.PauseConstantPedal(); ui.UpdateDriverNotice(gate, null, true);
            Require(label.text == "常時ペダル停止中：F8で再開", "An intentional constant pedal stop must not be reported as disconnected.");
            source.RequestConstantResume(); _ = gate.CurrentState; source.ConstantPedal = 0f; ui.UpdateDriverNotice(gate, null, true);
            Require(label.text.Contains("0%"), "Zero constant pedal must explain its setting.");
            wheel.IsConnected = false; gate.SetBlocked(true); ui.UpdateDriverNotice(gate, null, true);
            Require(!notice.gameObject.activeSelf, "Paused drivers must not see stale control notices.");
            gate.SetBlocked(false); ui.UpdateDriverNotice(gate, null, false);
            Require(!notice.gameObject.activeSelf, "Finished drivers must not see control notices.");
            gate.SwitchSource(normal); ui.UpdateDriverNotice(gate, null, true);
            Require(!label.text.Contains("接続を確認"), "A replaced input must not report the unused wheel's loss.");
            ValidateNoticeLayout(root, ui, source, wheel, label);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Debug.Log("P1_STEERING_TEST_NOTICE_PASS: status-specific messages, optional keyboard, explicit resume, zero/pause settings, blocked/finished/unused-source suppression and static font coverage.");
    }

    private static void ValidateNoticeLayout(GameObject root, OnPlayUIManager ui, P1SteeringTestInputSource source, Input wheel, TMPro.TMP_Text label)
    {
        var cameraObject = new GameObject("Input notice camera");
        var target = new RenderTexture(1280, 720, 24);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; camera.cullingMask = 1 << 30;
            camera.nearClipPlane = .1f; camera.farClipPlane = 20f; camera.targetTexture = target;
            Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1f;
            var scaler = root.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            string output = "Logs/P1SteeringTest"; System.IO.Directory.CreateDirectory(output);
            foreach (DriveConnectionState status in Enum.GetValues(typeof(DriveConnectionState)))
            {
                wheel.ConnectionState = status; source.ConstantPedal = 1f;
                var gate = new PlayerDriveInputGate(source);
                if (status == DriveConnectionState.Ready) gate.RequirePedalRelease();
                ui.UpdateDriverNotice(gate, null, true);
                Canvas.ForceUpdateCanvases(); label.ForceMeshUpdate();
                Require(!label.isTextOverflowing, "Input notice must fit the 720p HUD: " + status);
                if (status != DriveConnectionState.Ready && status != DriveConnectionState.SerialPreparing) continue;
                camera.Render(); RenderTexture.active = target;
                var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                System.IO.File.WriteAllBytes(output + "/notice-" + status + ".png", image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
        finally
        {
            RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
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
        var removedKeyboards = new List<Keyboard>();
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
            _ = gate.CurrentState; // Consume a pending F8 resume using the actual vehicle gate.
            Require(input.GetInputState(0).pedal == .65f, "The real source must apply constant input during racing.");
            input.SetP1SteeringTestMode(P1SteeringTestMode.KeyboardPedal); Step(input, keyboard, Key.W);
            Require(ReferenceEquals(p1, input.GetPlayerInputSource(0)) && gate.NeedsPedalRelease && gate.CurrentState.pedal == 0f,
                "A live mode change keeps the existing adapter/gate and waits for release.");
            Step(input, keyboard); _ = gate.CurrentState;
            input.SetP1SteeringTestMode(P1SteeringTestMode.ConstantPedal); Step(input, keyboard, Key.W, Key.S);
            Require(input.GetInputState(0).pedal == 0f && gate.CurrentState.pedal == 0f && !gate.NeedsPedalRelease,
                "S must override constant input even when W is held and clear the live-switch gate.");
            Step(input, keyboard);
            input.PauseP1ConstantPedal();
            Require(gate.CurrentState.pedal == 0f, "The production F8 release action must stop constant input.");
            gate.RequirePedalRelease(); input.ResumeP1ConstantPedal();
            Require(input.GetInputState(0).pedal == 0f && gate.CurrentState.pedal == 0f && !gate.NeedsPedalRelease,
                "The production F8 resume action must clear the existing gate with a neutral sample.");
            foreach (var device in InputSystem.devices) if (device is Keyboard found) removedKeyboards.Add(found);
            foreach (var device in removedKeyboards) InputSystem.RemoveDevice(device);
            // Device reconfiguration may exceed the real 100 ms TEL timeout; wait for live input,
            // then perform the same explicit F8 resume a driver uses after a genuine outage.
            deadline = DateTime.UtcNow.AddSeconds(6);
            while (!input.IsPlayerConnected(0) && DateTime.UtcNow < deadline) { input.UpdateInput(.01f); Thread.Sleep(10); }
            input.ResumeP1ConstantPedal(); _ = gate.CurrentState;
            Debug.Log($"P1_NO_KEYBOARD_STATE: keyboard={Keyboard.current?.name ?? "none"} status={DriveInputStatus.Of(p1)} raw={input.GetInputState(0).pedal} gated={gate.CurrentState.pedal} release={gate.NeedsPedalRelease} blocked={gate.IsBlocked}");
            Require(Keyboard.current == null && input.IsPlayerConnected(0) && gate.CurrentState.pedal == .65f,
                "Constant input through the real serial adapter must remain connected and drive with no Keyboard device.");
            Physics.simulationMode = SimulationMode.Script; Physics.SyncTransforms();
            Vector3 start = body.position;
            for (int i = 0; i < 80; i++)
            {
                input.UpdateInput(.02f); Call(mover, "FixedUpdate"); Call(car.GetComponent<CarStabilityController>(), "FixedUpdate");
                Physics.Simulate(.02f); Physics.SyncTransforms();
            }
            float distance = Vector3.Distance(start, body.position), heading = Mathf.Abs(Mathf.DeltaAngle(0f, body.rotation.eulerAngles.y));
            Require(distance > 3f && body.linearVelocity.magnitude > 3f && heading > 5f, "The production car must accelerate and turn using the PTY wheel and constant pedal through its gate.");
            foreach (var device in removedKeyboards) InputSystem.AddDevice(device);
            removedKeyboards.Clear(); keyboard.MakeCurrent();
            input.SetP1SteeringTestMode(P1SteeringTestMode.Normal); Step(input, keyboard, Key.W);
            Require(input.GetInputState(0).pedal == 0f && ReferenceEquals(p2, input.GetPlayerInputSource(1)), "OFF restores the original zero serial pedal and leaves P2's adapter unchanged.");
            input.SetP1SteeringTestMode(P1SteeringTestMode.ConstantPedal); Step(input, keyboard);
            input.SharedSerialInputSource.Dispose();
            Require(!input.IsPlayerConnected(0) && input.GetInputState(0).pedal == 0f && gate.CurrentState.pedal == 0f,
                "Real transport closure must cancel constant input.");
            Debug.Log($"P1_STEERING_TEST_PTY_PASS: real RGP input, keyboard W/S and ignored A/D, P2 unchanged, preparation neutral, live switching/release, S priority, F8 release/resume, no keyboard device, production physics distance={distance:F2}m heading={heading:F2}deg speed={body.linearVelocity.magnitude:F2}m/s, normal restore and port closure.");

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

            SessionState.SetBool(SerialSimulatorSession.EnabledKey, true);
            input.isP1SerialP2KeyboardDebugMode = true;
            input.Init(); input.SetP1SteeringTestMode(P1SteeringTestMode.KeyboardPedal);
            transport = input.SharedSerialInputSource;
            deadline = DateTime.UtcNow.AddSeconds(6);
            while (!input.IsPlayerConnected(0) && DateTime.UtcNow < deadline) { input.UpdateInput(.01f); Thread.Sleep(10); }
            Step(input, keyboard, Key.W, Key.UpArrow, Key.RightArrow);
            Require(input.IsPlayerConnected(0) && input.GetInputState(0).steering == 12f && input.GetInputState(0).pedal == 1f &&
                input.GetInputState(1).steering == 10f && input.GetInputState(1).pedal == 1f && transport.Session.InputMode == "P1_STEERING",
                "P1 serial/P2 keyboard must also hold with an explicit PTY session and negotiate wheel-only mode.");
            Debug.Log("P1_STEERING_TEST_ROUTING_PASS: P1 ESP32 wheel and W/S, P2 keyboard, explicit steering-only negotiation; default two-serial routing verified above.");
        }
        finally
        {
            transport?.Dispose();
            foreach (var device in removedKeyboards) InputSystem.AddDevice(device);
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
