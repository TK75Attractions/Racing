using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;

// Pure protocol/session state; all methods are called on the game's main thread.
public sealed class RgpHostSession
{
    private sealed class PendingCommand
    {
        public string Command;
        public string Wire;
        public double SentAt;
        public int Retries;
        public bool RequiredForSetup;
    }
    private readonly Action<string> send;
    private readonly Dictionary<uint, PendingCommand> pending = new Dictionary<uint, PendingCommand>();
    private uint nextSequence;
    private uint lastTelSequence;
    private bool hasTelSequence;
    private bool hasTelemetry;
    private double lastTelemetryAt;
    private string sessionId;
    private bool automaticCalibrationAttempted, automaticAngleZeroAttempted;
    public event Action<string> CommandTimedOut;
    public bool Compatible { get; private set; } = true;
    public bool SessionOpen { get; private set; }
    public string State { get; private set; } = "UNKNOWN";
    public string LastCommandResult { get; private set; } = "Waiting for session";
    public int TelemetryRate { get; private set; } = 100;
    public bool TelemetryEnabled { get; private set; } = true;
    public RgpPlayerInput PlayerOne { get; private set; }
    public RgpPlayerInput PlayerTwo { get; private set; }
    public string AdsStatus { get; private set; } = "Unknown";
    public string ImuStatus { get; private set; } = "Unknown";
    public string AdsAddress { get; private set; } = "";
    public string ImuAddress { get; private set; } = "";
    public string ImuIdentity { get; private set; } = "";
    public bool AngleLocked { get; private set; }
    public bool Calibrated { get; private set; }
    public int PendingCommands => pending.Count;

    // All operator commands share the setup session's SEQ, ACK matching and bounded retry policy.
    public bool TryCommand(string command, string[] args, double now, out uint sequence, out string reason)
    {
        sequence = 0; reason = "invalid_arguments";
        args = args ?? Array.Empty<string>();
        bool valid;
        switch (command)
        {
            case "PING": case "GET_STATUS": case "GET_CONFIG": case "ANGLE_ZERO": case "SCAN_I2C": valid = args.Length == 0; break;
            case "CALIBRATE": valid = args.Length == 1 && (args[0] == "GYRO" || args[0] == "MAG" || args[0] == "ALL"); break;
            case "SET_BETA": valid = args.Length == 1 && args[0] != null && SerialInputProtocol.Numeric(args[0], .010m, 1m); break;
            case "SET_STREAM": valid = args.Length == 2 && (args[0] == "TEL" || args[0] == "IMU" || args[0] == "DBG") && (args[1] == "0" || args[1] == "1"); break;
            case "SET_RATE": valid = args.Length == 2 && (args[0] == "TEL" || args[0] == "IMU") && args[1] != null &&
                SerialInputProtocol.UInt(args[1], out uint hz) && hz >= 1 && hz <= (args[0] == "TEL" ? 100 : 50); break;
            default: return false;
        }
        if (!valid) return false;
        if (!Compatible || !SessionOpen) { reason = "session_not_ready"; return false; }
        if (pending.Count >= 8) { reason = "commands_pending"; return false; }
        foreach (PendingCommand active in pending.Values)
            if (active.Command == command) { reason = "command_pending"; return false; }
        if ((command == "ANGLE_ZERO" || command == "CALIBRATE" || command == "SET_BETA") && State != "READY" && State != "WAIT_ANGLE")
        { reason = "device_not_ready"; return false; }
        if (command == "CALIBRATE") automaticCalibrationAttempted = true;
        if (command == "ANGLE_ZERO") automaticAngleZeroAttempted = true;
        sequence = nextSequence;
        SendCommand(command, now, false, args); reason = null; return true;
    }

    private void ResetHardware()
    {
        AdsStatus = ImuStatus = "Unknown"; AdsAddress = ImuAddress = ImuIdentity = "";
        AngleLocked = Calibrated = false;
    }
    public double StaleAfterMilliseconds => Math.Max(3000.0 / TelemetryRate, 100);
    public bool IsFresh(double now) => Compatible && SessionOpen && State == "READY" && TelemetryEnabled &&
        hasTelemetry && now - lastTelemetryAt <= StaleAfterMilliseconds;

