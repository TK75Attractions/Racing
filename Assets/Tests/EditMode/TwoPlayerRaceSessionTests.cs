using NUnit.Framework;

public class TwoPlayerRaceSessionTests
{
    [Test]
    public void FirstFinish_StartsFortySecondWindow()
    {
        TwoPlayerRaceSession session = CreateSession();
        RaceResultRecord first = new RaceResultRecord { totalRaceTime = 12.5f };

        RaceFinishRegistration registration = session.RegisterFinish(1, first);

        Assert.That(registration, Is.EqualTo(RaceFinishRegistration.FirstPlace));
        Assert.That(session.WaitingForSecondPlace, Is.True);
        Assert.That(session.SecondPlaceTimeRemaining, Is.EqualTo(40f));
        Assert.That(first.playerNumber, Is.EqualTo(2));
        Assert.That(first.finishPosition, Is.EqualTo(1));
    }

    [Test]
    public void SecondFinish_CompletesRaceInArrivalOrder()
    {
        TwoPlayerRaceSession session = CreateSession();
        RaceResultRecord first = new RaceResultRecord();
        RaceResultRecord second = new RaceResultRecord();
        session.RegisterFinish(1, first);

        RaceFinishRegistration registration = session.RegisterFinish(0, second);

        Assert.That(registration, Is.EqualTo(RaceFinishRegistration.RaceComplete));
        Assert.That(session.IsComplete, Is.True);
        Assert.That(second.playerNumber, Is.EqualTo(1));
        Assert.That(second.finishPosition, Is.EqualTo(2));
        Assert.That(session.Result.GetResultAtPosition(1), Is.SameAs(first));
    }

    [Test]
    public void Timeout_RequiresDnfForRemainingPlayer()
    {
        TwoPlayerRaceSession session = CreateSession();
        session.RegisterFinish(0, new RaceResultRecord());

        Assert.That(session.Tick(39.9f), Is.False);
        Assert.That(session.SecondPlaceTimeRemaining, Is.EqualTo(0.1f).Within(0.001f));
        Assert.That(session.Tick(0.1f), Is.True);
        Assert.That(session.HasTimedOut, Is.True);
        Assert.That(session.UnfinishedPlayerIndex, Is.EqualTo(1));

        RaceResultRecord dnf = new RaceResultRecord();
        Assert.That(session.RegisterDnf(1, dnf), Is.True);
        Assert.That(session.IsComplete, Is.True);
        Assert.That(dnf.didFinish, Is.False);
        Assert.That(dnf.finishPosition, Is.EqualTo(2));
    }

    [Test]
    public void DuplicateFinish_IsIgnored()
    {
        TwoPlayerRaceSession session = CreateSession();
        RaceResultRecord original = new RaceResultRecord();
        session.RegisterFinish(0, original);

        RaceFinishRegistration duplicate = session.RegisterFinish(0, new RaceResultRecord());

        Assert.That(duplicate, Is.EqualTo(RaceFinishRegistration.Ignored));
        Assert.That(session.Result.GetPlayerResult(0), Is.SameAs(original));
        Assert.That(session.UnfinishedPlayerIndex, Is.EqualTo(1));
    }

    [Test]
    public void WithdrawalBeforeFirstFinish_RemainingPlayerWinsWithoutWaiting()
    {
        TwoPlayerRaceSession session = CreateSession();
        RaceResultRecord withdrawn = new RaceResultRecord();
        Assert.That(session.RegisterWithdrawal(0, withdrawn), Is.True);
        Assert.That(session.IsComplete, Is.False);
        Assert.That(session.WaitingForSecondPlace, Is.False);
        Assert.That(session.RegisterFinish(0, new RaceResultRecord()), Is.EqualTo(RaceFinishRegistration.Ignored));
        RaceResultRecord winner = new RaceResultRecord();
        Assert.That(session.RegisterFinish(1, winner), Is.EqualTo(RaceFinishRegistration.RaceComplete));
        Assert.That(session.IsComplete, Is.True);
        Assert.That(winner.finishPosition, Is.EqualTo(1));
        Assert.That(withdrawn.didFinish, Is.False);
        Assert.That(withdrawn.finishPosition, Is.EqualTo(2));
    }

    [Test]
    public void BothWithdraw_EndWithTwoDistinctDnfRecords()
    {
        TwoPlayerRaceSession session = CreateSession();
        Assert.That(session.RegisterWithdrawal(1, new RaceResultRecord()), Is.True);
        Assert.That(session.RegisterWithdrawal(1, new RaceResultRecord()), Is.False);
        Assert.That(session.RegisterWithdrawal(0, new RaceResultRecord()), Is.True);
        Assert.That(session.IsComplete, Is.True);
        Assert.That(session.Result.GetResultAtPosition(1).didFinish, Is.False);
        Assert.That(session.Result.GetResultAtPosition(2).didFinish, Is.False);
        Assert.That(session.WaitingForSecondPlace, Is.False);
    }

    [Test]
    public void WithdrawalAfterFirstFinish_EndsWaitImmediately()
    {
        TwoPlayerRaceSession session = CreateSession();
        session.RegisterFinish(0, new RaceResultRecord());
        Assert.That(session.RegisterWithdrawal(0, new RaceResultRecord()), Is.False);
        Assert.That(session.RegisterWithdrawal(1, new RaceResultRecord()), Is.True);
        Assert.That(session.IsComplete, Is.True);
        Assert.That(session.WaitingForSecondPlace, Is.False);
        Assert.That(session.SecondPlaceTimeRemaining, Is.Zero);
    }

    [Test]
    public void DnfWithoutTimeout_IsRejected()
    {
        TwoPlayerRaceSession session = CreateSession();
        Assert.That(session.RegisterDnf(0, new RaceResultRecord()), Is.False);
    }

    private static TwoPlayerRaceSession CreateSession()
    {
        TwoPlayerRaceSession session = new TwoPlayerRaceSession(40f);
        session.Start();
        return session;
    }
}
