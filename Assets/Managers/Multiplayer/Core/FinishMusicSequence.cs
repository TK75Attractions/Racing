using System;

/// <summary>One complete finish cue per player; watching/results never restart it.</summary>
public sealed class FinishMusicSequence
{
    public enum Track { Race, Finish, Results }
    public bool HasStarted { get; private set; }
    public double EndsAt { get; private set; }
    public void Begin(double scheduledStart, double duration)
    {
        if (HasStarted) return;
        HasStarted=true;EndsAt=scheduledStart+Math.Max(0,duration);
    }
    public Track Select(bool finished, bool otherRacing, bool raceEnded, double dspTime)
    {
        if (finished)
        {
            if (!HasStarted || dspTime<EndsAt) return Track.Finish;
            return otherRacing ? Track.Race : Track.Results;
        }
        return raceEnded ? Track.Results : Track.Race;
    }
    public void Reset() { HasStarted=false;EndsAt=0; }
}
