using NUnit.Framework;

public sealed class DriverNoticeQueueTests
{
    [Test] public void SafetyInterruptsSuccessImmediatelyAndDoesNotReplayIt()
    {
        var queue = new DriverNoticeQueue();
        queue.Submit("landing", "着地成功", DriverNoticePriority.Success, 0);
        Assert.That(queue.Tick(0).Key, Is.EqualTo("landing"));
        queue.Submit("jump", "ジャンプ", DriverNoticePriority.Safety, .05f);
        Assert.That(queue.Tick(.05f).Key, Is.EqualTo("jump"));
        Assert.That(queue.Tick(2), Is.Null);
    }

    [Test] public void GuidanceDoesNotReplaceACardBeforeItCanBeRead()
    {
        var queue = new DriverNoticeQueue();
        queue.Submit("success", "成功", DriverNoticePriority.Success, 0);
        queue.Tick(0);
        queue.Submit("guide", "曲がろう", DriverNoticePriority.Guidance, .1f);
        Assert.That(queue.Tick(.1f).Key, Is.EqualTo("success"));
        Assert.That(queue.Tick(1.6f).Key, Is.EqualTo("guide"));
    }

    [Test] public void UpdatedCurrentSourceNeverReplaysAnOlderMessage()
    {
        var queue = new DriverNoticeQueue();
        queue.Submit("input", "接続待ち", DriverNoticePriority.Critical, 0, condition:true);
        queue.Tick(0);
        queue.Submit("input", "ペダルを離そう", DriverNoticePriority.Critical, .1f, condition:true);
        Assert.That(queue.Tick(.1f).Text, Is.EqualTo("ペダルを離そう"));
        queue.Release("input");
        Assert.That(queue.Tick(.11f), Is.Null);
        Assert.That(queue.Tick(1), Is.Null);
    }

    [Test] public void ResolvedCriticalConditionDisappearsImmediately()
    {
        var queue = new DriverNoticeQueue();
        queue.Submit("input", "ペダルを離そう", DriverNoticePriority.Critical, 0, condition:true);
        queue.Tick(0);
        queue.Release("input");
        Assert.That(queue.Tick(.01f), Is.Null);
    }

    [Test] public void BriefConditionDropoutDoesNotBlinkAndEventuallyExpires()
    {
        var queue = new DriverNoticeQueue();
        queue.Submit("coach", "曲がろう", DriverNoticePriority.Guidance, 0, condition:true);
        queue.Tick(0);queue.Release("coach");
        Assert.That(queue.Tick(.1f).Key, Is.EqualTo("coach"));
        queue.Submit("coach", "曲がろう", DriverNoticePriority.Guidance, .2f, condition:true);
        Assert.That(queue.Tick(.2f).ShownAt, Is.Zero);
        queue.Release("coach");
        Assert.That(queue.Tick(1.1f), Is.Null);
    }

    [Test] public void StalePendingConditionsNeverAppearAfterACriticalWarning()
    {
        var queue = new DriverNoticeQueue();
        queue.Submit("input", "接続待ち", DriverNoticePriority.Critical, 0, condition:true);
        queue.Submit("success", "成功", DriverNoticePriority.Success, 0, condition:true);
        queue.Tick(0);queue.Release("input");
        Assert.That(queue.Tick(.2f), Is.Null);
    }

    [Test] public void DuplicateEventsAreConsumedOnceAndClearResetsTheRace()
    {
        var queue = new DriverNoticeQueue();
        queue.Submit("landing", "着地", DriverNoticePriority.Success, 0);queue.Tick(0);
        queue.Submit("landing", "着地", DriverNoticePriority.Success, .2f);
        Assert.That(queue.Tick(1.6f), Is.Null);
        queue.Submit("landing", "着地", DriverNoticePriority.Success, 1.7f);
        Assert.That(queue.Tick(1.7f), Is.Null);
        queue.Clear();queue.Submit("landing", "着地", DriverNoticePriority.Success, 1.8f);
        Assert.That(queue.Tick(1.8f).Text, Is.EqualTo("着地"));
    }
}
