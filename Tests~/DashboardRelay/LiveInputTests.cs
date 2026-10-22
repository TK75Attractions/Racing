using System;

// Exercise the production handle and car gate together, without Unity/device substitutes in their code.
public static class LiveInputTests
{
    private sealed class Device : IDriveInputSource, IDriveInputStatusSource, IDrivePedalReleaseObserver
    {
        public int PlayerIndex => 0;
        public string DeviceId { get; set; }
        public bool IsConnected { get; set; } = true;
        public DriveConnectionState ConnectionState => IsConnected ? DriveConnectionState.Ready : DriveConnectionState.SerialDisconnected;
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
        Console.WriteLine("LIVE_INPUT_PASS: startup, live car/practice routing, release gate, pause/reverse, disconnect, constant resume and ownership.");
    }
}
