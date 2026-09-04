using UnityEngine.InputSystem;

public sealed class KeyboardDriveInputSource : IDriveInputSource
{
    private const float KeyboardSteeringValue = 10f;

    public int PlayerIndex { get; }
    public string DeviceId => $"KEYBOARD_P{PlayerIndex + 1}";
    public bool IsConnected => Keyboard.current != null;
    public DriveInputState CurrentState { get; private set; }

    public KeyboardDriveInputSource(int playerIndex)
    {
        PlayerIndex = playerIndex;
    }

    public void UpdateInput(float deltaTime)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            CurrentState = DriveInputState.Neutral;
            return;
        }

        bool usePlayerOneLayout = PlayerIndex == 0;
        bool accelerate = usePlayerOneLayout
            ? keyboard.wKey.isPressed
            : keyboard.upArrowKey.isPressed;
        bool brake = usePlayerOneLayout
            ? keyboard.sKey.isPressed
            : keyboard.downArrowKey.isPressed;
        bool steerLeft = usePlayerOneLayout
            ? keyboard.aKey.isPressed
            : keyboard.leftArrowKey.isPressed;
        bool steerRight = usePlayerOneLayout
            ? keyboard.dKey.isPressed
            : keyboard.rightArrowKey.isPressed;

        CurrentState = new DriveInputState
        {
            pedal = accelerate ? 1f : brake ? -1f : 0f,
            steering = steerRight
                ? KeyboardSteeringValue
                : steerLeft ? -KeyboardSteeringValue : 0f,
            resetPressed = usePlayerOneLayout
                ? keyboard.spaceKey.wasPressedThisFrame
                : keyboard.rightCtrlKey.wasPressedThisFrame,
            readyPressed = usePlayerOneLayout
                ? keyboard.enterKey.wasPressedThisFrame
                : keyboard.rightShiftKey.wasPressedThisFrame
        };
    }

    public void Dispose()
    {
        CurrentState = DriveInputState.Neutral;
    }
}
