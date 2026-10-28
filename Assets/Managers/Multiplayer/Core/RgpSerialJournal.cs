using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;

// Independent of Unity's frame loop: called directly by the serial receive/write threads.
public sealed class RgpSerialJournal
{
    public sealed class Entry
    {
        public readonly long Id, ReceivedAt;
        public readonly string Time, Status, Line, Direction;
        // Entries are immutable. Build JSON once rather than on every stream read/eviction.
        public string Json { get; }
        internal Entry(long id, string time, string status, string line, string direction, long receivedAt)
        {
            Id = id; Time = time; Status = status; Line = line; Direction = direction; ReceivedAt = receivedAt;
            Json = "{\"id\":" + Id + ",\"time\":" + Quote(Time) + ",\"status\":" + Quote(Status) +
                ",\"line\":" + Quote(Line) + ",\"direction\":" + Quote(Direction) + ",\"receivedAt\":" + ReceivedAt + "}";
        }
    }
    public sealed class Batch
    {
        public string Session;
        public long Cursor, RawCursor;
        public bool Gap, RawGap;
        public Entry Telemetry;
        public Entry[] Lines, Alerts, RawLines;
    }
    private readonly object gate = new object();
    private struct Pending
    {
        public string Direction, Text, Status;
        public bool IsWire;
        public DateTimeOffset ReceivedAt;
    }
    private readonly ConcurrentQueue<Pending> pending = new ConcurrentQueue<Pending>();
    private int pendingCount, droppedPending;
    private int drainScheduled;
    private static readonly WaitCallback drainWork = state => ((RgpSerialJournal)state).DrainOnWorker();
    private readonly Queue<Entry> history = new Queue<Entry>();
    private readonly Queue<Entry> raw = new Queue<Entry>();
    private long rawEvictedThrough;
    private int rawBytes;
    private readonly Queue<Entry> alerts = new Queue<Entry>();
    private readonly string session = Guid.NewGuid().ToString("N");
    private long sequence, evictedThrough;
    private int bytes;
    private Entry telemetry;
    public void RecordWire(string direction, string wire)
        => Enqueue(new Pending { Direction = direction, Text = wire, IsWire = true, ReceivedAt = DateTimeOffset.Now });
    public void Diagnostic(string status, string text)
        => Enqueue(new Pending { Direction = "HOST", Text = text, Status = status, ReceivedAt = DateTimeOffset.Now });

    private void Enqueue(Pending item)
    {
        Interlocked.Increment(ref pendingCount);
        pending.Enqueue(item);
        // A paused dashboard reader must never hold up physical serial acquisition.
        while (Volatile.Read(ref pendingCount) > 8192 && pending.TryDequeue(out _))
        { Interlocked.Decrement(ref pendingCount); Interlocked.Increment(ref droppedPending); }
        ScheduleDrain();
    }

    private void ScheduleDrain()
    {
        if (Interlocked.CompareExchange(ref drainScheduled, 1, 0) == 0)
            ThreadPool.QueueUserWorkItem(drainWork, this);
    }

    // Parsing, CRC validation and JSON formatting must never run on the UART
    // producer, even when the dashboard lock happens to be uncontended.
    private void DrainOnWorker()
    {
        try { lock (gate) { DrainPending(256); } }
        finally
        {
            Volatile.Write(ref drainScheduled, 0);
            if (!pending.IsEmpty) ScheduleDrain();
        }
    }

    // Call with gate held; bound catch-up work even after a long dashboard pause.
    private void DrainPending(int maximum)
    {
        int dropped = Interlocked.Exchange(ref droppedPending, 0);
        if (dropped > 0)
        {
            rawEvictedThrough = evictedThrough = ++sequence;
            Add("JOURNAL_GAP", "dropped_pending=" + dropped, "HOST", false, DateTimeOffset.Now);
        }
        for (int i = 0; i < maximum && pending.TryDequeue(out Pending item); i++)
        {
            Interlocked.Decrement(ref pendingCount);
            if (item.IsWire) RecordPendingWire(item);
            else Add(item.Status, item.Text, item.Direction, false, item.ReceivedAt);
        }
    }

