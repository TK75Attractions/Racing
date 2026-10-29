using NUnit.Framework;

public sealed class PreRaceCountdownTests
{
    [Test] public void TitleIdleAndRepeatedStartDoNotConsumeOrExtendTheDeadline()
    {
        var clock = new PreRaceCountdown();
        clock.Tick(120); Assert.AreEqual(60, clock.Remaining);
        clock.Start(); clock.Tick(30); clock.Start(); Assert.AreEqual(30, clock.Remaining);
        clock.Stop(); clock.Tick(120); Assert.AreEqual(30, clock.Remaining);
        clock.Reset(); Assert.AreEqual(60, clock.Remaining);
    }
    [Test] public void SharedDeadlineReservesCountdownAndToleratesStalledFrames()
    {
        var clock = new PreRaceCountdown(); clock.Start();
        clock.Tick(44); Assert.IsFalse(clock.MustLeavePractice);
        clock.Tick(1); Assert.IsTrue(clock.MustLeavePractice);
        clock.Tick(12); Assert.IsTrue(clock.MustFinishIntro); Assert.AreEqual(3, clock.Remaining);
        clock.Tick(100); Assert.AreEqual(0, clock.Remaining);
    }
    [Test] public void InvalidFramesCannotBreakThePreparationDeadline()
    {
        var clock = new PreRaceCountdown(); clock.Start();
        clock.Tick(float.NaN); clock.Tick(float.PositiveInfinity); clock.Tick(-1);
        Assert.AreEqual(60, clock.Remaining);
        clock.Tick(60); Assert.AreEqual(0, clock.Remaining);
    }
}
