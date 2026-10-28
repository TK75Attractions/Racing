using NUnit.Framework;

public sealed class RaceDisplayRoutingTests
{
    [Test]
    public void PlayerTwoUsesAConnectedSecondaryDisplay()
    {
        Assert.That(RaceDisplayRouting.PlayerTwoDisplay(2, 2), Is.EqualTo(1), "Saved SampleScene prefers Display 3.");
        Assert.That(RaceDisplayRouting.PlayerTwoDisplay(2, 3), Is.EqualTo(2), "Keep a connected preference.");
        Assert.That(RaceDisplayRouting.PlayerTwoDisplay(1, 2), Is.EqualTo(1));
        Assert.That(RaceDisplayRouting.PlayerTwoDisplay(0, 2), Is.EqualTo(1), "Never share P1 output.");
        Assert.That(RaceDisplayRouting.PlayerTwoDisplay(8, 10), Is.EqualTo(1), "Unity supports at most eight outputs.");
        Assert.That(RaceDisplayRouting.PlayerTwoDisplay(2, 1), Is.EqualTo(-1));
        Assert.That(RaceDisplayRouting.PlayerTwoDisplay(1, 0), Is.EqualTo(-1));
    }

    [Test]
    public void BroadcastNeverTakesAPlayerOutput()
    {
        Assert.That(RaceDisplayRouting.BroadcastDisplay(0, 1), Is.EqualTo(2));
        Assert.That(RaceDisplayRouting.BroadcastDisplay(0, 2), Is.EqualTo(1));
        Assert.That(RaceDisplayRouting.BroadcastDisplay(0, 7), Is.EqualTo(2));
    }
}
