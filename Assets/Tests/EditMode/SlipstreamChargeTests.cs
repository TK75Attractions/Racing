using NUnit.Framework;

public sealed class SlipstreamChargeTests
{
    [Test] public void OnlySustainedPursuitOfOneTargetAwardsABoost()
    {
        var chase = new SlipstreamCharge();
        for (int i = 0; i < 8; i++) Assert.IsFalse(chase.Tick(2, .1f));
        Assert.IsTrue(chase.Tick(2, .1f));
        Assert.AreEqual(0, chase.Charge);
        Assert.IsFalse(chase.Tick(2, .1f));
        Assert.Greater(chase.Cooldown, 0);
    }
    [Test] public void SwitchingCarsOrLeavingTheCorridorResetsProgress()
    {
        var chase = new SlipstreamCharge();
        for (int i = 0; i < 8; i++) chase.Tick(1, .1f);
        Assert.IsFalse(chase.Tick(2, .1f));
        Assert.AreEqual(.1f, chase.Charge, .0001f);
        chase.Tick(-1, .02f); Assert.AreEqual(0, chase.Charge);
        Assert.IsFalse(chase.Tick(1, 20f));
        chase.Clear(); Assert.AreEqual(0, chase.Cooldown);
        Assert.AreEqual(0, chase.Charge);
    }
    [Test] public void InvalidTimeCannotPoisonTheChargeState()
    {
        var chase = new SlipstreamCharge();
        chase.Tick(1, float.NaN); chase.Tick(1, -1); chase.Tick(1, float.PositiveInfinity);
        Assert.AreEqual(0, chase.Charge);
    }
}
