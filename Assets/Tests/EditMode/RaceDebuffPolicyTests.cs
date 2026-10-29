using NUnit.Framework;

public sealed class RaceDebuffPolicyTests
{
    [Test]
    public void DirectedAttacksOnlyTargetCarsAhead()
    {
        for (int count = 2; count <= 4; count++)
        for (int owner = 1; owner <= count; owner++)
        for (int target = 1; target <= count; target++)
            Assert.That(RaceDebuffPolicy.CanTarget(owner, target, count, count), Is.EqualTo(target < owner));
    }
    [Test]
    public void LastActiveRacerIsProtectedEvenAfterAFinishOrWithdrawal()
    {
        Assert.That(RaceDebuffPolicy.IsProtected(4, 4, 4), Is.True);
        Assert.That(RaceDebuffPolicy.IsProtected(3, 4, 3), Is.True);
        Assert.That(RaceDebuffPolicy.IsProtected(3, 4, 4), Is.False);
        Assert.That(RaceDebuffPolicy.IsProtected(0, 4, 0), Is.False);
    }
    [Test]
    public void TwoPlayerDuelsRetainContactItemCounterplay()
    {
        Assert.That(RaceDebuffPolicy.IsProtected(2, 2, 2), Is.False);
        Assert.That(RaceDebuffPolicy.CanTarget(2, 1, 2, 2), Is.True);
        Assert.That(RaceDebuffPolicy.CanTarget(1, 2, 2, 2), Is.False);
    }
}
