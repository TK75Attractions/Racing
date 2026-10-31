using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Separate start journal: old ranking writers cannot discard these records.</summary>
public sealed class DailyPlayHistory
{
    private readonly string directory;
    private readonly Func<DateTime> now;
    private readonly HashSet<int> recorded = new HashSet<int>();
    private string raceId, boardKey, preset;
    private int laps;
    private bool reversed, enabled, highSpeed;
    public DailyPlayHistory(string directory, Func<DateTime> clock = null)
    { this.directory = directory; now = clock ?? (() => DateTime.Now); }
    public void BeginRace(string id, string board, int goalLaps, bool reverse, RacePreset mode, bool recording, bool accelerated = false)
    { raceId=id; boardKey=board; laps=goalLaps; reversed=reverse; preset=mode.ToString(); enabled=recording; highSpeed=accelerated; recorded.Clear(); }
    public bool RecordPlayerStarted(int player)
    {
        if (!enabled || player<1 || player>2 || recorded.Contains(player)) return false;
        DateTime time=now(); string day=DailyRankingBook.Day(time), path=Path.Combine(directory,day+".json");
        try
        {
            DailyPlayBook book;
            try { book=File.Exists(path)?Read(path,day):File.Exists(path+".bak")?Read(path+".bak",day):new DailyPlayBook {day=day}; }
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            { book=Read(path+".bak",day); }
            bool added=book.Record(new DailyPlayEntry { raceId=raceId,boardKey=boardKey,playerNumber=player,goalLaps=laps,
                reversed=reversed,highSpeed=highSpeed,preset=preset,startedAt=time.ToString("HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture) });
            if (added)
            {
                Directory.CreateDirectory(directory);
                string temporary=path+".tmp";File.WriteAllText(temporary,JsonUtility.ToJson(book,true),Encoding.UTF8);
                if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
            }
            recorded.Add(player); return added;
        }
        catch(Exception error) when(error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
        {
            recorded.Add(player); // Do not retry a failing disk write every frame.
            Debug.LogWarning("Play count could not be saved; original records retained: "+error.Message); return false;
        }
    }
    private static DailyPlayBook Read(string path,string day)
    {
        var book=JsonUtility.FromJson<DailyPlayBook>(File.ReadAllText(path));
        if(book==null || book.version!=1 || book.day!=day || book.entries==null)throw new IOException("Invalid play history schema.");
        foreach(var entry in book.entries)if(!DailyPlayBook.Valid(entry))throw new IOException("Invalid play history entry.");
        return book;
    }
}
