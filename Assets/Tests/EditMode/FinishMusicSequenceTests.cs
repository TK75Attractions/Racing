using NUnit.Framework;
public sealed class FinishMusicSequenceTests
{
    [Test] public void FullCuePlaysOnceThenFollowsTheUnfinishedOpponent()
    {
        var music=new FinishMusicSequence();
        Assert.AreEqual(FinishMusicSequence.Track.Finish,music.Select(true,true,false,100));
        music.Begin(100.2,22.2);
        Assert.AreEqual(FinishMusicSequence.Track.Finish,music.Select(true,true,false,122.39));
        Assert.AreEqual(FinishMusicSequence.Track.Race,music.Select(true,true,false,122.41));
        music.Begin(200,22.2);Assert.AreEqual(122.4,music.EndsAt,.0001);
        Assert.AreEqual(FinishMusicSequence.Track.Results,music.Select(true,false,true,200));
    }
    [Test] public void ResultsCanOpenWithoutCuttingOrRestartingTheCue()
    {
        var music=new FinishMusicSequence();music.Begin(10,22.2);
        Assert.AreEqual(FinishMusicSequence.Track.Finish,music.Select(true,false,true,16));
        Assert.AreEqual(FinishMusicSequence.Track.Results,music.Select(true,false,true,33));
        Assert.AreEqual(FinishMusicSequence.Track.Results,music.Select(false,false,true,33));
    }
    [Test] public void TwoFinishesStartIndependentlyAndANewRaceResetsTheSequence()
    {
        var first=new FinishMusicSequence();var second=new FinishMusicSequence();
        first.Begin(10,22.2);second.Begin(30,22.2);
        Assert.AreEqual(FinishMusicSequence.Track.Results,first.Select(true,false,true,40));
        Assert.AreEqual(FinishMusicSequence.Track.Finish,second.Select(true,false,true,40));
        first.Reset();Assert.AreEqual(FinishMusicSequence.Track.Finish,first.Select(true,true,false,50));
    }
}
