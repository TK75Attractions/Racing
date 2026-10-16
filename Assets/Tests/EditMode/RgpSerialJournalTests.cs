using NUnit.Framework;

public sealed class RgpSerialJournalTests
{
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
