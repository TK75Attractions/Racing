using NUnit.Framework;

public class RaceParticipantSessionTests
{
    private static RaceParticipantSession Create(float maximum = 300) => new RaceParticipantSession(2, 2, 40, maximum);
    [Test] public void CpuFinishesDoNotStartHumanTimeout()
    {
        var s = Create();
        Assert.That(s.RegisterFinish(2, new RaceResultRecord()), Is.EqualTo(RaceFinishRegistration.Recorded));
        s.RegisterFinish(3, new RaceResultRecord());
        Assert.That(s.WaitingForSecondPlace, Is.False);
        Assert.That(s.IsComplete, Is.False);
        Assert.That(s.Tick(50), Is.False);
        s.RegisterFinish(1, new RaceResultRecord());
        Assert.That(s.SecondPlaceTimeRemaining, Is.EqualTo(40));
        Assert.That(s.RegisterFinish(0, new RaceResultRecord()), Is.EqualTo(RaceFinishRegistration.RaceComplete));
        Assert.That(s.Result.GetPlayerResult(0).finishPosition, Is.EqualTo(4));
        Assert.That(s.Result.GetPlayerResult(2).isCpu, Is.True);
    }
    [Test] public void HumanCompletionDoesNotWaitForCpu()
    {
        var s = Create(); s.RegisterFinish(0, new RaceResultRecord());
        s.RegisterFinish(1, new RaceResultRecord());
        Assert.That(s.IsComplete, Is.True);
        Assert.That(s.CloseParticipant(3, new RaceResultRecord()), Is.True);
        Assert.That(s.CloseParticipant(2, new RaceResultRecord()), Is.True);
        Assert.That(s.Result.GetResultAtPosition(3).playerNumber, Is.EqualTo(4));
        Assert.That(s.Result.GetResultAtPosition(4).didFinish, Is.False);
    }
    [Test] public void OverallLimitAppliesWhenNobodyFinishes()
    {
        var s = Create(60); Assert.That(s.Tick(59), Is.False); Assert.That(s.Tick(1), Is.True);
        for (int i = 0; i < 4; i++) Assert.That(s.CloseParticipant(i, new RaceResultRecord()), Is.True);
        Assert.That(s.IsComplete, Is.True);
        Assert.That(s.RegisterFinish(0, new RaceResultRecord()), Is.EqualTo(RaceFinishRegistration.Ignored));
    }
    [Test] public void WithdrawalCannotStealAFinishRank()
    {
        var s = Create(); s.RegisterWithdrawal(0, new RaceResultRecord());
        s.RegisterFinish(2, new RaceResultRecord()); s.RegisterFinish(1, new RaceResultRecord());
        s.CloseParticipant(3, new RaceResultRecord());
        Assert.That(s.Result.GetResultAtPosition(1).playerNumber, Is.EqualTo(3));
        Assert.That(s.Result.GetResultAtPosition(2).playerNumber, Is.EqualTo(2));
        Assert.That(s.Result.GetResultAtPosition(3).didFinish, Is.False);
        Assert.That(s.Result.GetResultAtPosition(4).didFinish, Is.False);
    }
    [Test] public void GracePeriodRejectsLateFinishAndDuplicateWithdrawal()
    {
        var s = Create(); s.RegisterFinish(2, new RaceResultRecord()); s.RegisterFinish(0, new RaceResultRecord());
        Assert.That(s.Tick(40), Is.True);
        Assert.That(s.RegisterFinish(1, new RaceResultRecord()), Is.EqualTo(RaceFinishRegistration.Ignored));
        Assert.That(s.RegisterDnf(1, new RaceResultRecord()), Is.True);
        Assert.That(s.RegisterWithdrawal(1, new RaceResultRecord()), Is.False);
    }
}
