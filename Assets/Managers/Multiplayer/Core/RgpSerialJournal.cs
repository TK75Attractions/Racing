using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// Independent of Unity's frame loop: called directly by the serial receive/write threads.
public sealed class RgpSerialJournal
{
    public sealed class Entry
    {
        public long Id;
        public string Time, Status, Line, Direction;
        public long ReceivedAt;
        public string Json => "{\"id\":" + Id + ",\"time\":" + Quote(Time) + ",\"status\":" + Quote(Status) +
            ",\"line\":" + Quote(Line) + ",\"direction\":" + Quote(Direction) + ",\"receivedAt\":" + ReceivedAt + "}";
    }
    public sealed class Batch
    {
        public string Session;
        public long Cursor;
        public bool Gap;
        public Entry Telemetry;
        public Entry[] Lines, Alerts;
    }
    private readonly object gate = new object();
    private readonly Queue<Entry> history = new Queue<Entry>();
    private readonly Queue<Entry> alerts = new Queue<Entry>();
    private readonly string session = Guid.NewGuid().ToString("N");
    private long sequence, evictedThrough;
    private int bytes;
    private Entry telemetry;
    public void RecordWire(string direction, string wire)
    {
        bool valid = SerialInputProtocol.TryParse(wire, out RgpFrame frame);
        string status = !valid ? "INVALID" : direction == "TX" ? "SENT" : frame.Type == "ERR" ? frame.Data[0] :
            frame.Type == "EVT" ? "EVENT" : frame.Type == "ACK" && frame.Data.Length > 2 &&
            frame.Data[2] != "OK" && frame.Data[2] != "ACCEPTED" ? "ACK_FAILED" : "RECEIVED";
        Add(status, wire.TrimEnd('\r', '\n'), direction, valid && frame.Type == "TEL");
    }
    public void Diagnostic(string status, string text) => Add(status, text, "HOST", false);
    private void Add(string status, string line, string direction, bool isTelemetry)
    {
        if (line.Length > 512) line = line.Substring(0, 512);
        lock (gate)
        {
            var entry = new Entry { Id = ++sequence, Time = DateTime.Now.ToString("HH:mm:ss.fff"), Status = status,
                Line = line, Direction = direction, ReceivedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            if (isTelemetry) { telemetry = entry; return; }
            history.Enqueue(entry); bytes += Encoding.UTF8.GetByteCount(entry.Json);
            if (status == "INVALID" || status == "WARN" || status == "ERROR" || status == "FATAL" || status == "STALE" ||
                status == "INCOMPATIBLE" || status == "TIMEOUT" || status == "ACK_FAILED" ||
                (status == "EVENT" && (line.Contains(",STATE,RECOVERING*") || line.Contains(",STATE,FATAL*"))))
            { alerts.Enqueue(entry); while (alerts.Count > 8) alerts.Dequeue(); }
            while (history.Count > 5000 || bytes > 4 * 1024 * 1024)
            {
                Entry old = history.Dequeue(); evictedThrough = old.Id; bytes -= Encoding.UTF8.GetByteCount(old.Json);
            }
        }
    }
    public Batch Read(string previousSession, long after, int maximum = 128)
    {
        lock (gate)
        {
            bool same = previousSession == session;
            long start = same ? after : 0;
            Entry[] lines = history.Where(entry => entry.Id > start).Take(maximum).ToArray();
            return new Batch { Session = session, Gap = same && start < evictedThrough,
                Cursor = lines.Length == maximum ? lines[lines.Length - 1].Id : sequence,
                Lines = lines, Alerts = alerts.ToArray(), Telemetry = telemetry };
        }
    }
    public static string Quote(string value)
    {
        var output = new StringBuilder("\"");
        foreach (char c in value ?? "")
        {
            if (c == '"' || c == '\\') output.Append('\\').Append(c);
            else if (c < 32) output.Append("\\u").Append(((int)c).ToString("x4"));
            else output.Append(c);
        }
        return output.Append('"').ToString();
    }
}
