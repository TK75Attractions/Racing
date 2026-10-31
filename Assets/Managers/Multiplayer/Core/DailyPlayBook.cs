using System;
using System.Collections.Generic;

[Serializable]
public sealed class DailyPlayEntry
{
    public string raceId, boardKey, startedAt, preset;
    public int playerNumber, goalLaps;
    public bool reversed;
}

/// <summary>One entry per human seat per race, including races that do not finish.</summary>
[Serializable]
public sealed class DailyPlayBook
{
    public int version = 1;
    public string day;
    public List<DailyPlayEntry> entries = new List<DailyPlayEntry>();
    public static bool Valid(DailyPlayEntry entry) => entry != null && !string.IsNullOrEmpty(entry.raceId) &&
        !string.IsNullOrEmpty(entry.boardKey) && entry.playerNumber >= 1 && entry.playerNumber <= 2 && entry.goalLaps > 0;
    public bool Record(DailyPlayEntry entry)
    {
        if (!Valid(entry)) return false;
        foreach (var previous in entries)
            if (previous.raceId == entry.raceId && previous.playerNumber == entry.playerNumber) return false;
        entries.Add(entry); return true;
    }
}
