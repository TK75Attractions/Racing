using System;
using UnityEngine;

// Settings for the single ESP32 that sends both players on one USB port.
[Serializable]
public sealed class Esp32SerialConfiguration
{
    [Tooltip("Leave empty to select the connected ESP32 USB port automatically.")]
    [SerializeField] private string portName = string.Empty;
    [Tooltip("Handle degrees per game steering unit. RGP wire angles remain in degrees.")]
    [SerializeField, Min(0.0001f)] private float steeringDivisor = 15f;
    [SerializeField, Min(50)] private int readTimeoutMilliseconds = 200;

    public string PortName => portName;
    public int BaudRate => 115200;
    public float SteeringDivisor => float.IsNaN(steeringDivisor) || float.IsInfinity(steeringDivisor) ? 15f : Mathf.Max(0.0001f, steeringDivisor);
    public int ReadTimeoutMilliseconds => readTimeoutMilliseconds;
    public bool UsePseudoTerminal { get; private set; }

    public Esp32SerialConfiguration WithSimulatorPort(string path, float divisor)
    {
        if (!MacPseudoTerminalLineReader.IsSupportedPath(path)) throw new ArgumentException("Expected a macOS PTY path.", nameof(path));
        if (float.IsNaN(divisor) || float.IsInfinity(divisor) || divisor < .0001f || divisor > 1000f)
            throw new ArgumentOutOfRangeException(nameof(divisor));
        return new Esp32SerialConfiguration { portName = path, steeringDivisor = divisor,
            readTimeoutMilliseconds = readTimeoutMilliseconds, UsePseudoTerminal = true };
    }
}
