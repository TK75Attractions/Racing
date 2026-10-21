using NUnit.Framework;

public sealed class CpuRacePaceTests
{
    [Test] public void LegacyFastCpuComparisonRemainsAvailable()
    {
        Assert.Less(CpuRacePace.LegacyFastRequest(31, -100, 0, 2, 50), 31);
        Assert.Greater(CpuRacePace.LegacyFastRequest(31, -100, 0, 3, 50), 31);
        Assert.AreEqual(50, CpuRacePace.LegacyFastRequest(31, 100, 0, 2, 50));
    }
    [Test] public void LegacyFastFloorsArePreservedForTheOffComparison()
    {
        Assert.AreEqual(28, CpuRacePace.LegacyFastRequest(10, -200, 0, 2, 50));
        Assert.AreEqual(32, CpuRacePace.LegacyFastRequest(10, -200, 0, 3, 50));
        Assert.AreEqual(50, CpuRacePace.LegacyFastRequest(0, -200, 0, 2, 50));
    }
    [Test] public void FastNearbyRivalsStillHaveAttackAndCounterattackWindows()
    {
        Assert.Greater(CpuRacePace.LegacyFastRequest(42, -10, 0, 2, 50), 42);
        Assert.Less(CpuRacePace.LegacyFastRequest(42, -10, 7, 2, 50), 42);
        Assert.LessOrEqual(CpuRacePace.LegacyFastRequest(80, 20, 0, 2, 50), 52);
    }
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
