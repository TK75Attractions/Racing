using NUnit.Framework;

public sealed class AdaptiveAiPaceTests
{
    [Test] public void SlowPlayersGetNearbyContestsWithoutTheOldHighSpeedFloor()
    {
        Assert.Less(CpuRacePace.FastRequest(12,-20,0,2,50),16);
        Assert.Less(CpuRacePace.FastRequest(12,-120,0,2,50),12);
        Assert.Less(CpuRacePace.FastRequest(22,-80,0,3,50),22);
    }
    [Test] public void CpuCanStillPassThenOfferACounterattackAtBothHumanPaces()
    {
        foreach(float pace in new[]{12f,22f,42f})
        {
            Assert.Greater(CpuRacePace.FastRequest(pace,-10,0,2,50),pace);
            Assert.Less(CpuRacePace.FastRequest(pace,-10,7,2,50),pace);
        }
    }
    [Test] public void StrongPlayersAndCatchingUpRetainTheFullSpeedPotential()
    {
        Assert.GreaterOrEqual(CpuRacePace.FastRequest(48,10,0,2,50),50);
        Assert.AreEqual(52,CpuRacePace.FastRequest(20,500,0,2,50));
        Assert.GreaterOrEqual(CpuRacePace.FastRequest(0,-500,0,2,50),8);
    }
    [Test] public void AutoP2AccompaniesP1InsteadOfEscaping()
    {
        Assert.AreEqual(14,CpuRacePace.CompanionRequest(14,10,50));
        Assert.Less(CpuRacePace.CompanionRequest(14,-80,50),14);
        Assert.Greater(CpuRacePace.CompanionRequest(14,80,50),14);
        Assert.AreEqual(50,CpuRacePace.CompanionRequest(48,100,50));
    }
    [Test] public void PaceObservationIgnoresShortStopsAndItemHitsButAdaptsToLongChanges()
    {
        var observed=new HumanPaceEstimate();
        for(int i=0;i<100;i++)observed.Observe(32,false,.1f);
        Assert.AreEqual(32,observed.Pace,.01f);
        for(int i=0;i<20;i++)observed.Observe(0,true,.1f);
        Assert.AreEqual(32,observed.Pace,.01f);
        for(int i=0;i<200;i++)observed.Observe(12,false,.1f);
        Assert.AreEqual(12,observed.Pace,.01f);
        for(int i=0;i<100;i++)observed.Observe(0,false,.1f);
        Assert.GreaterOrEqual(observed.Pace,8);
        Assert.Less(observed.Pace,12);
    }
    [Test] public void ObservationIsTimeBasedAndRejectsInvalidTelemetry()
    {
        var a=new HumanPaceEstimate();var b=new HumanPaceEstimate();
        for(int i=0;i<100;i++)a.Observe(40,false,.02f);
        for(int i=0;i<20;i++)b.Observe(40,false,.1f);
        Assert.AreEqual(a.Pace,b.Pace,.001f);
        float previous=a.Pace;a.Observe(float.NaN,false,.1f);a.Observe(30,false,float.PositiveInfinity);
        Assert.AreEqual(previous,a.Pace);
        Assert.AreEqual(18,CpuRacePace.FastRequest(float.PositiveInfinity,0,0,2,50));
    }
}
