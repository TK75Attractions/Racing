using System;
using UnityEngine;

[Serializable]
public sealed class SerialControllerConfiguration
{
    [SerializeField, Range(0, 1)] private int playerIndex;
    [SerializeField] private string deviceId = "P1";
    [SerializeField] private string portName = string.Empty;
    [SerializeField, Min(1)] private int baudRate = 115200;
    [SerializeField, Min(0.0001f)] private float steeringDivisor = 3f;
    [SerializeField, Min(50)] private int readTimeoutMilliseconds = 200;
    [SerializeField, Min(100)] private int discoveryTimeoutMilliseconds = 1500;
    [SerializeField, Min(0.1f)] private float inputTimeoutSeconds = 2f;

    public int PlayerIndex => playerIndex;
    public string DeviceId => deviceId;
    public string PortName => portName;
    public int BaudRate => baudRate;
    public float SteeringDivisor => steeringDivisor;
    public int ReadTimeoutMilliseconds => readTimeoutMilliseconds;
    public int DiscoveryTimeoutMilliseconds => discoveryTimeoutMilliseconds;
    public float InputTimeoutSeconds => inputTimeoutSeconds;

    public SerialControllerConfiguration(int playerIndex, string deviceId)
    {
        this.playerIndex = playerIndex;
        this.deviceId = deviceId;
    }

    public void AssignPlayerIndex(int value)
    {
        playerIndex = Mathf.Clamp(value, 0, 1);
    }
}
