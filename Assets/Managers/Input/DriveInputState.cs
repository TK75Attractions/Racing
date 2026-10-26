using System;

[Serializable]
public struct DriveInputState
{
    public float pedal;
    public float steering;
    public bool resetPressed;
    public bool readyPressed;

    public static DriveInputState Neutral => new DriveInputState();
}

public interface IDriveInputSource : IDisposable
{
    int PlayerIndex { get; }
    string DeviceId { get; }
    bool IsConnected { get; }
    DriveInputState CurrentState { get; }

    void UpdateInput(float deltaTime);
}

// Calibration applies only when a vehicle consumes input; menus keep transport units.
public interface IDriveSteeringCalibration
{
    float FullSteeringInput { get; }
}

public static class DriveSteeringCalibration
{
    public const float FullLockUnits = 30f;
    public static float Range(IDriveInputSource source) => source is IDriveSteeringCalibration calibration
        ? calibration.FullSteeringInput : FullLockUnits;
    public static float CanonicalInput(float value, float fullLock)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
        if (float.IsNaN(fullLock) || float.IsInfinity(fullLock) || fullLock <= .0001f) fullLock = FullLockUnits;
        return value / fullLock * FullLockUnits;
    }
}

public enum DriveConnectionState
{
    Ready, Unavailable, KeyboardMissing, SerialDisconnected, SerialPreparing,
    TelemetryWaiting, TelemetryStale, TelemetryDisabled, ProtocolMismatch, SteeringMissing, FirmwareUpdateRequired
}

public interface IDriveInputStatusSource
{
    DriveConnectionState ConnectionState { get; }
}

// A debug input can request a neutral sample, but only the normal gate acknowledges it.
public interface IDrivePedalReleaseObserver
{
    void PedalReleaseObserved();
}

public static class DriveInputStatus
{
    public static DriveConnectionState Of(IDriveInputSource source) => source == null ? DriveConnectionState.Unavailable :
        source is IDriveInputStatusSource diagnostic ? diagnostic.ConnectionState :
        source.IsConnected ? DriveConnectionState.Ready : DriveConnectionState.Unavailable;
}
