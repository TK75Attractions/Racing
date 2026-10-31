using NUnit.Framework;
public sealed class DailyPlayBookTests
{
    private static DailyPlayEntry Entry(string race,int player)=>new DailyPlayEntry{raceId=race,playerNumber=player,boardKey="course",goalLaps=2,preset="Standard"};
    [Test] public void CountsBothSeatsOnceAndAReplayAsANewPlay()
    {
        var book=new DailyPlayBook{day="2026-11-01"};
        Assert.IsTrue(book.Record(Entry("first",1)));Assert.IsFalse(book.Record(Entry("first",1)));
        Assert.IsTrue(book.Record(Entry("first",2)));Assert.IsTrue(book.Record(Entry("replay",1)));
        Assert.AreEqual(3,book.entries.Count);
    }
    [Test] public void AStartDoesNotRequireAFinishRecord()
    {
        var book=new DailyPlayBook();Assert.IsTrue(book.Record(Entry("dnf-later",1)));
        Assert.AreEqual(1,book.entries.Count);
    }
    [Test] public void InvalidSeatsAndMissingIdentifiersCannotEnterTheCount()
    {
        var book=new DailyPlayBook();Assert.IsFalse(book.Record(null));
        Assert.IsFalse(book.Record(Entry("",1)));Assert.IsFalse(book.Record(Entry("race",0)));Assert.IsFalse(book.Record(Entry("race",3)));
        var noBoard=Entry("race",1);noBoard.boardKey="";Assert.IsFalse(book.Record(noBoard));
        Assert.AreEqual(0,book.entries.Count);
    }
}
