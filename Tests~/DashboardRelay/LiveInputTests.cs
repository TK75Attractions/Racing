using System;

// Exercise the production handle and car gate together, without Unity/device substitutes in their code.
public static class LiveInputTests
{
    private sealed class Device : IDriveInputSource, IDriveSteeringCalibration, IDriveInputStatusSource, IDrivePedalReleaseObserver
    {
        public int PlayerIndex => 0;
        public float FullSteeringInput { get; set; } = 30f;
        public string DeviceId { get; set; }
        public bool IsConnected { get; set; } = true;
        public DriveConnectionState OfflineState = DriveConnectionState.SerialDisconnected;
        public DriveConnectionState ConnectionState => IsConnected ? DriveConnectionState.Ready : OfflineState;
        public DriveInputState State;
        public DriveInputState CurrentState => State;
        public int Acknowledged, Updated, Disposed;
        public void PedalReleaseObserved() { Acknowledged++; }
        public void UpdateInput(float deltaTime) { Updated++; }
        public void Dispose() { Disposed++; }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    public static void Main()
    {
        var serial = new Device { DeviceId = "serial", State = new DriveInputState { pedal = .7f, steering = 8f } };
        var keyboard = new Device { DeviceId = "keyboard", State = new DriveInputState { pedal = 1f, readyPressed = true } };
        var live = new LiveDriveInputSource(0);
        live.SwitchSource(serial, false);
        var car = new PlayerDriveInputGate(live);
        var tutorial = new PlayerDriveInputGate(live);
        Require(car.CurrentState.pedal == .7f, "Initial input must retain startup behavior.");
        car.SetBlocked(true); car.ToggleDirection();
        live.SwitchSource(keyboard, true);
        Require(tutorial.CurrentState.pedal == 0f && !tutorial.CurrentState.readyPressed,
            "A retained tutorial handle must suppress held input after switching.");
        keyboard.State = DriveInputState.Neutral;
        _ = tutorial.CurrentState;
        keyboard.State = new DriveInputState { pedal = 1f, steering = -10f };
        Require(tutorial.CurrentState.pedal == 1f && tutorial.CurrentState.steering == -10f,
            "Practice must follow the replacement input after physical release.");
        Require(car.IsBlocked && car.IsReverse && car.CurrentState.pedal == 0f,
            "Switching devices must preserve the existing pause and reverse state.");
        car.SetBlocked(false);
        Require(car.CurrentState.pedal == -1f, "A retained car gate must drive from the replacement source.");
        keyboard.IsConnected = false;
        Require(!live.IsConnected && DriveInputStatus.Of(live) == DriveConnectionState.SerialDisconnected && live.CurrentState.pedal == 0f,
            "Disconnect diagnostics and neutral input must propagate.");
        live.SwitchSource(serial, false);
        car.RequirePedalRelease(); serial.State = DriveInputState.Neutral;
        int before = serial.Acknowledged;
        _ = live.CurrentState; _ = live.CurrentState;
        Require(serial.Acknowledged == before, "Dashboard/status polling must not acknowledge constant-pedal resume.");
        _ = car.CurrentState;
        Require(serial.Acknowledged == before + 1 && !car.NeedsPedalRelease,
            "Only the production car gate may acknowledge the neutral sample.");
        live.UpdateInput(.02f); live.Dispose();
        Require(serial.Updated == 0 && serial.Disposed == 0 && keyboard.Disposed == 0,
            "The stable handle must leave device updates and ownership to InputManager.");
        live.SwitchSource(null, true);
        Require(!live.IsConnected && live.CurrentState.pedal == 0f, "Detaching must never expose disposed input.");
        TestCalibrationRouting();
        TestDashboardInput();
        TestReleaseDiagnostics();
        Console.WriteLine("LIVE_INPUT_PASS: startup, live car/practice routing, release gate, pause/reverse, disconnect, constant resume and ownership.");
    }
    private static void TestCalibrationRouting()
    {
        var physical = new Device { FullSteeringInput = 90f / 6.5f, State = new DriveInputState { steering = 45f / 6.5f } };
        var lease = new DashboardInputLease(); double now = 0;
        var dashboard = new DashboardDriveInputSource(physical, lease, () => now);
        var live = new LiveDriveInputSource(0); live.SwitchSource(dashboard, false);
        var gate = new PlayerDriveInputGate(live);
        Require(Math.Abs(DriveSteeringCalibration.CanonicalInput(gate.CurrentState.steering, gate.FullSteeringInput) - 15f) < .0001f,
            "Physical half lock must survive dashboard/live/gate routing.");
        // The dashboard supplies canonical keyboard units even over a calibrated serial device.
        Require(lease.TryApply("calibration-test", 1, "begin", DashboardInputButtons.Right, now, out _), "Begin remote lease.");
        dashboard.UpdateInput(0); dashboard.UpdateInput(0);
        Require(gate.FullSteeringInput == 30f && gate.CurrentState.steering == 10f,
            "A remote key lease must use keyboard scale rather than physical calibration.");
        now = 10; dashboard.UpdateInput(0);
        Require(Math.Abs(gate.FullSteeringInput - physical.FullSteeringInput) < .0001f,
            "Lease expiry must restore physical scale.");
        live.SwitchSource(new Device { FullSteeringInput = 6f }, false);
        Require(gate.FullSteeringInput == 6f, "A retained gate must follow calibration when devices change.");
    }

