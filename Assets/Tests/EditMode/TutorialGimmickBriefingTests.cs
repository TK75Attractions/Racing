using NUnit.Framework;

public sealed class TutorialGimmickBriefingTests
{
    private static void Wait(TutorialGimmickBriefing guide, float seconds, bool connected=true, float pedal=0f)
    { for(int i=0; i<(int)(seconds*20); i++) guide.Tick(.05f,connected,pedal); }
    [Test] public void SingleOverviewFinishesAfterEightSecondsWithoutInput()
    {
        var guide=new TutorialGimmickBriefing();
        Assert.That(TutorialGimmickBriefing.PageCount,Is.EqualTo(1));
        Wait(guide,7.9f,false);Assert.That(guide.IsComplete,Is.False);
        Wait(guide,.2f,false);
        Assert.That(guide.IsComplete,Is.True);
    }
    [Test] public void HoldingPedalAdvancesOnlyAfterMinimumReadingTime()
    {
        var guide=new TutorialGimmickBriefing();
        Wait(guide,TutorialGimmickBriefing.MinimumReadSeconds-.1f,true,1f);
        Assert.That(guide.IsComplete,Is.False);
        Wait(guide,.2f,true,1f);
        Assert.That(guide.IsComplete,Is.True);
    }
    [Test] public void EarlyTapDoesNotSkipReadingOrAdvanceAfterRelease()
    {
        var guide=new TutorialGimmickBriefing();guide.Tick(.05f,true,1f);
        Wait(guide,TutorialGimmickBriefing.MinimumReadSeconds+.1f,true,0f);
        Assert.That(guide.IsComplete,Is.False);
        guide.Tick(.05f,true,1f);Assert.That(guide.IsComplete,Is.True);
    }
    [Test] public void InvalidFramesAreIgnoredAndLargeFramesRespectTheRealDeadline()
    {
        var guide=new TutorialGimmickBriefing();
        guide.Tick(float.NaN,true,0f);guide.Tick(float.PositiveInfinity,true,0f);guide.Tick(-2f,true,0f);
        Assert.That(guide.Elapsed,Is.Zero);
        guide.Tick(100f,false,1f);Assert.That(guide.IsComplete,Is.True);
    }
    [Test] public void OnlyValidConnectedPedalCanAdvanceBeforeAutomaticDeadline()
    {
        var guide=new TutorialGimmickBriefing();Wait(guide,TutorialGimmickBriefing.MinimumReadSeconds+.1f);
        guide.Tick(.05f,false,1f);guide.Tick(.05f,true,float.PositiveInfinity);
        guide.Tick(.05f,true,float.NaN);Assert.That(guide.IsComplete,Is.False);
        guide.Tick(.05f,true,1f);Assert.That(guide.IsComplete,Is.True);
    }
    [Test] public void PlayersReadIndependently()
    {
        var first=new TutorialGimmickBriefing();var second=new TutorialGimmickBriefing();
        Wait(first,TutorialGimmickBriefing.MinimumReadSeconds+.1f);first.Tick(.05f,true,1f);
        Assert.That(first.Page,Is.EqualTo(1));Assert.That(second.Page,Is.Zero);
    }
}
