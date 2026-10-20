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

public enum DriveConnectionState
{
    Ready, Unavailable, KeyboardMissing, SerialDisconnected, SerialPreparing,
    TelemetryWaiting, TelemetryStale, TelemetryDisabled, ProtocolMismatch
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
