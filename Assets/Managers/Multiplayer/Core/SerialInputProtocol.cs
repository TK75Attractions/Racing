using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// Wire values remain independent; game-specific pedal mixing belongs in the adapter.
public readonly struct RgpPlayerInput
{
    public float Handle { get; }
    public float Accel { get; }
    public float Brake { get; }
    public RgpPlayerInput(float handle, float accel, float brake)
    { Handle = handle; Accel = accel; Brake = brake; }
}

public sealed class RgpFrame
{
    public string Type { get; }
    public uint Sequence { get; }
    public uint Time { get; }
    public string[] Data { get; }
    public RgpFrame(string type, uint sequence, uint time, string[] data)
    { Type = type; Sequence = sequence; Time = time; Data = data; }
    public RgpPlayerInput Player(int index)
    {
        int offset = index * 3;
        return new RgpPlayerInput(float.Parse(Data[offset], CultureInfo.InvariantCulture),
            float.Parse(Data[offset + 1], CultureInfo.InvariantCulture),
            float.Parse(Data[offset + 2], CultureInfo.InvariantCulture));
    }
}

public static class SerialInputProtocol
{
    private static readonly Regex Identifier = new Regex(@"\A[A-Z0-9_.-]+\z");
    private static readonly Regex Number = new Regex(@"\A-?[0-9]+(?:\.[0-9]+)?\z");
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
    public static ushort Crc(string body)
    {
        ushort crc = 0xFFFF;
        foreach (char c in body)
        {
            crc ^= (ushort)(c << 8);
            for (int bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }

    public static string Encode(string body) => "@" + body + "*" + Crc(body).ToString("X4") + "\n";
    public static bool UInt(string value, out uint result)
    {
        result = 0;
        return value.Length > 0 && value.Length <= 24 && AllDigits(value) &&
            uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }
    private static bool AllDigits(string value)
    {
        foreach (char c in value) if (c < '0' || c > '9') return false;
        return true;
    }
    public static bool Numeric(string value, decimal minimum, decimal maximum)
    {
        return value.Length <= 24 && Number.IsMatch(value) &&
            decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal parsed) && parsed >= minimum && parsed <= maximum;
    }
    private static bool Id(string value) => Identifier.IsMatch(value);
    private static bool Bool(string value) => value == "0" || value == "1";
    private static bool Origin(string value) => value == "AUTO" || UInt(value, out _);
    private static bool Target(string value) => value == "ALL" || value == "GYRO" || value == "MAG";
    private static bool Hex(string value, int maximum)
    {
        return Regex.IsMatch(value, @"\A0x[0-9A-F]{2}\z") &&
            int.Parse(value.Substring(2), NumberStyles.HexNumber) <= maximum;
    }
    private static bool Unreserved(byte b) => b >= 'A' && b <= 'Z' || b >= 'a' && b <= 'z' ||
        b >= '0' && b <= '9' || b == '-' || b == '.' || b == '_' || b == '~';
    public static bool TryDecodeText(string value, out string decoded)
    {
        decoded = null;
        var bytes = new List<byte>();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '%')
            {
                if (i + 2 >= value.Length || !Regex.IsMatch(value.Substring(i + 1, 2), @"\A[0-9A-F]{2}\z")) return false;
                byte b = byte.Parse(value.Substring(i + 1, 2), NumberStyles.HexNumber);
                if (Unreserved(b)) return false;
                bytes.Add(b); i += 2;
            }
            else
            {
                if (c > 127 || !Unreserved((byte)c)) return false;
                bytes.Add((byte)c);
            }
        }
        try { decoded = StrictUtf8.GetString(bytes.ToArray()); }
        catch (DecoderFallbackException) { return false; }
        foreach (char c in decoded)
            if (char.IsControl(c) && c != '\t' && c != '\r' && c != '\n') return false;
        return value.Length > 0;
    }

    // Input must include its terminator. No trimming: spaces and embedded CR are invalid.
    public static bool TryParse(string wire, out RgpFrame frame)
    {
        frame = null;
        if (string.IsNullOrEmpty(wire) || wire[0] != '@' || !wire.EndsWith("\n", StringComparison.Ordinal)) return false;
        string line = wire.Substring(0, wire.Length - 1);
        if (line.EndsWith("\r", StringComparison.Ordinal)) line = line.Substring(0, line.Length - 1);
        if (line.Length + 1 > 256) return false;
        foreach (char c in line) if (c < 33 || c > 126) return false;
        int star = line.IndexOf('*');
        if (star < 0 || star != line.LastIndexOf('*') || line.Length - star != 5) return false;
        string crc = line.Substring(star + 1);
        if (!Regex.IsMatch(crc, @"\A[0-9A-F]{4}\z")) return false;
        string body = line.Substring(1, star - 1);
        if (body.IndexOf('@') >= 0 || Crc(body) != ushort.Parse(crc, NumberStyles.HexNumber)) return false;
        string[] fields = body.Split(',');
        if (fields.Length < 3 || fields[0].Length == 0 || fields[0][0] < 'A' || fields[0][0] > 'Z' ||
            !Id(fields[0]) || !UInt(fields[1], out uint seq) || !UInt(fields[2], out uint time)) return false;
        foreach (string field in fields) if (field.Length == 0) return false;
        string[] data = new string[fields.Length - 3];
        Array.Copy(fields, 3, data, 0, data.Length);
        if (!Validate(fields[0], data)) return false;
        frame = new RgpFrame(fields[0], seq, time, data);
        return true;
    }

    private static bool Validate(string type, string[] d)
    {
        int n = d.Length;
        switch (type)
        {
            case "CMD": return n >= 1 && Id(d[0]);
            case "TEL":
            case "IMU":
                if (n < 6) return false;
                for (int i = 0; i < 6; i++)
                {
                    decimal limit = type == "TEL" ? (i % 3 == 0 ? 3600 : 1) : (i < 3 ? 64 : 4000);
                    if (!Numeric(d[i], type == "TEL" && i % 3 != 0 ? 0 : -limit, limit)) return false;
                }
                return true;
            case "SYS":
                if (n < 1 || !Id(d[0])) return false;
                switch (d[0])
                {
                    case "HELLO": return n >= 4 && d[1] == "RGP" && Regex.IsMatch(d[2], @"\A[0-9]+\.[0-9]+\z");
                    case "DEVICE": return n >= 3 && Id(d[1]) && Hex(d[2], 127) && (d[1] != "IMU" || n >= 4 && Hex(d[3], 255));
                    case "CONFIG": return n >= 3 && ConfigPair(d[1], d[2]);
                    case "I2C_DEVICE": return n >= 3 && UInt(d[1], out _) && Hex(d[2], 127);
                    default: return true;
                }
            case "EVT":
                if (n < 1 || !Id(d[0])) return false;
                switch (d[0])
                {
                    case "STATE": return n >= 2 && Id(d[1]);
                    case "CAL_START": case "CAL_DONE": return n >= 3 && Origin(d[1]) && Target(d[2]);
                    case "CAL_MAG_START": case "CAL_MAG_DONE": case "CAL_MAG_SKIPPED":
                    case "CAL_GYRO_START": case "CAL_GYRO_DONE": return n >= 2 && Origin(d[1]);
                    case "I2C_SCAN_START": return n >= 2 && UInt(d[1], out _);
                    case "I2C_SCAN_END": return n >= 3 && UInt(d[1], out _) && UInt(d[2], out uint count) && count <= 112;
                    case "CONFIG_CHANGED": return n >= 3 && ConfigPair(d[1], d[2]);
                    default: return true;
                }
            case "DBG": return n >= 2 && Id(d[0]) && TryDecodeText(d[1], out _);
            case "ERR": return ValidateError(d);
            case "ACK": return ValidateAck(d);
            // Host receives unknown types only after full framing and CRC validation.
            default: return true;
        }
    }

    private static bool ConfigPair(string name, string value)
    {
        if (!Id(name)) return false;
        switch (name)
        {
            case "TEL_RATE": return UInt(value, out uint tel) && tel >= 1 && tel <= 100;
            case "IMU_RATE": return UInt(value, out uint imu) && imu >= 1 && imu <= 50;
            case "TEL_STREAM": case "IMU_STREAM": case "DBG_STREAM": return Bool(value);
            case "BETA": return Numeric(value, 0.010m, 1);
            case "INPUT_MODE": return value == "ALL" || value == "P1_STEERING";
            default: return true;
        }
    }
    private static bool ValidateAck(string[] d)
    {
        int n = d.Length;
        if (n < 3 || !UInt(d[0], out _) || !Id(d[1]) || !Id(d[2])) return false;
        switch (d[2])
        {
            case "INVALID_ARG": return n >= 4 && UInt(d[3], out uint index) && index >= 1;
            case "BUSY": return n >= 5 && Id(d[3]) && Origin(d[4]);
            case "NOT_READY": case "FAILED": return n >= 4 && Id(d[3]);
            case "ACCEPTED": return d[1] != "CALIBRATE" || n >= 4 && Target(d[3]);
            case "OK":
                switch (d[1])
                {
                    case "OPEN_SESSION": return n >= 4 && Regex.IsMatch(d[3], @"\A[0-9A-F]{16}\z");
                    case "PING": return n >= 4 && d[3] == "PONG";
                    case "GET_STATUS": return n >= 8 && Id(d[3]) && Bool(d[4]) && Bool(d[5]) && Bool(d[6]) && Bool(d[7]);
                    case "GET_CONFIG":
                        string[] names = { "TEL_RATE", "IMU_RATE", "TEL_STREAM", "IMU_STREAM", "DBG_STREAM", "BETA" };
                        if (n < 15 || (n - 3) % 2 != 0) return false;
                        for (int i = 3; i < n; i += 2)
                            if (!ConfigPair(d[i], d[i + 1]) || i < 15 && d[i] != names[(i - 3) / 2]) return false;
                        return true;
                    case "SET_STREAM": return n >= 5 && (d[3] == "TEL" || d[3] == "IMU" || d[3] == "DBG") && Bool(d[4]);
                    case "SET_RATE": return n >= 5 && (d[3] == "TEL" || d[3] == "IMU") && ConfigPair(d[3] + "_RATE", d[4]);
                    case "SET_BETA": return n >= 4 && Numeric(d[3], 0.010m, 1);
                    case "SET_INPUT_MODE": return n >= 4 && ConfigPair("INPUT_MODE", d[3]);
                    default: return true;
                }
            default: return true; // Unknown status completes its pending request as a failure.
        }
    }
    private static bool ValidateError(string[] d)
    {
        int n = d.Length;
        if (n < 3 || (d[0] != "WARN" && d[0] != "ERROR" && d[0] != "FATAL") || !Id(d[1]) || !Id(d[2])) return false;
        int end = n;
        for (int i = 3; i < n; i++)
        {
            if (d[i] == "TEXT")
            {
                if (i != n - 2 || !TryDecodeText(d[i + 1], out _)) return false;
                end = Math.Min(end, i); break;
            }
            if (d[i] == "REQ")
            {
                if (i + 1 >= n || !UInt(d[i + 1], out _)) return false;
                end = Math.Min(end, i); i++;
            }
        }
        int count = end - 3;
        switch (d[2])
        {
            case "WHOAMI_MISMATCH": return count >= 3 && Hex(d[3], 127) && Hex(d[4], 255) && Hex(d[5], 255);
            case "NO_RESPONSE": case "WHOAMI_FAILED": return count >= 1 && Hex(d[3], 127);
            case "CONNECTION_LOST": return count == 0 || Hex(d[3], 127);
            case "INVALID_STATE": return count >= 2 && Id(d[3]) && Id(d[4]);
            case "TIMEOUT": return count >= 2 && Id(d[3]) && UInt(d[4], out _);
            case "INVALID_COMMAND": return count >= 1 && Id(d[3]);
            case "INVALID_ARGUMENT": return count >= 2 && Id(d[3]) && UInt(d[4], out uint index) && index >= 1;
            case "SEQ_REUSE": return count >= 1 && UInt(d[3], out _);
            case "INIT_FAILED": case "BUS_ERROR": case "UNKNOWN_ERROR": case "REINIT_FAILED":
                return count == 0 || d[3].Length <= 24 && Regex.IsMatch(d[3], @"\A-?[0-9]+\z");
            default: return true;
        }
    }
}

// Byte-oriented, bounded receiver. A new '@' resynchronizes even inside a broken frame.
public sealed class RgpStreamDecoder
{
    private readonly StringBuilder buffer = new StringBuilder(257);
    private double lastByteTime;
    public void Expire(double now) { if (buffer.Length > 0 && now - lastByteTime >= 1000) buffer.Clear(); }
    public string Push(byte value, double now)
    {
        Expire(now);
        if (value == '@') buffer.Clear();
        else if (buffer.Length == 0) return null;
        lastByteTime = now;
        buffer.Append((char)value);
        if (value == '\n')
        {
            string wire = buffer.ToString(); buffer.Clear(); return wire;
        }
        // Up to 255 bytes before LF, or 256 when the last byte is the optional CR.
        if (buffer.Length > 256 || buffer.Length == 256 && value != '\r') buffer.Clear();
        return null;
    }
}
