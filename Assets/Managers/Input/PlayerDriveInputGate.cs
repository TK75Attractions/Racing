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

    public DriveInputState CurrentState
    {
        get
        {
            if (IsBlocked || source == null) return DriveInputState.Neutral;

            DriveInputState state = source.CurrentState;
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

    public bool ToggleDirection()
    {
        IsReverse = !IsReverse;
        return IsReverse;
    }

    public void Reset()
    {
        IsBlocked = false;
        IsReverse = false;
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
