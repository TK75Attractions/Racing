using UnityEngine;

/// <summary>Shared observations of actual manual racers; the P2 test driver cannot set the difficulty.</summary>
[DefaultExecutionOrder(-175)]
public sealed class RaceHumanPace : MonoBehaviour
{
    private Gmanager manager;
    private readonly HumanPaceEstimate[] estimates = {new HumanPaceEstimate(),new HumanPaceEstimate()};
    private readonly bool[] observing = new bool[2];
    public static bool Enabled => RaceFeatureController.Enabled(RaceFeature.AdaptiveAiPace);
    public static RaceHumanPace For(Gmanager race)
    {
        var observer=race.GetComponent<RaceHumanPace>()??race.gameObject.AddComponent<RaceHumanPace>();
        observer.manager=race;return observer;
    }
    public static bool ManualRacer(RaceParticipant entry)
    {
        if (entry?.IsRacing != true || entry.IsCpu || entry.Body==null || entry.Car==null || entry.Car.GetComponent<PlayerAutodrive>()?.IsActive==true) return false;
        var mover=entry.Car.GetComponent<DebugMover>();var source=mover?.InputSource;
        return source!=null;
    }
    public float Pace(int index) => index>=0&&index<2 ? estimates[index].Pace : 18f;
    private void FixedUpdate()
    {
        if(manager==null)return;
        for(int i=0;i<2;i++)
        {
            var entry=manager.GetParticipant(i);
            bool active=manager.state==Gmanager.State.Game && ManualRacer(entry);
            if(!active){observing[i]=false;estimates[i].Clear();continue;}
            if(!observing[i]){estimates[i].Clear();observing[i]=true;}
            Vector3 forward=entry.Body.rotation*Vector3.forward;
            var mover=entry.Car.GetComponent<DebugMover>();var source=mover.InputSource;
            bool slowed=!source.IsConnected || source is PlayerDriveInputGate gate && (gate.IsBlocked||gate.NeedsPedalRelease) ||
                !mover.HasGroundedWheels ||
                entry.Effects!=null&&(entry.Effects.IsOiled||entry.Effects.IsSpinning||entry.Effects.IsConfused||entry.Effects.IsRocketActive);
            estimates[i].Observe(Mathf.Max(0,Vector3.Dot(entry.Body.linearVelocity,forward)),slowed,Time.fixedDeltaTime);
        }
    }
}
