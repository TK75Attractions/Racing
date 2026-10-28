using NUnit.Framework;

public sealed class LapTraversalTests
{
    [Test] public void RepeatedForwardAndBackwardFinishCrossingsNeverArmALap()
    {
        var lap = new LapTraversal();lap.Rebase(.001);
        for (int i=0;i<100;i++){lap.Observe(.998,true);lap.Observe(.002,true);}
        Assert.That(lap.IsArmed,Is.False);
    }
    [Test] public void FullCircuitArmsOnceAndEachNewLapNeedsAnotherCircuit()
    {
        var lap = new LapTraversal();
        for(int round=0;round<2;round++)
        {
            lap.Reset();lap.Rebase(0);
            for(int i=1;i<=96;i++)lap.Observe(i/100.0,true);
            Assert.That(lap.IsArmed,Is.True);
            lap.Reset();Assert.That(lap.IsArmed,Is.False);
        }
    }
    [Test] public void TeleportsAndRecoveryCannotCompleteAnUnvisitedSection()
    {
        var lap=new LapTraversal();lap.Rebase(0);
        lap.Observe(.1,false);lap.Observe(.9,false);lap.Observe(.99,true);
        Assert.That(lap.IsArmed,Is.False);
        lap.Reset();lap.Rebase(0);
        for(int i=1;i<=45;i++)lap.Observe(i/100.0,true);
        lap.Rebase(.30); // Safe recovery preserves visited gates but awards no distance.
        for(int i=31;i<=99;i++)lap.Observe(i/100.0,true);
        Assert.That(lap.IsArmed,Is.True);
    }
    [Test] public void ReverseCircuitAndInvalidValuesDoNotArm()
    {
        var lap=new LapTraversal();lap.Rebase(0);
        for(int i=99;i>=1;i--)lap.Observe(i/100.0,true);
        lap.Observe(double.NaN,true);Assert.That(lap.IsArmed,Is.False);
    }
    [Test] public void FastFinishSweepCountsOnlyAnArmedForwardCircuit()
    {
        var lap = new LapTraversal(); lap.Rebase(0);
        for (int i = 1; i <= 99; i++) lap.Observe(i / 100.0, true);
        lap.Observe(.004, true);
        Assert.That(lap.CrossedFinish, Is.True);
        lap.Reset(); lap.Rebase(.99); lap.Observe(.004, true);
        Assert.That(lap.CrossedFinish, Is.False, "A finish sweep cannot replace ordered gates.");
    }
    [Test] public void InvalidExcursionCannotBridgeAnUnvisitedGate()
    {
        var lap = new LapTraversal(); lap.Rebase(0);
        for (int i = 1; i <= 20; i++) lap.Observe(i / 100.0, true);
        for (int i = 21; i <= 40; i++) lap.Observe(i / 100.0, false);
        for (int i = 41; i <= 99; i++) lap.Observe(i / 100.0, true);
        lap.Observe(.004, true);
        Assert.That(lap.IsArmed, Is.False);
        Assert.That(lap.CrossedFinish, Is.False);
    }
}
