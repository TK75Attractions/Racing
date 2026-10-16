using NUnit.Framework;

public class PreparationCountdownTests
{
    [Test]
    public void ExpiryOccursOnceAtTheBoundaryAndNeverGoesNegative()
    {
        var timer = new PreparationCountdown(30f);
        Assert.That(timer.Tick(29f), Is.False);
        Assert.That(timer.RemainingSeconds, Is.EqualTo(1f));
        Assert.That(timer.Tick(1f), Is.True);
        Assert.That(timer.HasExpired, Is.True);
        Assert.That(timer.RemainingSeconds, Is.Zero);
        Assert.That(timer.Tick(100f), Is.False);
        Assert.That(timer.RemainingSeconds, Is.Zero);
    }

    [Test]
    public void StoppingPreservesTimeAndDoesNotExpire()
    {
        var timer = new PreparationCountdown(30f);
        timer.Tick(4f);
        timer.Stop();
        Assert.That(timer.Tick(100f), Is.False);
        Assert.That(timer.RemainingSeconds, Is.EqualTo(26f));
        Assert.That(timer.HasExpired, Is.False);
    }

    [Test]
    public void EachScreenHasItsOwnTimeAndNegativeDeltaCannotAddTime()
    {
        var first = new PreparationCountdown(30f);
        var second = new PreparationCountdown(60f);
        first.Tick(20f);
        second.Tick(-5f);
        Assert.That(first.RemainingSeconds, Is.EqualTo(10f));
        Assert.That(second.RemainingSeconds, Is.EqualTo(60f));
        Assert.That(new PreparationCountdown(-1f).Tick(0f), Is.True);
    }
}
