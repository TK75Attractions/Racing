using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>Local, per-day race history. Never writes from result UI or synthetic batch playtests.</summary>
public sealed class DailyRaceRanking
{
    private sealed class LoadedDay
    {
        public DailyRankingBook book;
        public bool available = true, saved = true;
    }
    private readonly string directory;
    private readonly Func<DateTime> now;
    private readonly Dictionary<string, LoadedDay> days = new Dictionary<string, LoadedDay>();
    private readonly HashSet<int> excludedPlayers = new HashSet<int>();
    private string boardKey, raceId;
    private int goalLaps;
    private bool reversed, experience, recording;
    public DailyRaceRanking(string directory, Func<DateTime> clock = null)
    { this.directory = directory; now = clock ?? (() => DateTime.Now); }

    public void BeginRace(string key, int laps, bool reverse, bool gentle, bool allowRecording)
    {
        raceId = Guid.NewGuid().ToString("N"); boardKey = key; goalLaps = laps;
        reversed = reverse; experience = gentle; recording = allowRecording;
        excludedPlayers.Clear();
    }
    public void ExcludePlayer(int playerNumber) => excludedPlayers.Add(playerNumber);
    public bool DayChanged(DailyRankingView view) => view == null || view.day != DailyRankingBook.Day(now());

    public void RecordFinish(RaceResultRecord result)
    {
        if (result == null) return;
        result.dailyRankingExcluded |= !recording || excludedPlayers.Contains(result.playerNumber);
        if (!DailyRankingBook.Eligible(result) || result.goalLap != goalLaps || string.IsNullOrEmpty(raceId) || !string.IsNullOrEmpty(result.dailyRankingDay)) return;
        DateTime localTime = now();
        string day = DailyRankingBook.Day(localTime);
        LoadedDay history = Load(day);
        if (!history.available) return;
        int count = history.book.entries.Count;
        var entry = history.book.Record(raceId, boardKey, result, localTime);
        if (entry == null) return;
        result.dailyRankingDay = day;
        if (history.book.entries.Count != count || !history.saved) Save(day, history);
    }
    public DailyRankingView Snapshot()
    {
        string day = DailyRankingBook.Day(now());
        LoadedDay history = recording ? Load(day) : new LoadedDay { book = new DailyRankingBook { day = day } };
        var view = DailyRankingView.Build(history.book, boardKey, raceId);
        view.available = history.available; view.saved = history.saved; view.recording = recording;
        view.goalLaps = goalLaps; view.reversed = reversed; view.experience = experience;
        return view;
    }
    private string FilePath(string day) => Path.Combine(directory, day + ".json");
    private LoadedDay Load(string day)
    {
        if (days.TryGetValue(day, out var loaded)) return loaded;
        loaded = new LoadedDay { book = new DailyRankingBook { day = day } };
        string path = FilePath(day);
        try
        {
            if (File.Exists(path)) loaded.book = Read(path, day);
            else if (File.Exists(path + ".bak")) loaded.book = Read(path + ".bak", day);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
        {
            try { loaded.book = Read(path + ".bak", day); }
            catch (Exception backupError) when (backupError is IOException || backupError is UnauthorizedAccessException || backupError is ArgumentException)
            {
                loaded.available = false;
                Debug.LogWarning("Daily ranking history could not be read; originals retained: " + error.Message);
            }
        }
        days.Add(day, loaded);
        return loaded;
    }
    private static DailyRankingBook Read(string path, string day)
    {
        var book = JsonUtility.FromJson<DailyRankingBook>(File.ReadAllText(path));
        if (book == null || book.version != 1 || book.day != day || book.entries == null)
            throw new IOException("Daily ranking history has an invalid schema or date.");
        foreach (var entry in book.entries)
            if (!DailyRankingBook.Valid(entry)) throw new IOException("Daily ranking history contains an invalid entry.");
        return book;
    }
    private void Save(string day, LoadedDay history)
    {
        string path = FilePath(day), temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporary, JsonUtility.ToJson(history.book, true), Encoding.UTF8);
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
            history.saved = true;
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            history.saved = false;
            Debug.LogWarning("Daily ranking could not be saved: " + error.Message);
        }
    }

    // Same course geometry / lap count / direction / preset only. Increment rules-v1 after incompatible physics changes.
    public static string BoardKey(RaceCourse course, int laps, float sign, RacePreset preset)
    {
        var data = new StringBuilder("rules-v1|").Append(laps).Append('|').Append(sign < 0).Append('|').Append((int)preset);
        if (course != null)
        {
            data.Append('|').Append(course.gameObject.scene.path);
            for (int i = 0; i < 64; i++)
            {
                if (!course.TryGetSampleAtProgress(course.TotalLength * i / 64f, out var point)) continue;
                foreach (float value in new[] { point.position.x, point.position.y, point.position.z, point.width })
                    data.Append('|').Append(value.ToString("F3", CultureInfo.InvariantCulture));
            }
        }
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(data.ToString()))).Replace("-", "").ToLowerInvariant();
    }
}
