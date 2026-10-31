using System;

/// <summary>Temporary dashboard input follows the same car/tutorial gates as a local keyboard.</summary>
public sealed class DashboardDriveInputSource : IDriveInputSource, IDriveSteeringCalibration, IDriveInputStatusSource, IDrivePedalReleaseObserver
{
    private readonly IDriveInputSource source;
    private readonly DashboardInputLease lease;
    private readonly Func<double> clock;
    private DriveInputState remoteState;
    private bool wasRemote, needsLocalRelease, localReleasedDuringRemote;
    private long sampledGeneration;
    public int PlayerIndex => source.PlayerIndex;
    public string DeviceId => lease.IsActive(clock()) ? $"Dashboard P{PlayerIndex + 1}" : source.DeviceId;
    public float FullSteeringInput => wasRemote || lease.IsActive(clock()) ? DriveSteeringCalibration.FullLockUnits : DriveSteeringCalibration.Range(source);
    public bool IsConnected => lease.IsActive(clock()) || source.IsConnected;
    public DriveConnectionState ConnectionState => lease.IsActive(clock()) ? DriveConnectionState.Ready : DriveInputStatus.Of(source);
    public DriveInputState CurrentState
    {
        get
        {
            if (wasRemote) return lease.IsActive(clock()) && sampledGeneration == lease.Generation ? remoteState : DriveInputState.Neutral;
            if (lease.IsActive(clock())) return DriveInputState.Neutral; // Await the next sampled input frame.
            DriveInputState local = source.CurrentState;
            if (!needsLocalRelease) return local;
            return DriveInputState.Neutral;
        }
    }
    public DashboardDriveInputSource(IDriveInputSource source, DashboardInputLease lease, Func<double> clock)
    { this.source = source; this.lease = lease; this.clock = clock; }
    public void UpdateInput(float deltaTime)
    {
        double now = clock();
        bool active = lease.IsActive(now);
        bool entering = active && (!wasRemote || sampledGeneration != lease.Generation);
        if (entering) localReleasedDuringRemote = false;
        bool localReleased = source.IsConnected && PedalRelease.IsReleased(source.CurrentState.pedal);
        // Observe the real device even while menus/cars are not consuming input. A release
        // made while the operator owns the controls must also count toward the handover.
        if (active && localReleased) localReleasedDuringRemote = true;
        if (wasRemote && !active) needsLocalRelease = !localReleasedDuringRemote;
        if (!active && localReleased) needsLocalRelease = false;
        wasRemote = active; sampledGeneration = lease.Generation;
        // Publish a neutral frame before the first button edge so existing release gates can arm.
        if (entering) { remoteState = DriveInputState.Neutral; return; }
        DashboardInputButtons buttons = lease.Sample(now);
        remoteState = new DriveInputState {
            pedal = ((buttons & DashboardInputButtons.Accelerate) != 0 ? 1f : 0f) - ((buttons & DashboardInputButtons.Brake) != 0 ? 1f : 0f),
            steering = ((buttons & DashboardInputButtons.Right) != 0 ? 10f : 0f) - ((buttons & DashboardInputButtons.Left) != 0 ? 10f : 0f),
            resetPressed = (buttons & DashboardInputButtons.Reset) != 0, readyPressed = (buttons & DashboardInputButtons.Ready) != 0
        };
    }
    public void PedalReleaseObserved()
    {
        // A suppressed held pedal is synthetic neutral, not a physical release.
        if (!wasRemote && source.IsConnected && PedalRelease.IsReleased(source.CurrentState.pedal))
            (source as IDrivePedalReleaseObserver)?.PedalReleaseObserved();
    }
    public void Dispose() { } // InputManager owns the real devices.
}
