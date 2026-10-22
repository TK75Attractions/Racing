using System;

/// <summary>Temporary dashboard input follows the same car/tutorial gates as a local keyboard.</summary>
public sealed class DashboardDriveInputSource : IDriveInputSource, IDriveInputStatusSource, IDrivePedalReleaseObserver
{
    private readonly IDriveInputSource source;
    private readonly DashboardInputLease lease;
    private readonly Func<double> clock;
    private DriveInputState remoteState;
    private bool wasRemote, needsLocalRelease;
    private long sampledGeneration;
    public int PlayerIndex => source.PlayerIndex;
    public string DeviceId => lease.IsActive(clock()) ? $"Dashboard P{PlayerIndex + 1}" : source.DeviceId;
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
            if (source.IsConnected && Math.Abs(local.pedal) <= .05f) needsLocalRelease = false;
            return DriveInputState.Neutral;
        }
    }
    public DashboardDriveInputSource(IDriveInputSource source, DashboardInputLease lease, Func<double> clock)
    { this.source = source; this.lease = lease; this.clock = clock; }
    public void UpdateInput(float deltaTime)
    {
        double now = clock();
        bool active = lease.IsActive(now);
        if (wasRemote && !active) needsLocalRelease = true;
        bool entering = active && (!wasRemote || sampledGeneration != lease.Generation);
        wasRemote = active; sampledGeneration = lease.Generation;
        // Publish a neutral frame before the first button edge so existing release gates can arm.
        if (entering) { remoteState = DriveInputState.Neutral; return; }
        DashboardInputButtons buttons = lease.Sample(now);
        remoteState = new DriveInputState {
            pedal = (buttons & DashboardInputButtons.Accelerate) != 0 ? 1f : (buttons & DashboardInputButtons.Brake) != 0 ? -1f : 0f,
            steering = (buttons & DashboardInputButtons.Right) != 0 ? 10f : (buttons & DashboardInputButtons.Left) != 0 ? -10f : 0f,
            resetPressed = (buttons & DashboardInputButtons.Reset) != 0, readyPressed = (buttons & DashboardInputButtons.Ready) != 0
        };
    }
    public void PedalReleaseObserved() { if (!wasRemote) (source as IDrivePedalReleaseObserver)?.PedalReleaseObserved(); }
    public void Dispose() { } // InputManager owns the real devices.
}
