using NUnit.Framework;

public sealed class RoadRewardBudgetTests
{
    [Test] public void SimultaneousNewRewardsUseTheStrongerAcceleration()
    {
        Assert.AreEqual(.08f, RoadRewardBudget.SpeedDelta(.02f, .7f, .5f, 0, 10), .00001f);
        Assert.AreEqual(.05f, RoadRewardBudget.SpeedDelta(.02f, 0, .5f, 0, 10), .00001f);
        Assert.AreEqual(.025f, RoadRewardBudget.SpeedDelta(.02f, 0, .01f, 0, 10), .00001f);
    }
    [Test] public void TheLastFrameOfLegacyBoostStillConsumesThisFramesBudget()
    {
        Assert.AreEqual(.02f, RoadRewardBudget.SpeedDelta(.02f, .7f, .5f, .34f, 10), .00001f);
        Assert.AreEqual(0, RoadRewardBudget.SpeedDelta(.02f, .7f, .5f, .4f, 10));
    }
    [Test] public void PendingLegacyAccelerationCountsTowardsTheSpeedLimit()
    {
        Assert.AreEqual(.02f, RoadRewardBudget.SpeedDelta(.02f, .7f, .5f, .08f, 31.9f), .00001f);
        Assert.AreEqual(0, RoadRewardBudget.SpeedDelta(.02f, .7f, .5f, .2f, 31.9f));
        Assert.AreEqual(0, RoadRewardBudget.SpeedDelta(.02f, .7f, .5f, 0, 32f));
    }
    [Test] public void InvalidOrNegativeTimeCannotApplyAReward()
    {
        Assert.AreEqual(0, RoadRewardBudget.SpeedDelta(float.NaN, 1, 1, 0, 10));
        Assert.AreEqual(0, RoadRewardBudget.SpeedDelta(-1, 1, 1, 0, 10));
    }
}
