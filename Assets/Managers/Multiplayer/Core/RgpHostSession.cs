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
    }
    private readonly Action<string> send;
    private readonly Dictionary<uint, PendingCommand> pending = new Dictionary<uint, PendingCommand>();
    private uint nextSequence;
    private uint lastTelSequence;
    private bool hasTelSequence;
    private bool hasTelemetry;
    private double lastTelemetryAt;
    private string sessionId;
    public event Action<string> CommandTimedOut;
    public bool Compatible { get; private set; } = true;
    public bool SessionOpen { get; private set; }
    public string State { get; private set; } = "UNKNOWN";
    public string LastCommandResult { get; private set; } = "Waiting for session";
    public int TelemetryRate { get; private set; } = 100;
    public bool TelemetryEnabled { get; private set; } = true;
    public RgpPlayerInput PlayerOne { get; private set; }
    public RgpPlayerInput PlayerTwo { get; private set; }
    public int PendingCommands => pending.Count;
    public double StaleAfterMilliseconds => Math.Max(3000.0 / TelemetryRate, 100);
    public bool IsFresh(double now) => Compatible && SessionOpen && State == "READY" && TelemetryEnabled &&
        hasTelemetry && now - lastTelemetryAt <= StaleAfterMilliseconds;

    public RgpHostSession(Action<string> send) { this.send = send ?? throw new ArgumentNullException(nameof(send)); }
    public void Start(double now)
    {
        pending.Clear(); nextSequence = 0; hasTelSequence = false;
        SessionOpen = false; State = "UNKNOWN"; Compatible = true;
        TelemetryRate = 100; TelemetryEnabled = true; Invalidate();
        byte[] random = new byte[8];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
        sessionId = BitConverter.ToString(random).Replace("-", "");
        Command("OPEN_SESSION", now, sessionId);
    }
    private void Command(string command, double now, params string[] args)
    {
        uint seq = nextSequence++;
        string body = "CMD," + seq.ToString(CultureInfo.InvariantCulture) + ",0," + command;
        if (args.Length > 0) body += "," + string.Join(",", args);
        string wire = SerialInputProtocol.Encode(body);
        pending.Add(seq, new PendingCommand { Command = command, Wire = wire, SentAt = now });
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
                if (request.Command == "OPEN_SESSION" || request.Command == "GET_STATUS" || request.Command == "GET_CONFIG")
                { SessionOpen = false; Invalidate(); }
                CommandTimedOut?.Invoke(request.Command);
                continue;
            }
            request.Retries++; request.SentAt = now; send(request.Wire);
        }
    }
    public void Disconnect()
    {
        pending.Clear(); SessionOpen = false; State = "DISCONNECTED"; Invalidate();
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
                Compatible = false; SessionOpen = false; pending.Clear(); Invalidate();
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
                if (d[0] == "CONFIG_CHANGED") ApplyConfig(d[1], d[2]);
                if (d[0] == "I2C_LOST" || d[0] == "ANGLE_UNLOCKED")
                { SetState(d[0] == "I2C_LOST" ? "RECOVERING" : "WAIT_ANGLE"); }
                return "EVENT";
            case "SYS":
                if (d[0] == "CONFIG") ApplyConfig(d[1], d[2]);
                return "SYS";
            case "ERR":
                if (d[0] == "ERROR" || d[0] == "FATAL") SetState(d[0]);
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
            if (d[1] == "OPEN_SESSION" || d[1] == "GET_STATUS" || d[1] == "GET_CONFIG")
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
