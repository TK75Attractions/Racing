using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    [SerializeField] private bool isDebugMode = true;

    public float pedalInput = 0f;
    public float handleRotationInput = 0f;

    //キーが押されている時に true になる。
    public bool UpPressed = false;
    public bool DownPressed = false;
    public bool LeftPressed = false;
    public bool RightPressed = false;

    //キーが押された瞬間に true になる。
    public bool UpGetDown = false;
    public bool DownGetDown = false;
    public bool LeftGetDown = false;
    public bool RightGetDown = false;

    //キーが離された瞬間に true になる。
    public bool UpGetUp = false;
    public bool DownGetUp = false;
    public bool LeftGetUp = false;
    public bool RightGetUp = false;


    public void UpdateInput()
    {
        DetectKeyboardInput();
    }

    private void DetectKeyboardInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (isDebugMode)
        {
            UpPressed = keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed;
            DownPressed = keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed;
            LeftPressed = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
            RightPressed = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;

            UpGetDown = keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame;
            DownGetDown = keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame;
            LeftGetDown = keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame;
            RightGetDown = keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame;

            UpGetUp = keyboard.wKey.wasReleasedThisFrame || keyboard.upArrowKey.wasReleasedThisFrame;
            DownGetUp = keyboard.sKey.wasReleasedThisFrame || keyboard.downArrowKey.wasReleasedThisFrame;
            LeftGetUp = keyboard.aKey.wasReleasedThisFrame || keyboard.leftArrowKey.wasReleasedThisFrame;
            RightGetUp = keyboard.dKey.wasReleasedThisFrame || keyboard.rightArrowKey.wasReleasedThisFrame;
        }
    }
}
