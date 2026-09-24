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

public readonly struct TwoPlayerSerialInputFrame
{
    public SerialInputFrame PlayerOne { get; }
    public SerialInputFrame PlayerTwo { get; }
    public bool PlayerOnePedalValid { get; }
    public bool PlayerOneSteeringValid { get; }
    public bool PlayerTwoPedalValid { get; }
    public bool PlayerTwoSteeringValid { get; }
    public bool PlayerOneValid => PlayerOnePedalValid || PlayerOneSteeringValid;
    public bool PlayerTwoValid => PlayerTwoPedalValid || PlayerTwoSteeringValid;

    public TwoPlayerSerialInputFrame(
        SerialInputFrame playerOne,
        SerialInputFrame playerTwo,
        bool playerOnePedalValid,
        bool playerOneSteeringValid,
        bool playerTwoPedalValid,
        bool playerTwoSteeringValid)
    {
        PlayerOne = playerOne;
        PlayerTwo = playerTwo;
        PlayerOnePedalValid = playerOnePedalValid;
        PlayerOneSteeringValid = playerOneSteeringValid;
        PlayerTwoPedalValid = playerTwoPedalValid;
        PlayerTwoSteeringValid = playerTwoSteeringValid;
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

    // A shared controller sends: P1 pedal, P1 steering, P2 pedal, P2 steering.
    // "nan" marks only that axis as unavailable; the other axis can still update.
    public static bool TryParseTwoPlayerInput(
        string line,
        float steeringDivisor,
        out TwoPlayerSerialInputFrame frame)
    {
        frame = default;
        if (string.IsNullOrWhiteSpace(line)) return false;

        string payload = line.Split(new[] { "||" }, StringSplitOptions.None)[0].Trim();
        string[] parts = payload.Split(',');
        if (parts.Length != 4) return false;

        bool p1PedalParsed = TryParseFloatOrNan(parts[0], out float p1Pedal, out bool p1PedalNan);
        bool p1SteeringParsed = TryParseFloatOrNan(parts[1], out float p1Steering, out bool p1SteeringNan);
        bool p2PedalParsed = TryParseFloatOrNan(parts[2], out float p2Pedal, out bool p2PedalNan);
        bool p2SteeringParsed = TryParseFloatOrNan(parts[3], out float p2Steering, out bool p2SteeringNan);
        if (!p1PedalParsed || !p1SteeringParsed || !p2PedalParsed || !p2SteeringParsed)
        {
            return false;
        }

        float divisor = Math.Abs(steeringDivisor) < 0.0001f ? 1f : steeringDivisor;
        frame = new TwoPlayerSerialInputFrame(
            new SerialInputFrame(Clamp(p1Pedal, -1f, 1f), p1Steering / divisor, false, false),
            new SerialInputFrame(Clamp(p2Pedal, -1f, 1f), p2Steering / divisor, false, false),
            !p1PedalNan,
            !p1SteeringNan,
            !p2PedalNan,
            !p2SteeringNan);
        return true;
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

    private static bool TryParseFloatOrNan(string value, out float result, out bool isNan)
    {
        isNan = string.Equals(value.Trim(), "nan", StringComparison.OrdinalIgnoreCase);
        if (isNan)
        {
            result = 0f;
            return true;
        }

        return TryParseFloat(value, out result);
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
