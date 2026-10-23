/// <summary>
/// 車両へ渡す入力をプレイヤー単位で停止し、ペダルの前進・後退方向を切り替えます。
/// 元の入力ソースの更新と破棄は InputManager が所有します。
/// </summary>
public sealed class PlayerDriveInputGate : IDriveInputSource, IDriveInputStatusSource
{
    // Only transitions are published; inspecting diagnostics never consumes player input.
    public static event System.Action<PlayerDriveInputGate, string> ReleaseChanged;
    private IDriveInputSource source;
    public IDriveInputSource Source => source;

    public int PlayerIndex => source?.PlayerIndex ?? -1;
    public string DeviceId => source?.DeviceId ?? string.Empty;
    public bool IsConnected => source != null && source.IsConnected;
    public DriveConnectionState ConnectionState => DriveInputStatus.Of(source);
    public bool IsBlocked { get; private set; }
    public bool IsReverse { get; private set; }
    public bool NeedsPedalRelease { get; private set; }
    public string PedalReleaseReason { get; private set; } = "";
    // Used only to retain a reset-button edge while the gate temporarily emits neutral input.
    public bool ResetButtonHeld => source != null && source.IsConnected && source.CurrentState.resetPressed;

    public DriveInputState CurrentState
    {
        get
        {
            if (source == null) return DriveInputState.Neutral;
            if (!source.IsConnected)
            {
                if (!NeedsPedalRelease) RequirePedalRelease("connection_lost");
                return DriveInputState.Neutral;
            }
            if (IsBlocked) return DriveInputState.Neutral;

            DriveInputState state = source.CurrentState;
            bool released = System.Math.Abs(state.pedal) <= .05f;
            if (released) (source as IDrivePedalReleaseObserver)?.PedalReleaseObserved();
            if (NeedsPedalRelease)
            {
                if (released)
                {
                    NeedsPedalRelease = false;
                    ReleaseChanged?.Invoke(this, "RELEASE_OBSERVED");
                }
                return DriveInputState.Neutral;
            }
            if (IsReverse) state.pedal = -state.pedal;
            return state;
        }
    }

    public PlayerDriveInputGate(IDriveInputSource source)
    {
        this.source = source;
    }

    public void SwitchSource(IDriveInputSource next)
    {
        source = next; IsReverse = false; RequirePedalRelease("source_changed");
    }

    public void SetBlocked(bool blocked)
    {
        IsBlocked = blocked;
    }
    public void RequirePedalRelease(string reason = "requested")
    {
        bool changed = !NeedsPedalRelease || PedalReleaseReason != reason;
        NeedsPedalRelease = true;
        PedalReleaseReason = reason;
        if (changed) ReleaseChanged?.Invoke(this, "RELEASE_REQUIRED");
    }

    public bool ToggleDirection()
    {
        IsReverse = !IsReverse;
        return IsReverse;
    }

    public void Reset()
    {
        IsBlocked = false;
        IsReverse = false;
        NeedsPedalRelease = false;
        PedalReleaseReason = "";
    }

    public void UpdateInput(float deltaTime)
    {
        // InputManager が共有元を一度だけ更新します。
    }

    public void Dispose()
    {
        // 元の入力ソースは InputManager が破棄します。
    }
}
