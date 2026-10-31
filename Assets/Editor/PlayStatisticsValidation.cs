#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class PlayStatisticsValidation
{
    [MenuItem("Racing/Validation/Play Count Persistence")]
    public static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"racing-play-history-"+Guid.NewGuid().ToString("N"));
        DateTime time=new DateTime(2026,10,31,23,59,0);
        var history=new DailyPlayHistory(root,()=>time);
        try
        {
            history.BeginRace("first","course",2,false,RacePreset.Standard,true);
            Require(history.RecordPlayerStarted(1) && !history.RecordPlayerStarted(1),"One seat counts once.");
            Require(history.RecordPlayerStarted(2),"P2 counts independently.");
            string yesterday=Path.Combine(root,"2026-10-31.json");
            Require(Read(yesterday).entries.Count==2,"Unfinished players are saved at start.");
            time=time.AddMinutes(2);Require(!history.RecordPlayerStarted(1),"An ongoing play cannot count again at midnight.");
            Require(!File.Exists(Path.Combine(root,"2026-11-01.json")),"Finishing after midnight does not create a new start.");
            history.BeginRace("second","quick",1,false,RacePreset.Quick,true);Require(history.RecordPlayerStarted(1),"A new race counts on the new day.");
            string today=Path.Combine(root,"2026-11-01.json");
            var restarted=new DailyPlayHistory(root,()=>time);restarted.BeginRace("second","quick",1,false,RacePreset.Quick,true);
            Require(!restarted.RecordPlayerStarted(1) && Read(today).entries.Count==1,"Reloading the journal preserves deduplication.");
            history.BeginRace("batch","course",2,false,RacePreset.Standard,false);Require(!history.RecordPlayerStarted(1),"Synthetic runs do not write visitor records.");
            Require(Read(today).entries[0].goalLaps==1,"Board metadata keeps one-lap results separate.");
            File.Copy(today,today+".bak");File.WriteAllText(today,"corrupt");
            history.BeginRace("recovered","course",2,false,RacePreset.Standard,true);Require(history.RecordPlayerStarted(1),"A good backup can recover a corrupt main file.");
            Require(Read(today).entries.Count==2,"Recovered records remain alongside the new play.");
            File.WriteAllText(today,"broken-main");File.WriteAllText(today+".bak","broken-backup");
            history.BeginRace("unsafe","course",2,false,RacePreset.Standard,true);Require(!history.RecordPlayerStarted(2),"Unrecoverable files refuse the write.");
            Require(File.ReadAllText(today)=="broken-main"&&File.ReadAllText(today+".bak")=="broken-backup","Unrecoverable history is never overwritten.");
            Debug.Log("PLAY_STATISTICS_PERSISTENCE_PASS: starts, both seats, repeat/restart deduplication, midnight, synthetic exclusion, metadata, backup recovery and corrupt-file preservation.");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static DailyPlayBook Read(string path)=>JsonUtility.FromJson<DailyPlayBook>(File.ReadAllText(path));
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
#endif
