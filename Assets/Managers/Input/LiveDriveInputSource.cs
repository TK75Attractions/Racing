using System;

/// <summary>Stable handle shared by menus, practice and cars while InputManager replaces devices.</summary>
public sealed class LiveDriveInputSource : IDriveInputSource, IDriveSteeringCalibration, IDriveInputStatusSource, IDrivePedalReleaseObserver
{
    private IDriveInputSource source;
    private bool needsPedalRelease;
    public int PlayerIndex { get; }
    public string DeviceId => source?.DeviceId ?? string.Empty;
    public float FullSteeringInput => DriveSteeringCalibration.Range(source);
    public bool IsConnected => source?.IsConnected == true;
    public DriveConnectionState ConnectionState => DriveInputStatus.Of(source);
    public DriveInputState CurrentState
    {
        get
        {
            if (!IsConnected) return DriveInputState.Neutral;
            DriveInputState state = source.CurrentState;
            if (!needsPedalRelease) return state;
            if (Math.Abs(state.pedal) <= .05f) needsPedalRelease = false;
            return DriveInputState.Neutral;
        }
    }

    public LiveDriveInputSource(int playerIndex) { PlayerIndex = playerIndex; }
    public void SwitchSource(IDriveInputSource next, bool requireRelease)
    {
        source = next;
        needsPedalRelease = requireRelease;
    }
    // Only the car/tutorial gate acknowledges constant-pedal resume. Status polling must not consume it.
    public void PedalReleaseObserved() => (source as IDrivePedalReleaseObserver)?.PedalReleaseObserved();
    public void UpdateInput(float deltaTime) { }
    public void Dispose() { } // InputManager owns the underlying device lifecycle.
}
