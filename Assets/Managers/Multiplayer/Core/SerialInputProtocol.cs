using System;
using System.Globalization;

public readonly struct SerialInputFrame
{
    public float Pedal { get; }
    public float Steering { get; }
    public bool ResetHeld { get; }
    public bool ReadyHeld { get; }

    public SerialInputFrame(float pedal, float steering, bool resetHeld, bool readyHeld)
    {
        Pedal = pedal;
        Steering = steering;
        ResetHeld = resetHeld;
        ReadyHeld = readyHeld;
    }
}

public static class SerialInputProtocol
{
    private const string DevicePrefix = "DEVICE";

    public static bool TryReadDeviceId(string line, out string deviceId)
    {
        deviceId = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string[] parts = line.Trim().Split(new[] { ',', ':' }, 2);
        if (parts.Length != 2 ||
            !string.Equals(parts[0].Trim(), DevicePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        deviceId = parts[1].Trim();
        return !string.IsNullOrEmpty(deviceId);
    }

    public static bool TryParseInput(string line, float steeringDivisor, out SerialInputFrame frame)
    {
        bool parsed = TryParsePartialInput(line, steeringDivisor, out frame,
            out bool pedalParsed, out bool steeringParsed);
        return parsed && pedalParsed && steeringParsed;
    }

    // Legacy controllers can send one valid axis while the other is unavailable.
    public static bool TryParsePartialInput(string line, float steeringDivisor,
        out SerialInputFrame frame, out bool pedalParsed, out bool steeringParsed)
    {
        frame = default;
        pedalParsed = false;
        steeringParsed = false;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string payload = line.Split(new[] { "||" }, StringSplitOptions.None)[0].Trim();
        string[] parts = payload.Split(',');
        if (parts.Length < 2)
        {
            return false;
        }

        pedalParsed = TryParseFloat(parts[0], out float pedal);
        steeringParsed = TryParseFloat(parts[1], out float steering);
        if (!pedalParsed && !steeringParsed) return false;

        float divisor = Math.Abs(steeringDivisor) < 0.0001f ? 1f : steeringDivisor;
        bool resetHeld = parts.Length > 2 && TryParseButton(parts[2]);
        bool readyHeld = parts.Length > 3 && TryParseButton(parts[3]);
        frame = new SerialInputFrame(
            Clamp(pedal, -1f, 1f),
            steering / divisor,
            resetHeld,
            readyHeld);
        return true;
    }

    private static bool TryParseFloat(string value, out float result)
    {
        return (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                || float.TryParse(value.Trim(), out result))
            && !float.IsNaN(result) && !float.IsInfinity(result);
    }

    private static bool TryParseButton(string value)
    {
        string normalized = value.Trim();
        return normalized == "1" ||
               normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static float Clamp(float value, float minimum, float maximum)
    {
        return Math.Min(maximum, Math.Max(minimum, value));
    }
}
