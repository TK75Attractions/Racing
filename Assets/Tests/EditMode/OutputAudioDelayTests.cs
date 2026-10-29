using NUnit.Framework;

public sealed class OutputAudioDelayTests
{
    [Test] public void ZeroDelayPreservesStereoAndInput()
    {
        var line=new OutputAudioDelay(8,4);float[] input={1,2,3,4};
        var result=line.Process(input,2,0);
        Assert.That(result[0],Is.EqualTo(1));Assert.That(result[3],Is.EqualTo(4));Assert.That(input,Is.EqualTo(new float[]{1,2,3,4}));
    }
    [Test] public void DelayCountsFramesAcrossBlockBoundaries()
    {
        var line=new OutputAudioDelay(8,4);
        var first=line.Process(new float[]{1,2,0,0},2,3);
        Assert.That(first[0],Is.Zero);Assert.That(first[2],Is.Zero);
        var next=line.Process(new float[]{0,0,0,0},2,3);
        Assert.That(next[0],Is.Zero);Assert.That(next[2],Is.EqualTo(1));Assert.That(next[3],Is.EqualTo(2));
    }
    [Test] public void MonoIsCopiedToBothChannels()
    {
        var result=new OutputAudioDelay(8,4).Process(new float[]{.25f,.75f},1,1);
        Assert.That(result[0],Is.Zero);Assert.That(result[2],Is.EqualTo(.25f));Assert.That(result[3],Is.EqualTo(.25f));
    }
    [Test] public void ResetDoesNotLeakGameplayIntoMaintenance()
    {
        var line=new OutputAudioDelay(8,4);line.Process(new float[]{1,1,1,1},2,3);line.Reset();
        var result=line.Process(new float[8],2,3);Assert.That(result,Is.All.Zero);
    }
    [Test] public void OutputsRemainIndependent()
    {
        var first=new OutputAudioDelay(8,4);var second=new OutputAudioDelay(8,4);
        first.Process(new float[]{1,1},2,4);
        Assert.That(second.Process(new float[8],2,4),Is.All.Zero);
    }
}
