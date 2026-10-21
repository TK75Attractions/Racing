using NUnit.Framework;

public class FrameRateCounterTests
{
    [Test]
    public void CountsIntervalsAndUpdatesFromSixtyToThirtyFps()
    {
        var counter = new FrameRateCounter();
        counter.RecordFrame(100);
        Assert.That(counter.FramesPerSecond, Is.Zero);
        for (int i = 1; i <= 30; i++) counter.RecordFrame(100 + i / 60.0);
        Assert.That(counter.FramesPerSecond, Is.EqualTo(60).Within(.0001));
        Assert.That(counter.FrameMilliseconds, Is.EqualTo(1000.0 / 60).Within(.0001));
        for (int i = 1; i <= 15; i++) counter.RecordFrame(100.5 + i / 30.0);
        Assert.That(counter.FramesPerSecond, Is.EqualTo(30).Within(.0001));
        Assert.That(counter.FrameMilliseconds, Is.EqualTo(1000.0 / 30).Within(.0001));
    }

    [Test]
    public void IncludesStallsInTheMeasuredTime()
    {
        var counter = new FrameRateCounter();
        counter.RecordFrame(0);
        for (int i = 1; i <= 4; i++) counter.RecordFrame(i / 10.0);
        Assert.That(counter.FramesPerSecond, Is.Zero);
        counter.RecordFrame(1);
        Assert.That(counter.FramesPerSecond, Is.EqualTo(5));
        Assert.That(counter.FrameMilliseconds, Is.EqualTo(200));
    }

    [Test]
    public void ReenableAndClockResetDiscardTheOldSample()
    {
        var counter = new FrameRateCounter();
        counter.RecordFrame(10);
        counter.RecordFrame(10.5);
        Assert.That(counter.FramesPerSecond, Is.EqualTo(2));
        counter.Reset();
        Assert.That(counter.FramesPerSecond, Is.Zero);
        Assert.That(counter.FrameMilliseconds, Is.Zero);
        counter.RecordFrame(1000);
        counter.RecordFrame(1000.25);
        Assert.That(counter.FramesPerSecond, Is.Zero);
        counter.RecordFrame(0);
        counter.RecordFrame(.5);
        Assert.That(counter.FramesPerSecond, Is.EqualTo(2));
    }

    [Test]
    public void RetainsPrecisionAfterLongRunningSessions()
    {
        var counter = new FrameRateCounter();
        counter.RecordFrame(1000000000);
        for (int i = 1; i <= 60; i++) counter.RecordFrame(1000000000 + i / 120.0);
        Assert.That(counter.FramesPerSecond, Is.EqualTo(120).Within(.001));
    }
}
