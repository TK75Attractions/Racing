using System;
using System.Collections.Generic;
using System.Globalization;

[Serializable]
public sealed class DailyRankingEntry
{
    public string raceId, boardKey, finishedAt;
    public int playNumber, playerNumber, milliseconds;
}

[Serializable]
public sealed class DailyRankingBook
{
    public int version = 1;
    public string day;
    public List<DailyRankingEntry> entries = new List<DailyRankingEntry>();

    public static string Day(DateTime localTime) => localTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    // Match the production result screen's float-to-millisecond display, including boundary rounding.
    public static int Milliseconds(float seconds) => (int)Math.Round((double)seconds * 1000d, MidpointRounding.AwayFromZero);
    public static bool Eligible(RaceResultRecord result) => result != null && !result.isCpu && result.didFinish &&
        result.playerNumber >= 1 && result.playerNumber <= 2 && result.goalLap > 0 && result.completedLaps >= result.goalLap &&
        result.achievements?.usedAutodrive != true && !result.dailyRankingExcluded &&
        !float.IsNaN(result.totalRaceTime) && !float.IsInfinity(result.totalRaceTime) &&
        result.totalRaceTime >= .001f && result.totalRaceTime < 86400f;

    public DailyRankingEntry Record(string raceId, string boardKey, RaceResultRecord result, DateTime localTime)
    {
        if (day != Day(localTime) || string.IsNullOrEmpty(raceId) || string.IsNullOrEmpty(boardKey) || !Eligible(result)) return null;
        int play = 0, maximum = 0;
        foreach (var entry in entries)
        {
            if (!Valid(entry)) continue;
            maximum = Math.Max(maximum, entry.playNumber);
            if (entry.raceId != raceId) continue;
            play = entry.playNumber;
            if (entry.playerNumber == result.playerNumber) return entry;
        }
        var record = new DailyRankingEntry { raceId = raceId, boardKey = boardKey, playNumber = play > 0 ? play : maximum + 1,
            playerNumber = result.playerNumber, milliseconds = Milliseconds(result.totalRaceTime),
            finishedAt = localTime.ToString("HH:mm", CultureInfo.InvariantCulture) };
        entries.Add(record);
        return record;
    }

    public static bool Valid(DailyRankingEntry entry) => entry != null && !string.IsNullOrEmpty(entry.raceId) &&
        !string.IsNullOrEmpty(entry.boardKey) && entry.playNumber > 0 && entry.playerNumber >= 1 && entry.playerNumber <= 2 &&
        entry.milliseconds > 0 && entry.milliseconds < 86400000;
}

public sealed class DailyRankingRow
{
    public int rank;
    public DailyRankingEntry entry;
}

public sealed class DailyRankingView
{
    public string day, raceId;
    public int goalLaps;
    public bool reversed, experience, available = true, saved = true, recording = true;
    public readonly List<DailyRankingRow> rows = new List<DailyRankingRow>();
    public DailyRankingRow FindPlayer(int player) => rows.Find(row => row.entry.raceId == raceId && row.entry.playerNumber == player);

    public static DailyRankingView Build(DailyRankingBook book, string boardKey, string raceId)
    {
        var view = new DailyRankingView { day = book.day, raceId = raceId };
        var sorted = new List<DailyRankingEntry>();
        var seen = new HashSet<string>();
        foreach (var entry in book.entries)
            if (DailyRankingBook.Valid(entry) && entry.boardKey == boardKey && seen.Add(entry.raceId + ":" + entry.playerNumber)) sorted.Add(entry);
        sorted.Sort((a, b) => {
            int time = a.milliseconds.CompareTo(b.milliseconds);
            if (time != 0) return time;
            int play = a.playNumber.CompareTo(b.playNumber);
            return play != 0 ? play : a.playerNumber.CompareTo(b.playerNumber);
        });
        int rank = 0, previous = -1;
        for (int i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].milliseconds != previous) rank = i + 1;
            view.rows.Add(new DailyRankingRow { rank = rank, entry = sorted[i] });
            previous = sorted[i].milliseconds;
        }
        return view;
    }
}
