/// <summary>
/// 車両へ渡す入力をプレイヤー単位で停止し、ペダルの前進・後退方向を切り替えます。
/// 元の入力ソースの更新と破棄は InputManager が所有します。
/// </summary>
public sealed class PlayerDriveInputGate : IDriveInputSource
{
    private readonly IDriveInputSource source;

    public int PlayerIndex => source?.PlayerIndex ?? -1;
    public string DeviceId => source?.DeviceId ?? string.Empty;
    public bool IsConnected => source != null && source.IsConnected;
    public bool IsBlocked { get; private set; }
    public bool IsReverse { get; private set; }
    public bool NeedsPedalRelease { get; private set; }
    // Used only to retain a reset-button edge while the gate temporarily emits neutral input.
    public bool ResetButtonHeld => source != null && source.IsConnected && source.CurrentState.resetPressed;

    public DriveInputState CurrentState
    {
        get
        {
            if (source == null) return DriveInputState.Neutral;
            if (!source.IsConnected) { NeedsPedalRelease = true; return DriveInputState.Neutral; }
            if (IsBlocked) return DriveInputState.Neutral;

            DriveInputState state = source.CurrentState;
            if (NeedsPedalRelease)
            {
                if (System.Math.Abs(state.pedal) <= .05f) NeedsPedalRelease = false;
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

    public void SetBlocked(bool blocked)
    {
        IsBlocked = blocked;
    }
    public void RequirePedalRelease() => NeedsPedalRelease = true;

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