    private static void TestReleaseDiagnostics()
    {
        var device = new Device { State = new DriveInputState { pedal = .8f }, OfflineState = DriveConnectionState.TelemetryStale };
        var gate = new PlayerDriveInputGate(device);
        var events = new System.Collections.Generic.List<string>();
        Action<PlayerDriveInputGate, string> record = (sender, transition) => {
            if (sender == gate) events.Add(transition + ":" + sender.PedalReleaseReason + ":" + sender.NeedsPedalRelease);
        };
        PlayerDriveInputGate.ReleaseChanged += record;
        try
        {
            Require(gate.CurrentState.pedal == .8f, "Diagnostics must not alter normal acceleration.");
            device.IsConnected = false;
            _ = gate.CurrentState; _ = gate.CurrentState;
            Require(events.Count == 1 && events[0] == "RELEASE_REQUIRED:connection_lost:True",
                "A brief telemetry loss must record the release latch once, without per-frame spam.");
            device.IsConnected = true;
            Require(gate.CurrentState.pedal == 0f && gate.NeedsPedalRelease && events.Count == 1,
                "Recovered communications must not resume a held pedal or silently acknowledge release.");
            device.State = DriveInputState.Neutral; _ = gate.CurrentState;
            Require(events.Count == 2 && events[1] == "RELEASE_OBSERVED:connection_lost:False",
                "Record the physical release which clears the wait.");
            gate.RequirePedalRelease("course_reset");
            Require(events[2] == "RELEASE_REQUIRED:course_reset:True", "Course recovery must be distinguishable from a communication loss.");
            gate.RequirePedalRelease("input_mode_changed");
            Require(events[3] == "RELEASE_REQUIRED:input_mode_changed:True", "Changing input mode must retain its own cause.");
            gate.SwitchSource(device);
            Require(events[4] == "RELEASE_REQUIRED:source_changed:True", "Device replacement must retain its own cause.");
        }
        finally { PlayerDriveInputGate.ReleaseChanged -= record; }
        Console.WriteLine("RELEASE_DIAGNOSTICS_PASS: cause, recovery, physical release and unchanged safety latch.");
    }
    private static void TestDashboardInput()
    {
        double now=0;
        var local=new Device { DeviceId="keyboard", IsConnected=false, State=new DriveInputState {pedal=.8f} };
        var lease=new DashboardInputLease();
        var dashboard=new DashboardDriveInputSource(local,lease,()=>now);
        var live=new LiveDriveInputSource(0);live.SwitchSource(dashboard,false);
        var car=new PlayerDriveInputGate(live);
        _=car.CurrentState;Require(car.NeedsPedalRelease,"A disconnected car must require release.");
        Require(lease.TryApply("page",1,"begin",0,now,out _),"Acquire dashboard input.");
        dashboard.UpdateInput(.02f);_=car.CurrentState;
        Require(!car.NeedsPedalRelease&&car.IsConnected,"A neutral handover must arm an existing gate even without a local device.");
        Require(lease.TryApply("page",2,"pulse",DashboardInputButtons.Reset,now,out _),"Queue Space/Slash.");
        now=.02;dashboard.UpdateInput(.02f);
        _=live.CurrentState;
        Require(car.CurrentState.resetPressed&&car.CurrentState.resetPressed,"Status polling must not consume the reset edge.");
        now=.04;dashboard.UpdateInput(.02f);Require(!car.CurrentState.resetPressed,"Reset is one game frame, not a repeated reset.");
        Require(lease.TryApply("page",3,"hold",DashboardInputButtons.Accelerate|DashboardInputButtons.Right,now,out _),"Hold two controls.");
        now=.06;dashboard.UpdateInput(.02f);Require(car.CurrentState.pedal==1f&&car.CurrentState.steering==10f,"Remote movement reaches the production car gate.");
        local.IsConnected=true;now=1.1;
        Require(car.CurrentState.pedal==0f,"The getter must expire remote acceleration even before the next Update.");
        dashboard.UpdateInput(.02f);Require(car.CurrentState.pedal==0f,"Do not resume a held physical pedal on remote disconnect.");
        local.State=DriveInputState.Neutral;_=car.CurrentState;
        local.State=new DriveInputState {pedal=.8f};Require(car.CurrentState.pedal==.8f,"Physical input resumes after release.");
        Require(lease.TryApply("second",1,"begin",DashboardInputButtons.Brake,now,out _),"A new owner can acquire an expired lease.");
        Require(car.CurrentState.pedal==0f,"Do not expose stale movement from the previous controller.");
        dashboard.UpdateInput(.02f);now+=.02;dashboard.UpdateInput(.02f);
        Require(car.CurrentState.pedal==-1f,"New owner supplies its own input.");
        lease.Clear();Require(car.CurrentState.pedal==0f,"Transition/stop clears cached remote input immediately.");
        Console.WriteLine("DASHBOARD_INPUT_PASS: real gate handover, independent button samples, expiry between frames, local release and owner replacement.");
    }
}