    private void RecordPendingWire(Pending item)
    {
        string wire = item.Text, direction = item.Direction;
        bool valid = SerialInputProtocol.TryParse(wire, out RgpFrame frame);
        string status = !valid ? "INVALID" : direction == "TX" ? "SENT" : frame.Type == "ERR" ? frame.Data[0] :
            frame.Type == "EVT" ? "EVENT" : frame.Type == "ACK" && frame.Data.Length > 2 &&
            frame.Data[2] != "OK" && frame.Data[2] != "ACCEPTED" ? "ACK_FAILED" : "RECEIVED";
        Add(status, wire.TrimEnd('\r', '\n'), direction, valid && frame.Type == "TEL", item.ReceivedAt, wire);
    }
    private void Add(string status, string line, string direction, bool isTelemetry, DateTimeOffset receivedAt, string wire = null)
    {
        if (line.Length > 512) line = line.Substring(0, 512);
        lock (gate)
        {
            var entry = new Entry(++sequence, receivedAt.ToString("HH:mm:ss.fff"), status,
                line, direction, receivedAt.ToUnixTimeMilliseconds());
            if (wire != null)
            {
                var rawEntry = new Entry(entry.Id, entry.Time, entry.Status,
                    wire.Length > 1024 ? wire.Substring(0, 1024) : wire, direction, entry.ReceivedAt);
                raw.Enqueue(rawEntry); rawBytes += Encoding.UTF8.GetByteCount(rawEntry.Json);
                while (raw.Count > 1000 || rawBytes > 512 * 1024)
                { Entry old = raw.Dequeue(); rawEvictedThrough = Math.Max(rawEvictedThrough, old.Id); rawBytes -= Encoding.UTF8.GetByteCount(old.Json); }
            }
            if (isTelemetry) { telemetry = entry; return; }
            history.Enqueue(entry); bytes += Encoding.UTF8.GetByteCount(entry.Json);
            if (status == "INVALID" || status == "WARN" || status == "ERROR" || status == "FATAL" || status == "STALE" ||
                status == "INCOMPATIBLE" || status == "TIMEOUT" || status == "ACK_FAILED" || status == "JOURNAL_GAP" ||
                (status == "EVENT" && (line.Contains(",STATE,RECOVERING*") || line.Contains(",STATE,FATAL*"))))
            { alerts.Enqueue(entry); while (alerts.Count > 8) alerts.Dequeue(); }
            while (history.Count > 5000 || bytes > 4 * 1024 * 1024)
            {
                Entry old = history.Dequeue(); evictedThrough = Math.Max(evictedThrough, old.Id); bytes -= Encoding.UTF8.GetByteCount(old.Json);
            }
        }
    }
    public Batch Read(string previousSession, long after, int maximum = 128, long rawAfter = 0)
    {
        lock (gate)
        {
            DrainPending(256);
            bool same = previousSession == session;
            long start = same ? after : 0;
            Entry[] lines = history.Where(entry => entry.Id > start).Take(maximum).ToArray();
            Entry[] rawLines = raw.Where(entry => entry.Id > (same ? rawAfter : 0)).Take(maximum).ToArray();
            return new Batch { Session = session, Gap = same && start < evictedThrough,
                Cursor = lines.Length == maximum ? lines[lines.Length - 1].Id : sequence,
                Lines = lines, Alerts = alerts.ToArray(), Telemetry = telemetry, RawLines = rawLines,
                RawGap = same && rawAfter < rawEvictedThrough,
                RawCursor = rawLines.Length == maximum ? rawLines[rawLines.Length - 1].Id : sequence };
        }
    }
    public Entry[] LatestRaw(int maximum = 100)
    { lock (gate) { DrainPending(256); return raw.Skip(Math.Max(0, raw.Count - maximum)).ToArray(); } }
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
