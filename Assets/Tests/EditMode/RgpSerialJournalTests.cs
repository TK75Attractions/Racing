using NUnit.Framework;

public sealed class RgpSerialJournalTests
{
    [Test] public void PausedDashboardReaderCannotBlockAcquisitionAndReceiveTimeIsPreserved()
    {
        var journal = new RgpSerialJournal();
        object gate = typeof(RgpSerialJournal).GetField("gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(journal);
        string wire = SerialInputProtocol.Encode("TEL,1,0,0,1,0,0,1,0");
        long before = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), finishedAt = 0;
        bool completed;
        using (var done = new System.Threading.ManualResetEventSlim())
        {
            var producer = new System.Threading.Thread(() => {
                journal.RecordWire("RX", wire);
                journal.Diagnostic("INPUT_GATE", "P1 RELEASE_REQUIRED reason=connection_lost");
                finishedAt = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                done.Set();
            }) { IsBackground = true };
            lock (gate) { producer.Start(); completed = done.Wait(10000); }
            producer.Join();
        }
        Assert.That(completed, Is.True, "A dashboard holding the history lock must not stop the UART producer.");
        var batch = journal.Read("", 0);
        Assert.That(batch.RawLines[0].Line, Is.EqualTo(wire));
        Assert.That(batch.RawLines[0].ReceivedAt, Is.InRange(before, finishedAt), "Keep acquisition time, not later drain time.");
        Assert.That(batch.Lines[0].Status, Is.EqualTo("INPUT_GATE"));
        Assert.That(batch.Lines[0].Id, Is.GreaterThan(batch.RawLines[0].Id));
    }
    [Test] public void ContendedPendingCaptureIsBoundedAndReportsItsLoss()
    {
        var journal = new RgpSerialJournal();
        var initial = journal.Read("", 0);
        object gate = typeof(RgpSerialJournal).GetField("gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(journal);
        string wire = SerialInputProtocol.Encode("TEL,1,0,0,1,0,0,1,0");
        bool completed;
        using (var done = new System.Threading.ManualResetEventSlim())
        {
            var producer = new System.Threading.Thread(() => { for (int i=0;i<10000;i++) journal.RecordWire("RX",wire); done.Set(); }) { IsBackground = true };
            lock (gate) { producer.Start(); completed = done.Wait(10000); }
            producer.Join();
        }
        Assert.That(completed, Is.True);
        var batch = journal.Read(initial.Session, initial.Cursor);
        Assert.That(batch.Gap && batch.RawGap, Is.True);
        Assert.That(batch.Alerts[0].Status, Is.EqualTo("JOURNAL_GAP"));
        Assert.That(batch.Alerts[0].Line, Is.EqualTo("dropped_pending=1808"));
        for (int i=0;i<40;i++) journal.Read(initial.Session, 0);
        var later = journal.Read(initial.Session, 0);
        Assert.That(later.RawGap, Is.True, "Evicting older raw entries must not erase the reported capture gap.");
        Assert.That(journal.LatestRaw(2000).Length, Is.EqualTo(1000));
    }
    [Test] public void TelemetryDoesNotEvictHistoryAndCommandsAreIncluded()
    {
        var journal = new RgpSerialJournal();
        journal.RecordWire("TX", SerialInputProtocol.Encode("CMD,1,0,PING"));
        journal.RecordWire("RX", SerialInputProtocol.Encode("EVT,2,0,STATE,READY"));
        for (int i = 0; i < 180000; i++)
            journal.RecordWire("RX", SerialInputProtocol.Encode("TEL," + (i % 65536) + ",0,-12.5,0.75,0,25,0,0.4"));
        var batch = journal.Read("", 0);
        Assert.That(batch.Lines.Length, Is.EqualTo(2));
        Assert.That(batch.Lines[0].Direction, Is.EqualTo("TX"));
        Assert.That(batch.Lines[1].Line, Does.Contain("STATE,READY"));
        Assert.That(batch.Telemetry.Line, Does.StartWith("@TEL,"));
        Assert.That(batch.Gap, Is.False);
        Assert.That(journal.Read(batch.Session, batch.Cursor).Lines, Is.Empty);
    }
    [Test] public void RawReplayPreservesTelTransmitAndTerminatorsIndependently()
    {
        var journal = new RgpSerialJournal();
        string wire = SerialInputProtocol.Encode("TEL,1,0,0,1,0,0,1,0").Replace("\n", "\r\n");
        journal.RecordWire("RX", wire);
        journal.RecordWire("TX", SerialInputProtocol.Encode("CMD,1,0,PING"));
        var first = journal.Read("", 0);
        Assert.That(first.RawLines.Length, Is.EqualTo(2));
        Assert.That(first.RawLines[0].Line, Is.EqualTo(wire));
        Assert.That(first.Lines.Length, Is.EqualTo(1));
        Assert.That(first.RawLines[1].Direction, Is.EqualTo("TX"));
        var next = journal.Read(first.Session, first.Cursor, rawAfter: first.RawCursor);
        Assert.That(next.RawLines, Is.Empty);
        for (int i = 0; i < 1100; i++) journal.RecordWire("RX", SerialInputProtocol.Encode("TEL,"+(i+2)+",0,0,1,0,0,1,0"));
        var replay = journal.Read(first.Session, first.Cursor, rawAfter: first.RawCursor);
        Assert.That(replay.RawGap, Is.True); Assert.That(replay.Gap, Is.False);
        Assert.That(journal.LatestRaw(1000).Length, Is.EqualTo(1000));
        Assert.That(journal.Read(first.Session, replay.Cursor, rawAfter: replay.RawCursor).RawLines[0].Id,
            Is.GreaterThan(replay.RawLines[replay.RawLines.Length-1].Id));
    }
    [Test] public void InvalidTelemetryIsRetainedAndReconnectHasNoDuplicates()
    {
        var journal = new RgpSerialJournal();
        journal.RecordWire("RX", "@TEL,1,0,0,1,0,0,1,0*0000\n");
        var first = journal.Read("", 0);
        Assert.That(first.Lines[0].Status, Is.EqualTo("INVALID"));
        Assert.That(first.Telemetry, Is.Null);
        journal.Diagnostic("STALE", "TEL");
        var second = journal.Read(first.Session, first.Cursor);
        Assert.That(second.Lines.Length, Is.EqualTo(1));
        Assert.That(second.Lines[0].Status, Is.EqualTo("STALE"));
    }
    [Test] public void BoundedReplayReportsGapsAndResetsOnDeviceChange()
    {
        var journal = new RgpSerialJournal();
        var initial = journal.Read("", 0);
        for (int i = 0; i < 5200; i++) journal.Diagnostic("INFO", "event" + i);
        var replay = journal.Read(initial.Session, 0);
        Assert.That(replay.Gap, Is.True);
        Assert.That(replay.Lines.Length, Is.EqualTo(128));
        Assert.That(replay.Cursor, Is.EqualTo(replay.Lines[127].Id));
        long cursor = replay.Cursor; int total = 128;
        while (true)
        {
            var next = journal.Read(initial.Session, cursor);
            total += next.Lines.Length;
            if (next.Cursor == cursor) break;
            cursor = next.Cursor;
        }
        Assert.That(total, Is.EqualTo(5000));
        var other = new RgpSerialJournal(); other.Diagnostic("INFO", "new source");
        var changed = other.Read(initial.Session, cursor);
        Assert.That(changed.Lines.Length, Is.EqualTo(1));
        Assert.That(changed.Gap, Is.False);
    }
    [Test] public void JsonEscapesDeviceTextAndConcurrentReadsRemainOrdered()
    {
        var journal = new RgpSerialJournal();
        journal.Diagnostic("ERROR", "a\"b\\c\n\t車");
        Assert.That(journal.Read("", 0).Lines[0].Json, Does.Contain("a\\\"b\\\\c\\u000a\\u0009車"));
        System.Threading.Tasks.Parallel.For(0, 1000, i => { journal.Diagnostic("INFO", "line" + i); journal.Read("", 0); });
        var batch = journal.Read("", 0);
        for (int i = 1; i < batch.Lines.Length; i++) Assert.That(batch.Lines[i].Id, Is.GreaterThan(batch.Lines[i-1].Id));
    }
}
