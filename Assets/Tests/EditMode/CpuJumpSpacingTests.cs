using NUnit.Framework;

public sealed class CpuJumpSpacingTests
{
    [Test] public void ASlowerLeaderLimitsApproachSpeedBeforeBothCarsLeaveTheRoad()
    {
        Assert.AreEqual(18.5f, CpuJumpSpacing.Request(24, 18, 30));
        Assert.AreEqual(21.25f, CpuJumpSpacing.Request(24, 23, 15), .0001f);
    }
    [Test] public void SpacingNeverRequestsAParkOnTheRampAndNeverAcceleratesTheCar()
    {
        Assert.AreEqual(18.5f, CpuJumpSpacing.Request(24, 0, 4));
        Assert.AreEqual(12, CpuJumpSpacing.Request(12, 22, 15));
        Assert.AreEqual(24, CpuJumpSpacing.Request(24, 12, -4));
        Assert.AreEqual(24, CpuJumpSpacing.Request(24, 12, 80));
    }
}
