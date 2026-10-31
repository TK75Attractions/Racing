using System;
using NUnit.Framework;

public sealed class DailyRankingTests
{
    private readonly DateTime today = new DateTime(2026,10,21,12,0,0);
    private DailyRankingBook Book() => new DailyRankingBook { day = DailyRankingBook.Day(today) };
    private static RaceResultRecord Finish(float time, int player = 1) => new RaceResultRecord {
        playerNumber = player, didFinish = true, goalLap = 2, completedLaps = 2, totalRaceTime = time };

    [Test] public void FastestAttemptWinsAndBothPlayersOfARaceShareItsNumber()
    {
        var book = Book();
        book.Record("first","course",Finish(120),today);
        book.Record("first","course",Finish(110,2),today);
        book.Record("second","course",Finish(100),today);
        var view = DailyRankingView.Build(book,"course","first");
        Assert.AreEqual(3,view.rows.Count);
        Assert.AreEqual(2,view.rows[0].entry.playNumber);
        Assert.AreEqual(1,view.FindPlayer(2).entry.playNumber);
        Assert.AreEqual(2,view.FindPlayer(2).rank);
        Assert.AreEqual(3,view.FindPlayer(1).rank);
    }
    [Test] public void DuplicateFinishIsIdempotentButAnotherPlayOfTheSameSeatIsANewAttempt()
    {
        var book = Book();
        book.Record("race","course",Finish(100),today);
        book.Record("race","course",Finish(90),today);
        book.Record("race-again","course",Finish(95),today);
        Assert.AreEqual(2,book.entries.Count);
        Assert.AreEqual(100000,book.entries[0].milliseconds);
    }
    [Test] public void EqualDisplayedMillisecondsShareCompetitionRank()
    {
        Assert.AreEqual(100001,DailyRankingBook.Milliseconds(100.001f));
        var book = Book();
        book.Record("a","course",Finish(100.0001f),today);
        book.Record("b","course",Finish(100.0002f),today);
        book.Record("c","course",Finish(101),today);
        var view = DailyRankingView.Build(book,"course","c");
        Assert.AreEqual(1,view.rows[0].rank); Assert.AreEqual(1,view.rows[1].rank);
        Assert.AreEqual(3,view.rows[2].rank);
    }
    [Test] public void CourseRulesAndCalendarDatesAreSeparate()
    {
        var book = Book();
        book.Record("a","two-laps",Finish(100),today);
        book.Record("b","reverse",Finish(80),today);
        Assert.IsNull(book.Record("c","two-laps",Finish(90),today.AddDays(1)));
        Assert.AreEqual(1,DailyRankingView.Build(book,"two-laps","a").rows.Count);
        Assert.AreEqual(1,DailyRankingView.Build(book,"reverse","b").rows.Count);
        Assert.AreEqual("2026-10-21",DailyRankingBook.Day(today));
    }
    [Test] public void CpuDnfAutodriveAndDebugRunsCannotEnterTheBoard()
    {
        var book = Book();
        var cpu = Finish(90); cpu.isCpu = true;
        var dnf = Finish(90); dnf.didFinish = false;
        var auto = Finish(90); auto.achievements = new RaceAchievementSnapshot { usedAutodrive = true };
        var debug = Finish(90); debug.dailyRankingExcluded = true;
        var incomplete = Finish(90); incomplete.completedLaps = 1;
        foreach (var result in new[] { cpu,dnf,auto,debug,incomplete })
            Assert.IsNull(book.Record(Guid.NewGuid().ToString(),"course",result,today));
        Assert.AreEqual(0,book.entries.Count);
    }
    [Test] public void InvalidTimesAndParticipantsAreRejected()
    {
        var book = Book();
        foreach (float time in new[] { 0,-1,float.NaN,float.PositiveInfinity,86400 })
            Assert.IsNull(book.Record("race","course",Finish(time),today));
        Assert.IsNull(book.Record("race","course",Finish(100,3),today));
        Assert.IsNull(book.Record("race","course",null,today));
    }
    [Test] public void OwnRankIsAvailableBeyondTheTopFive()
    {
        var book = Book();
        for (int i = 0; i < 10; i++)book.Record("race"+i,"course",Finish(90+i),today);
        var view = DailyRankingView.Build(book,"course","race9");
        Assert.AreEqual(10,view.FindPlayer(1).rank);
        Assert.AreEqual(10,view.rows.Count);
        Assert.IsNull(view.FindPlayer(2));
    }
    [Test] public void DailyBestAndTopFifthUseTheMatchingBoardAndIncludeTiedRanks()
    {
        var book = new DailyRankingBook { day = "2026-10-31" };
        for (int i = 0; i < 10; i++) book.entries.Add(new DailyRankingEntry {
            raceId = "race" + i, boardKey = "course", playNumber = i + 1,
            playerNumber = 1, milliseconds = i < 3 ? 60000 : 60000 + i * 1000 });
        book.entries.Add(new DailyRankingEntry { raceId = "other", boardKey = "other-course", playNumber = 11, playerNumber = 1, milliseconds = 10000 });
        var view = DailyRankingView.Build(book, "course", "race0");
        Assert.AreEqual(60000, view.BestMilliseconds);
        Assert.IsTrue(view.IsTopTwentyPercent(view.rows[2]), "Equal best times share the celebration.");
        Assert.IsFalse(view.IsTopTwentyPercent(view.rows[3]));
        Assert.IsFalse(view.IsTopTwentyPercent(null));
        var empty = DailyRankingView.Build(book, "missing", "race0");
        Assert.AreEqual(0, empty.BestMilliseconds);
        Assert.IsFalse(empty.IsTopTwentyPercent(view.rows[0]));
    }
}
