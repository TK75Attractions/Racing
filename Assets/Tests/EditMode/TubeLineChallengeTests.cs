using NUnit.Framework;

public sealed class TubeLineChallengeTests
{
    private static bool Pass(TubeLineChallenge c, int lap, int side)
    {
        bool result = false;
        for (int i = 0; i <= 18; i++) result |= c.Tick(lap, side, i, true, .05f);
        return result;
    }
    [Test] public void EitherLaneAwardsOncePerLapAndPlayersAreIndependent()
    {
        var p1 = new TubeLineChallenge(); var p2 = new TubeLineChallenge();
        Assert.IsTrue(Pass(p1, 0, -1)); Assert.IsFalse(Pass(p1, 0, 1));
        Assert.IsTrue(Pass(p2, 0, 1)); Assert.IsTrue(Pass(p1, 1, 1));
    }
    [Test] public void JoiningHalfwaySwitchingLaneOrReversingCannotCompleteTheLine()
    {
        var c = new TubeLineChallenge(); Assert.IsFalse(c.Tick(0, 1, 10, true, .02f));
        c.Tick(0, 1, 0, true, .02f); c.Tick(0, -1, 1, true, .02f);
        Assert.IsFalse(c.Tick(0, -1, 18, true, .02f));
        c.Tick(0, 1, 0, true, .02f); c.Tick(0, 1, -1, true, .02f);
        Assert.IsFalse(c.IsPending);
    }
    [Test] public void LingeringAndTeleportingCannotAward()
    {
        var c = new TubeLineChallenge(); c.Tick(0, 1, 0, true, .02f);
        Assert.IsFalse(c.Tick(0, 1, 18, true, .02f));
        c.Tick(0, 1, 0, true, .02f);
        for (int i = 0; i < 40; i++) c.Tick(0, 1, 3, true, .1f);
        Assert.IsFalse(c.Tick(0, 1, 18, true, .02f));
    }
    [Test] public void OffAndRecoveryClearPendingButNeverEraseAnEarnedReward()
    {
        var c = new TubeLineChallenge(); c.Tick(0, 1, 0, true, .02f);
        c.Tick(0, 1, 1, false, .02f); Assert.IsFalse(c.IsPending);
        Assert.IsFalse(c.Tick(0, 1, 18, true, .02f));
        Assert.IsTrue(Pass(c, 0, 1)); c.Clear(); Assert.IsFalse(Pass(c, 0, -1));
    }
}
