using NUnit.Framework;

public class DrivingTutorialProgressTests
{
    [Test]
    public void TitlePedalHoldCannotFinishAcceleration()
    {
        var lesson = new DrivingTutorialProgress();
        lesson.Tick(5f, true, 1f, 0f, 8f, 0f);
        Assert.That(lesson.CurrentStage, Is.EqualTo(DrivingTutorialProgress.Stage.Accelerate));
        lesson.Tick(.1f, true, 0f, 0f, 0f, 0f);
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
        Assert.That(lesson.CurrentStage, Is.EqualTo(DrivingTutorialProgress.Stage.Stop));
    }

    [Test]
    public void StopRequiresReleasedPedalAndSustainedLowSpeed()
    {
        var lesson = Turned();
        lesson.Tick(2f, true, 1f, 0f, 0f, 35f);
        lesson.Tick(2f, true, -1f, 0f, 0f, 35f);
        lesson.Tick(2f, true, 0f, 0f, 2f, 35f);
        Assert.That(lesson.IsComplete, Is.False);
        lesson.Tick(.4f, true, 0f, 0f, .1f, 35f);
        lesson.Tick(.1f, true, 0f, 0f, 1f, 35f);
        lesson.Tick(.4f, true, 0f, 0f, .1f, 35f);
        Assert.That(lesson.IsComplete, Is.False, "A brief stop interrupted by movement resets the hold.");
        lesson.Tick(.3f, true, 0f, 0f, .1f, 35f);
        Assert.That(lesson.IsComplete, Is.True);
    }

    [Test]
    public void DisconnectionNeverCompletesStop()
    {
        var lesson = Turned();
        lesson.Tick(.4f, true, 0f, 0f, 0f, 35f);
        lesson.Tick(10f, false, 0f, 0f, 0f, 35f);
        lesson.Tick(.3f, true, 0f, 0f, 0f, 35f);
        Assert.That(lesson.IsComplete, Is.False);
    }

    [Test]
    public void PlayersProgressIndependentlyAndCompletionIsStable()
    {
        var first = Turned();
        var second = new DrivingTutorialProgress();
        first.Tick(.7f, true, 0f, 0f, 0f, 35f);
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
        Assert.That(lesson.CurrentStage, Is.EqualTo(DrivingTutorialProgress.Stage.Stop));
    }

    private static DrivingTutorialProgress Accelerated()
    {
        var lesson = new DrivingTutorialProgress();
        lesson.Tick(.1f, true, 0f, 0f, 0f, 0f);
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
