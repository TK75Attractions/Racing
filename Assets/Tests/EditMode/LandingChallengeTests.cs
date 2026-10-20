using NUnit.Framework;

public sealed class LandingChallengeTests
{
    private static void Fly(LandingChallenge challenge)
    { for (int i = 0; i < 12; i++) Assert.IsFalse(challenge.Tick(true, 0, true, false, .02f)); }
    [Test] public void ADesignatedFlightRequiresStableLandingAndAwardsOnlyOncePerLap()
    {
        var c = new LandingChallenge(); Assert.IsTrue(c.TryArm(2, 0)); Fly(c);
        for (int i = 0; i < 3; i++) Assert.IsFalse(c.Tick(true, 2, false, true, .02f));
        Assert.IsTrue(c.Tick(true, 2, false, true, .02f));
        Assert.IsFalse(c.TryArm(2, 0)); Assert.IsTrue(c.TryArm(2, 1));
    }
    [Test] public void BumpsAndOneWheelTouchesCannotAwardLanding()
    {
        var c = new LandingChallenge(); c.TryArm(0, 0);
        for (int i = 0; i < 20; i++) Assert.IsFalse(c.Tick(true, 0, false, false, .02f));
        Assert.IsFalse(c.Tick(true, 4, false, true, .02f));
        Fly(c); for (int i = 0; i < 20; i++) Assert.IsFalse(c.Tick(true, 1, false, true, .02f));
    }
    [Test] public void RecoveryCollisionAndTogglesCannotFarmAFlight()
    {
        var c = new LandingChallenge(); c.TryArm(0, 0); Fly(c);
        Assert.IsFalse(c.Tick(false, 4, false, true, .02f));
        c.Clear(); Assert.IsFalse(c.TryArm(0, 0));
        Assert.IsTrue(c.TryArm(1, 0)); Fly(c);
        Assert.IsFalse(c.Tick(true, 4, false, false, .02f));
        Assert.IsFalse(c.TryArm(1, 0));
    }
    [Test] public void BriefFlightAndLongFramesCannotCreateSuccess()
    {
        var c = new LandingChallenge(); c.TryArm(0, 0);
        c.Tick(true, 0, true, false, 3f);
        Assert.IsFalse(c.Tick(true, 4, false, true, 3f));
    }
}
