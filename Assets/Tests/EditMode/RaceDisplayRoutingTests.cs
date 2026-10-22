using NUnit.Framework;

public sealed class RaceDisplayRoutingTests
{
    [Test]
    public void BroadcastNeverTakesAPlayerOutput()
    {
        Assert.That(RaceDisplayRouting.BroadcastDisplay(0, 1), Is.EqualTo(2));
        Assert.That(RaceDisplayRouting.BroadcastDisplay(0, 2), Is.EqualTo(1));
        Assert.That(RaceDisplayRouting.BroadcastDisplay(0, 7), Is.EqualTo(2));
    }
}