    public RgpHostSession(Action<string> send) { this.send = send ?? throw new ArgumentNullException(nameof(send)); }
    public void Start(double now)
    {
        pending.Clear(); nextSequence = 0; hasTelSequence = false;
        automaticCalibrationAttempted = automaticAngleZeroAttempted = false;
        SessionOpen = false; State = "UNKNOWN"; Compatible = true; ResetHardware();
        TelemetryRate = 100; TelemetryEnabled = true; Invalidate();
        byte[] random = new byte[8];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
        sessionId = BitConverter.ToString(random).Replace("-", "");
        Command("OPEN_SESSION", now, sessionId);
    }
    private void Command(string command, double now, params string[] args) => SendCommand(command, now, true, args);
    private void SendCommand(string command, double now, bool requiredForSetup, params string[] args)
    {
        uint seq = nextSequence++;
        string body = "CMD," + seq.ToString(CultureInfo.InvariantCulture) + ",0," + command;
        if (args.Length > 0) body += "," + string.Join(",", args);
        string wire = SerialInputProtocol.Encode(body);
        pending.Add(seq, new PendingCommand { Command = command, Wire = wire, SentAt = now, RequiredForSetup = requiredForSetup });
        send(wire);
    }
    public void Tick(double now)
    {
        if (!Compatible) return;
        foreach (uint seq in new List<uint>(pending.Keys))
        {
            PendingCommand request = pending[seq];
            if (now - request.SentAt < 1000) continue;
            if (request.Retries == 2)
            {
                pending.Remove(seq);
                LastCommandResult = request.Command + ": TIMEOUT (result unknown)";
                // Failed setup leaves input disabled. Never repeat an operation with a new SEQ.
                if (request.RequiredForSetup && (request.Command == "OPEN_SESSION" || request.Command == "GET_STATUS" || request.Command == "GET_CONFIG"))
                { SessionOpen = false; Invalidate(); }
                if (request.Command == "GET_STATUS") AdsStatus = ImuStatus = "Unknown";
                CommandTimedOut?.Invoke(request.Command);
                continue;
            }
            request.Retries++; request.SentAt = now; send(request.Wire);
        }
        PrepareHardwareAutomatically(now);
    }

