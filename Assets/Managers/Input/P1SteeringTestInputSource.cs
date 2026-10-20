using UnityEngine;
using UnityEngine.InputSystem;

public enum P1SteeringTestMode { Normal, KeyboardPedal, ConstantPedal }

/// <summary>Stable P1 adapter: switch pedal sources without replacing a car/tutorial's input gate.</summary>
public sealed class P1SteeringTestInputSource : IDriveInputSource
{
    private readonly IDriveInputSource normal, keyboard;
    public IDriveInputSource Serial { get; set; }
    public P1SteeringTestMode Mode { get; set; }
    public float ConstantPedal { get; set; } = 1f;
    public bool IsRacing { get; set; }
    public int PlayerIndex => 0;
    public string DeviceId => Mode == P1SteeringTestMode.Normal ? normal.DeviceId :
        Mode == P1SteeringTestMode.KeyboardPedal ? "P1 ESP32 wheel + W/S pedal" : "P1 ESP32 wheel + constant pedal";
    public bool IsConnected => Mode == P1SteeringTestMode.Normal ? normal.IsConnected :
        Serial != null && Serial.IsConnected && keyboard.IsConnected;
    public DriveInputState CurrentState
    {
        get
        {
            if (Mode == P1SteeringTestMode.Normal) return normal.CurrentState;
            if (!IsConnected) return DriveInputState.Neutral;
            DriveInputState keys = keyboard.CurrentState;
            // The ESP32 pedal is deliberately never consulted in either steering-test mode.
            keys.steering = Serial.CurrentState.steering;
            if (Mode == P1SteeringTestMode.ConstantPedal && IsRacing)
                keys.pedal = keys.pedal < 0f || Keyboard.current?.sKey.isPressed == true ? 0f :
                    float.IsNaN(ConstantPedal) || float.IsInfinity(ConstantPedal) ? 0f : Mathf.Clamp01(ConstantPedal);
            return keys;
        }
    }
    public P1SteeringTestInputSource(IDriveInputSource normal, IDriveInputSource keyboard)
    {
        this.normal = normal; this.keyboard = keyboard;
    }
    public void UpdateInput(float dt)
    {
        normal.UpdateInput(dt);
        if (keyboard != normal) keyboard.UpdateInput(dt);
    }
    public void Dispose()
    {
        normal.Dispose();
        if (keyboard != normal) keyboard.Dispose();
        Serial = null;
    }
}
