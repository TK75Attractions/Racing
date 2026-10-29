using NUnit.Framework;

public sealed class TutorialGimmickBriefingTests
{
    private static void Wait(TutorialGimmickBriefing guide, float seconds, bool connected=true, float pedal=0f)
    { for(int i=0; i<(int)(seconds*20); i++) guide.Tick(.05f,connected,pedal); }
    [Test] public void EveryPageIsShownAndBriefingFinishesWithoutInput()
    {
        var guide=new TutorialGimmickBriefing();
        for(int page=0;page<TutorialGimmickBriefing.PageCount;page++)
        {
            Assert.That(guide.Page,Is.EqualTo(page)); Assert.That(guide.IsComplete,Is.False);
            Wait(guide,10.1f,false);
        }
        Assert.That(guide.IsComplete,Is.True);
    }
    [Test] public void HoldingPedalOrTappingTooEarlyCannotSkipTheReadingTime()
    {
        var guide=new TutorialGimmickBriefing();
        Wait(guide,4.1f,true,1f); Assert.That(guide.Page,Is.Zero);
        guide.Tick(.05f,true,0f); guide.Tick(.05f,true,1f);
        Assert.That(guide.Page,Is.EqualTo(1));
        Wait(guide,4.1f,true,1f); Assert.That(guide.Page,Is.EqualTo(1));
        guide.Tick(.05f,true,0f);guide.Tick(.05f,true,1f);Assert.That(guide.Page,Is.EqualTo(2));
    }
    [Test] public void APressBeforeReadTimeNeedsAnotherReleaseAndPress()
    {
        var guide=new TutorialGimmickBriefing(); guide.Tick(.05f,true,0f);
        Wait(guide,4.1f,true,1f);Assert.That(guide.Page,Is.Zero);
        guide.Tick(.05f,true,0f);guide.Tick(.05f,true,1f);Assert.That(guide.Page,Is.EqualTo(1));
    }
    [Test] public void LargeOrInvalidFramesAndDisconnectedPedalsCannotSkipPages()
    {
        var guide=new TutorialGimmickBriefing();
        guide.Tick(float.NaN,true,0f);guide.Tick(float.PositiveInfinity,true,0f);guide.Tick(-2f,true,0f);
        Assert.That(guide.Elapsed,Is.Zero);
        guide.Tick(100f,true,0f);Assert.That(guide.Page,Is.Zero);Assert.That(guide.Elapsed,Is.EqualTo(.1f));
        Wait(guide,4.1f,false);guide.Tick(.05f,false,1f);Assert.That(guide.Page,Is.Zero);
    }
    [Test] public void ReconnectionWhileHoldingPedalNeedsAFreshRelease()
    {
        var guide=new TutorialGimmickBriefing();Wait(guide,4.1f);
        guide.Tick(.05f,false,0f);guide.Tick(.05f,true,1f);
        Assert.That(guide.Page,Is.Zero);
        guide.Tick(.05f,true,0f);guide.Tick(.05f,true,1f);Assert.That(guide.Page,Is.EqualTo(1));
    }
    [Test] public void PlayersReadIndependently()
    {
        var first=new TutorialGimmickBriefing();var second=new TutorialGimmickBriefing();
        Wait(first,4.1f);first.Tick(.05f,true,1f);
        Assert.That(first.Page,Is.EqualTo(1));Assert.That(second.Page,Is.Zero);
    }
}
