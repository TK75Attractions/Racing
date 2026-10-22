using System;
using System.Collections.Generic;

public enum RaceHighlightKind { Overtake, Corner, Finish, Battle }
public sealed class RaceHighlight
{
    public float Time, Start, End, Score;
    public int Driver;
    public RaceHighlightKind Kind;
}

/// <summary>Bounded event selection; replay windows include the lead-up and never extend beyond recorded data.</summary>
public sealed class RaceHighlightTimeline
{
    private readonly List<RaceHighlight> candidates = new List<RaceHighlight>();
    public int CandidateCount => candidates.Count;
    public bool Add(float time, int driver, RaceHighlightKind kind, float score)
    {
        if (float.IsNaN(time) || float.IsInfinity(time) || time < 1f || driver < 0 || driver > 3 ||
            float.IsNaN(score) || float.IsInfinity(score)) return false;
        foreach (RaceHighlight item in candidates)
            if (item.Driver == driver && item.Kind == kind && Math.Abs(time - item.Time) < 10f) return false;
        candidates.Add(new RaceHighlight { Time=time, Driver=driver, Kind=kind, Score=score });
        if (candidates.Count > 96)
        {
            int weakest=0;
            for (int i=1;i<candidates.Count;i++) if (candidates[i].Score<candidates[weakest].Score) weakest=i;
            candidates.RemoveAt(weakest);
        }
        return true;
    }
    public List<RaceHighlight> Select(float duration, int maximum=8)
    {
        var result=new List<RaceHighlight>();
        if (float.IsNaN(duration) || float.IsInfinity(duration) || duration<3f || maximum<=0) return result;
        var ranked=new List<RaceHighlight>(candidates);
        ranked.Sort((a,b)=>b.Score.CompareTo(a.Score));
        foreach (RaceHighlight item in ranked)
        {
            if (item.Time>duration) continue;
            float start=Math.Max(0,item.Time-3f), end=Math.Min(duration,item.Time+4.5f);
            if (end-start<3f) continue;
            bool overlaps=false;
            foreach (RaceHighlight other in result) if (Math.Min(end,other.End)-Math.Max(start,other.Start)>2f) {overlaps=true;break;}
            if (overlaps) continue;
            result.Add(new RaceHighlight {Time=item.Time,Start=start,End=end,Driver=item.Driver,Kind=item.Kind,Score=item.Score});
            if (result.Count>=maximum) break;
        }
        if (result.Count==0) result.Add(new RaceHighlight {Start=Math.Max(0,duration-8),End=duration,Time=Math.Max(0,duration-4),Driver=0,Kind=RaceHighlightKind.Battle});
        result.Sort((a,b)=>a.Start.CompareTo(b.Start));
        return result;
    }
}
