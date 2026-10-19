using NUnit.Framework;

public sealed class CpuRacePaceTests
{
    [Test] public void MaintainingTheTargetNeedsEnoughPedalToBalanceTheActualCarResistance()
    {
        Assert.AreEqual(.4f, CpuRacePace.Pedal(20, 20, .4f));
        Assert.Greater(CpuRacePace.Pedal(20, 18, .4f), .4f);
        Assert.Less(CpuRacePace.Pedal(20, 30, .4f), 0f);
    }
    [Test] public void CpuThrottleAndBrakeUseTheSameRangeAsThePlayersPedal()
    {
        Assert.AreEqual(1f, CpuRacePace.Pedal(30, 0, .6f));
        Assert.AreEqual(-1f, CpuRacePace.Pedal(0, 30, 0f));
    }
    [Test] public void ANearbyCpuCanCatchAndOvertakeInsteadOfOnlyFollowing()
    { Assert.Greater(CpuRacePace.Request(22, 15, 0, 2, 25, 5), 26); }
    [Test] public void TheCounterattackWindowGivesRoomWithoutStoppingInFrontOfThePlayer()
    {
        Assert.Less(CpuRacePace.Request(22, -10, 7, 2, 25, 5), 22);
        Assert.Greater(CpuRacePace.Request(22, -10, 7, 2, 25, 5), 18);
    }
    [Test] public void ACpuStillCatchingUpDoesNotEnterTheYieldWindowTooEarly()
    { Assert.Greater(CpuRacePace.Request(17, 20, 7, 2, 25, 5), 23); }
    [Test] public void AStrandedPlayerDoesNotMakeCpuParkAndCatchupIsBounded()
    {
        Assert.GreaterOrEqual(CpuRacePace.Request(0, -80, 0, 2, 25, 5), 10);
        Assert.AreEqual(30, CpuRacePace.Request(50, 200, 0, 2, 25, 5));
    }
}