    private void PrepareHardwareAutomatically(double now)
    {
        // Wait for hardware status and the initial setup ACKs. A calibrated device is left alone.
        // Each operation gets one SEQ per session; Tick only retries that exact wire on timeout.
        if (!SessionOpen || pending.Count != 0 || AdsStatus != "Ok" || ImuStatus != "Ok" ||
            (State != "WAIT_ANGLE" && State != "READY")) return;
        if (!Calibrated && !automaticCalibrationAttempted)
        {
            automaticCalibrationAttempted = true;
            TryCommand("CALIBRATE", new[] { "ALL" }, now, out _, out _);
        }
        else if (Calibrated && !AngleLocked && !automaticAngleZeroAttempted)
        {
            automaticAngleZeroAttempted = true;
            TryCommand("ANGLE_ZERO", Array.Empty<string>(), now, out _, out _);
        }
    }
    public void Disconnect()
    {
        pending.Clear(); SessionOpen = false; State = "DISCONNECTED"; Invalidate(); ResetHardware();
    }
    private void Invalidate()
    {
        hasTelemetry = false; PlayerOne = default; PlayerTwo = default;
    }
    private void SetState(string state)
    {
        State = state;
        if (state != "READY") Invalidate();
    }
    private void ApplyConfig(string name, string value)
    {
        if (name == "TEL_RATE") TelemetryRate = int.Parse(value, CultureInfo.InvariantCulture);
        if (name == "TEL_STREAM")
        {
            TelemetryEnabled = value == "1";
            if (!TelemetryEnabled) Invalidate();
        }
    }
    public string Receive(RgpFrame frame, double receivedAt)
    {
        string[] d = frame.Data;
        if (frame.Type == "SYS" && d[0] == "HELLO")
        {
            if (d[2].Split('.')[0].TrimStart('0') != "1")
            {
                Compatible = false; SessionOpen = false; pending.Clear(); Invalidate(); ResetHardware();
                return "INCOMPATIBLE";
            }
            Start(receivedAt); return "HELLO";
        }
        if (!Compatible) return "INCOMPATIBLE";
        switch (frame.Type)
        {
            case "TEL":
                if (!SessionOpen || State != "READY" || !TelemetryEnabled) return "NOT_READY";
                uint delta = unchecked(frame.Sequence - lastTelSequence);
                if (hasTelSequence && (delta == 0 || delta > 0x7FFFFFFF)) return "OLD_TEL";
                PlayerOne = frame.Player(0); PlayerTwo = frame.Player(1);
                hasTelSequence = true; lastTelSequence = frame.Sequence;
                lastTelemetryAt = receivedAt; hasTelemetry = true; return "OK";
            case "EVT":
                if (d[0] == "STATE") SetState(d[1]);
                if (d[0] == "ADS_READY") AdsStatus = "Ok";
                if (d[0] == "IMU_FOUND") ImuStatus = "Ok";
                if (d[0] == "ANGLE_LOCKED") AngleLocked = true;
                if (d[0] == "ANGLE_UNLOCKED") AngleLocked = false;
                if (d[0] == "CAL_START") Calibrated = false;
                // A bus-wide loss cannot identify which individual sensor was unplugged.
                if (d[0] == "I2C_LOST") AdsStatus = ImuStatus = "Unknown";
                if (d[0] == "I2C_RESTORED") AdsStatus = ImuStatus = "Unknown";
                if (d[0] == "CONFIG_CHANGED") ApplyConfig(d[1], d[2]);
                if (d[0] == "I2C_LOST" || d[0] == "ANGLE_UNLOCKED")
                { SetState(d[0] == "I2C_LOST" ? "RECOVERING" : "WAIT_ANGLE"); }
                return "EVENT";
            case "SYS":
                if (d[0] == "CONFIG") ApplyConfig(d[1], d[2]);
                if (d[0] == "DEVICE" && d[1] == "ADS1115") AdsAddress = d[2];
                if (d[0] == "DEVICE" && d[1] == "IMU") { ImuAddress = d[2]; ImuIdentity = d[3]; }
                return "SYS";
            case "ERR":
                if (d[0] == "ERROR" || d[0] == "FATAL")
                {
                    SetState(d[0]);
                    if (d[1] == "I2C") AdsStatus = ImuStatus = "Unknown";
                    if (d[1] == "ADS1115") AdsStatus = "Fault";
                    if (d[1] == "IMU") ImuStatus = "Fault";
                }
                return d[0];
            case "ACK": return Ack(d, receivedAt);
            case "IMU": case "DBG": return frame.Type;
            default: return "IGNORED";
        }
    }
    private string Ack(string[] d, double now)
    {
        SerialInputProtocol.UInt(d[0], out uint seq);
        if (!pending.TryGetValue(seq, out PendingCommand request) || request.Command != d[1]) return "UNMATCHED_ACK";
        // A delayed OPEN_SESSION ACK from before HELLO must not consume the new request.
        if (d[1] == "OPEN_SESSION" && d[2] == "OK" && d[3] != sessionId) return "UNMATCHED_ACK";
        pending.Remove(seq);
        LastCommandResult = d[1] + ": " + d[2];
        if (d[2] != "OK" && d[2] != "ACCEPTED")
        {
            if (request.RequiredForSetup && (d[1] == "OPEN_SESSION" || d[1] == "GET_STATUS" || d[1] == "GET_CONFIG"))
            { SessionOpen = false; Invalidate(); }
            return "ACK_FAILED";
        }
        if (d[2] == "OK")
        {
            switch (d[1])
            {
                case "OPEN_SESSION":
                    SessionOpen = true;
                    Command("PING", now); Command("GET_STATUS", now); Command("GET_CONFIG", now);
                    break;
                case "GET_STATUS":
                    AdsStatus = d[4] == "1" ? "Ok" : "Fault"; ImuStatus = d[5] == "1" ? "Ok" : "Fault";
                    AngleLocked = d[6] == "1"; Calibrated = d[7] == "1";
                    SetState(d[3] == "READY" && (d[4] != "1" || d[5] != "1" || d[6] != "1" || d[7] != "1") ? "NOT_READY" : d[3]);
                    break;
                case "GET_CONFIG":
                    for (int i = 3; i + 1 < d.Length; i += 2) ApplyConfig(d[i], d[i + 1]);
                    break;
                case "SET_STREAM": ApplyConfig(d[3] + "_STREAM", d[4]); break;
                case "SET_RATE": ApplyConfig(d[3] + "_RATE", d[4]); break;
            }
        }
        return "ACK_" + d[2];
    }
}
