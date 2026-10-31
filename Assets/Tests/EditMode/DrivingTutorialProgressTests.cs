using NUnit.Framework;

public class DrivingTutorialProgressTests
{
    [Test]
    public void TimeoutEndsPracticeEvenWhenDisconnectedWithoutCompletingLessons()
    {
        var lesson = new DrivingTutorialProgress(5f);
        lesson.Tick(4f, false, 0f, 0f, 0f, 0f);
        Assert.That(lesson.TimeRemaining, Is.EqualTo(1f));
        Assert.That(lesson.IsFinished, Is.False);
        lesson.Tick(1f, false, 0f, 0f, 0f, 0f);
        Assert.That(lesson.HasTimedOut, Is.True);
        Assert.That(lesson.IsFinished, Is.True);
        Assert.That(lesson.IsComplete, Is.False);
        lesson.Tick(10f, true, 1f, 10f, 10f, 90f);
        Assert.That(lesson.CurrentStage, Is.EqualTo(DrivingTutorialProgress.Stage.Accelerate));
        Assert.That(lesson.TimeRemaining, Is.Zero);
    }

    [Test]
    public void EarlyCompletionStopsTimerAndRecoveryDoesNotResetDeadline()
    {
        var lesson = Turned();
        float beforeRecovery = lesson.TimeRemaining;
        lesson.ResetCurrentAttempt();
        Assert.That(lesson.TimeRemaining, Is.EqualTo(beforeRecovery));
        Assert.That(lesson.CollectItem(true), Is.True);
        float atCompletion = lesson.TimeRemaining;
        lesson.Tick(100f, false, 0f, 0f, 0f, 0f);
        Assert.That(lesson.IsComplete, Is.True);
        Assert.That(lesson.HasTimedOut, Is.False);
        Assert.That(lesson.TimeRemaining, Is.EqualTo(atCompletion));
    }

    [Test]
    public void HeldPedalStartsPracticeButStationaryCarCannotFinishAcceleration()
    {
        var lesson = new DrivingTutorialProgress();
        lesson.Tick(5f, true, 1f, 0f, 0f, 0f);
        Assert.That(lesson.CurrentStage, Is.EqualTo(DrivingTutorialProgress.Stage.Accelerate));
        lesson.Tick(1f, true, 1f, 0f, 0f, 0f);
        Assert.That(lesson.CurrentStage, Is.EqualTo(DrivingTutorialProgress.Stage.Accelerate), "A stationary car cannot pass.");
        lesson.Tick(.7f, true, 1f, 0f, 4f, 0f);
        Assert.That(lesson.CurrentStage, Is.EqualTo(DrivingTutorialProgress.Stage.Steer));
    }

    [Test]
    public void SteeringRequiresRealHeadingChangeWhileMoving()
    {
        var lesson = Accelerated();
        lesson.Tick(2f, true, 1f, 10f, 4f, 0f);
        Assert.That(lesson.CurrentStage, Is.EqualTo(DrivingTutorialProgress.Stage.Steer));
        lesson.Tick(2f, true, 1f, 10f, 0f, 35f);
        Assert.That(lesson.CurrentStage, Is.EqualTo(DrivingTutorialProgress.Stage.Steer));
        lesson.Tick(.7f, true, 1f, -10f, 4f, -35f);
        Assert.That(lesson.CurrentStage, Is.EqualTo(DrivingTutorialProgress.Stage.CollectItem));
    }

    [Test]
    public void ItemRequiresContactAfterSteeringAndWhileConnected()
    {
        var lesson = Accelerated();
        Assert.That(lesson.CollectItem(true), Is.False);
        lesson.Tick(.7f, true, 1f, 10f, 4f, 35f);
        lesson.Tick(2f, true, 0f, 0f, 0f, 35f);
        Assert.That(lesson.IsComplete, Is.False, "Stopping is no longer a lesson.");
        Assert.That(lesson.CollectItem(false), Is.False);
        Assert.That(lesson.CollectItem(true), Is.True);
        Assert.That(lesson.CollectItem(true), Is.False, "Multiple colliders cannot complete it twice.");
        Assert.That(lesson.IsComplete, Is.True);
    }

    [Test]
    public void TimedOutPracticeCannotCollectAnItem()
    {
        var lesson = Turned();
        lesson.Tick(100f, true, 1f, 0f, 4f, 35f);
        Assert.That(lesson.CollectItem(true), Is.False);
        Assert.That(lesson.IsComplete, Is.False);
    }

    [Test]
    public void PlayersProgressIndependentlyAndCompletionIsStable()
    {
        var first = Turned();
        var second = new DrivingTutorialProgress();
        first.CollectItem(true);
        first.Tick(10f, true, 1f, 10f, 10f, 90f);
        Assert.That(first.IsComplete, Is.True);
        Assert.That(second.CurrentStage, Is.EqualTo(DrivingTutorialProgress.Stage.Accelerate));
    }

    [Test]
    public void RecoveryKeepsCompletedLessonsButClearsPartialHold()
    {
        var lesson = Accelerated();
        lesson.Tick(.4f, true, 1f, 10f, 4f, 35f);
        lesson.ResetCurrentAttempt();
        lesson.Tick(.3f, true, 1f, 10f, 4f, 35f);
        Assert.That(lesson.CurrentStage, Is.EqualTo(DrivingTutorialProgress.Stage.Steer));
        lesson.Tick(.4f, true, 1f, 10f, 4f, 35f);
        Assert.That(lesson.CurrentStage, Is.EqualTo(DrivingTutorialProgress.Stage.CollectItem));
    }

    [Test]
    public void HeldPedalCanAccelerateImmediatelyButInvalidOrDisconnectedInputCannot()
    {
        var held = new DrivingTutorialProgress();
        held.Tick(.7f, true, 1f, 0f, 4f, 0f);
        Assert.AreEqual(DrivingTutorialProgress.Stage.Steer, held.CurrentStage);
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var lesson = new DrivingTutorialProgress();
            lesson.Tick(1f, true, invalid, 0f, 4f, 0f);
            Assert.AreEqual(DrivingTutorialProgress.Stage.Accelerate, lesson.CurrentStage);
        }
        var disconnected = new DrivingTutorialProgress();
        disconnected.Tick(1f, false, 1f, 0f, 4f, 0f);
        Assert.AreEqual(DrivingTutorialProgress.Stage.Accelerate, disconnected.CurrentStage);
    }

    private static DrivingTutorialProgress Accelerated()
    {
        var lesson = new DrivingTutorialProgress();
        lesson.Tick(.7f, true, 1f, 0f, 4f, 0f);
        return lesson;
    }

    private static DrivingTutorialProgress Turned()
    {
        var lesson = Accelerated();
        lesson.Tick(.7f, true, 1f, 10f, 4f, 35f);
        return lesson;
    }
}
