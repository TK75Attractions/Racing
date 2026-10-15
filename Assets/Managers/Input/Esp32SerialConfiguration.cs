using System;
using UnityEngine;

// Settings for the single ESP32 that sends both players on one USB port.
[Serializable]
public sealed class Esp32SerialConfiguration
{
    [Tooltip("Leave empty to select the connected ESP32 USB port automatically.")]
    [SerializeField] private string portName = string.Empty;
    [SerializeField, Min(1)] private int baudRate = 115200;
    [SerializeField, Min(0.0001f)] private float steeringDivisor = 15f;
    [SerializeField, Min(50)] private int readTimeoutMilliseconds = 200;
    [SerializeField, Min(0.1f)] private float inputTimeoutSeconds = 2f;

    public string PortName => portName;
    public int BaudRate => baudRate;
    public float SteeringDivisor => steeringDivisor;
    public int ReadTimeoutMilliseconds => readTimeoutMilliseconds;
    public float InputTimeoutSeconds => inputTimeoutSeconds;
    public bool UsePseudoTerminal { get; private set; }

    public Esp32SerialConfiguration WithSimulatorPort(string path, float divisor)
    {
        if (!MacPseudoTerminalLineReader.IsSupportedPath(path))
            throw new ArgumentException("Expected a macOS PTY path.", nameof(path));
        if (float.IsNaN(divisor) || float.IsInfinity(divisor) || divisor < .0001f || divisor > 1000f)
            throw new ArgumentOutOfRangeException(nameof(divisor));
        return new Esp32SerialConfiguration
        {
            portName = path,
            baudRate = baudRate,
            steeringDivisor = divisor,
            readTimeoutMilliseconds = readTimeoutMilliseconds,
            inputTimeoutSeconds = inputTimeoutSeconds,
            UsePseudoTerminal = true
        };
    }
}
