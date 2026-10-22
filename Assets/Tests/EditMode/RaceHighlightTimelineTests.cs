using NUnit.Framework;

public sealed class RaceHighlightTimelineTests
{
    [Test] public void PassingMomentsKeepTheirLeadInAndRejectDuplicateJitter()
    {
        var events=new RaceHighlightTimeline();
        Assert.IsTrue(events.Add(12,0,RaceHighlightKind.Overtake,100));
        Assert.IsFalse(events.Add(13,0,RaceHighlightKind.Overtake,101));
        var clips=events.Select(20);
        Assert.AreEqual(9,clips[0].Start); Assert.AreEqual(16.5f,clips[0].End);
    }
    [Test] public void StrongEventsWinOverlappingWindowsAndEndWithinTheRace()
    {
        var events=new RaceHighlightTimeline();
        events.Add(8,1,RaceHighlightKind.Corner,20);events.Add(9,0,RaceHighlightKind.Overtake,100);
        events.Add(29,1,RaceHighlightKind.Finish,60);
        var clips=events.Select(30);
        Assert.AreEqual(2,clips.Count); Assert.AreEqual(RaceHighlightKind.Overtake,clips[0].Kind);
        Assert.AreEqual(30,clips[1].End);
        Assert.AreEqual(0,events.Select(float.NaN).Count);
    }
    [Test] public void QuietRacesHaveAFactualFallbackAndCandidateMemoryIsBounded()
    {
        var events=new RaceHighlightTimeline();
        Assert.AreEqual(0,events.Select(2).Count); Assert.AreEqual(1,events.Select(10).Count);
        for(int i=1;i<200;i++) events.Add(i*11,0,RaceHighlightKind.Corner,i);
        Assert.LessOrEqual(events.CandidateCount,96);
        Assert.LessOrEqual(events.Select(2200).Count,8);
    }
    [Test] public void QuietFootageIsNotLabeledAsABattleAndFinishClipsEndBeforeRetirement()
    {
        var events=new RaceHighlightTimeline();
        Assert.AreEqual(RaceHighlightKind.Drive,events.Select(10)[0].Kind);
        events.Add(10,0,RaceHighlightKind.Finish,80);
        Assert.AreEqual(12.5f,events.Select(30)[0].End);
    }
}
