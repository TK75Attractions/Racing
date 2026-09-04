using System;

[Serializable]
public struct DriveInputState
{
    public float pedal;
    public float steering;
    public bool resetPressed;
    public bool readyPressed;

    public static DriveInputState Neutral => new DriveInputState();
}

public interface IDriveInputSource : IDisposable
{
    int PlayerIndex { get; }
    string DeviceId { get; }
    bool IsConnected { get; }
    DriveInputState CurrentState { get; }

    void UpdateInput(float deltaTime);
}
