#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>Opt-in integration check: natural two-lap finishes, isolated history, production Gmanager callbacks.</summary>
public sealed class DailyRankingPlaytestProbe : MonoBehaviour
{
    private Gmanager manager;
    private DailyRaceRanking store;
    private string output, history;
    private bool checkedResult;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private void Update()
    {
        manager = Gmanager.Control;
        if (manager == null) return;
        if (store == null && manager.state == Gmanager.State.Game && manager.Participants.Count >= 2)
        {
            output = Environment.GetEnvironmentVariable("RACING_DAILY_PLAYTEST_OUTPUT") ?? "Logs/DailyRanking/Course";
            history = Path.Combine(output,"history-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(history);
            store = new DailyRaceRanking(history);
            store.BeginRace(DailyRaceRanking.BoardKey(manager.course,manager.GoalLap,manager.CourseProgressSign,manager.Features.Settings.ActivePreset),
                manager.GoalLap,manager.CourseProgressSign<0,manager.Features.Settings.ActivePreset==RacePreset.Experience,true);
            typeof(Gmanager).GetField("dailyRanking",Private).SetValue(manager,store);
        }
        if (store == null || checkedResult || manager.state != Gmanager.State.Result || manager.LatestSessionResult?.dailyRanking == null) return;
        checkedResult = true;
        var session=manager.LatestSessionResult;var view=session.dailyRanking;
        Require(view.available && view.saved && view.rows.Count==2,"Both natural human finishes must be saved; CPUs stay out.");
        foreach(var row in view.rows)
        {
            var result=session.GetPlayerResult(row.entry.playerNumber-1);
            Require(result.didFinish && result.completedLaps==manager.GoalLap && row.entry.milliseconds==DailyRankingBook.Milliseconds(result.totalRaceTime),"Rank the actual complete race time.");
            Require(row.entry.playNumber==1 && row.entry.raceId==view.raceId,"Both seats must belong to the same attempt.");
            store.RecordFinish(result);
        }
        Require(store.Snapshot().rows.Count==2,"Repeated result delivery must not duplicate rows.");
        var restarted=new DailyRaceRanking(history);
        restarted.BeginRace(DailyRaceRanking.BoardKey(manager.course,manager.GoalLap,manager.CourseProgressSign,manager.Features.Settings.ActivePreset),manager.GoalLap,false,false,true);
        Require(restarted.Snapshot().rows.Count==2,"Reload the saved playtest history after restart.");
        foreach(var ui in (ResultUIManager[])typeof(Gmanager).GetField("resultUIManagers",Private).GetValue(manager))
            Require(ui.CurrentSessionResult?.dailyRanking==view,"Both production result screens receive the ranking snapshot.");
        File.WriteAllText(Path.Combine(output,"daily-integration.txt"),"PASS: natural 2-lap human finishes; CPU excluded; both result screens; millisecond timings; duplicate prevention; restart persistence.\n");
        Debug.Log("DAILY_RANKING_PLAYTEST_PASS: actual finish events, P1/P2 result snapshots and persisted history.");
    }
    private static void Require(bool condition,string message)
    {if(!condition)throw new InvalidOperationException("Daily ranking integration: "+message);}
}
#endif
