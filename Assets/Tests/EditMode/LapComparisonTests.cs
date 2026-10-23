using NUnit.Framework;

public class LapComparisonTests
{
    [Test] public void FirstLapHasNoFabricatedDelta()
    {
        var comparison=new LapComparison();Assert.That(comparison.Sample(0,.5f,30,0),Is.Null);
    }
    [Test] public void ComparesTimeAtTheSamePositionRatherThanWholeLapDuration()
    {
        var comparison=new LapComparison();comparison.Sample(0,0,0,0);comparison.Sample(0,.5f,30,0);comparison.Sample(0,.99f,59.4f,0);
        Assert.That(comparison.Sample(1,0,0,60),Is.EqualTo(0).Within(.001));
        Assert.That(comparison.Sample(1,.5f,29,60),Is.EqualTo(-1).Within(.01));
        Assert.That(comparison.Sample(1,.9f,52.2f,60),Is.EqualTo(-1.8f).Within(.01));
    }
    [Test] public void RetryDiscardsThePreviousRun()
    {
        var comparison=new LapComparison();comparison.Sample(0,.9f,54,0);comparison.Sample(1,.5f,29,60);
        Assert.That(comparison.Sample(0,0,0,0),Is.Null);
        Assert.That(comparison.Sample(0,.5f,35,0),Is.Null);
    }
}
