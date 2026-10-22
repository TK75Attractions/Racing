using System.Collections.Generic;
using UnityEngine;

/// <summary>15 Hz visual snapshots; the driving bodies are observed and never repositioned.</summary>
public sealed class RaceReplayRecorder
{
    public readonly RaceReplayTake Take;
    private readonly Gmanager manager;
    private readonly RaceHighlightTimeline timeline=new RaceHighlightTimeline();
    private readonly int[] ranks=new int[4];
    private readonly int[] previousRanks=new int[4];
    private readonly bool[] finished=new bool[4];
    private readonly float[] discontinuities={-100,-100,-100,-100};
    private float elapsed,next;
    public RaceReplayRecorder(Gmanager race,RaceReplayAssets assets,Vector3[] anchors)
    {
        manager=race;
        var cars=new List<RaceReplayCar>();
        foreach(var entry in race.Participants)
        {
            if(entry.Car==null)continue;
            var parts=new List<RaceReplayPart>();
            foreach(var renderer in entry.Car.GetComponentsInChildren<MeshRenderer>())
            {
                if(!renderer.enabled||parts.Count>=64)continue;
                var part=assets.Capture(renderer);if(part!=null)parts.Add(part);
            }
            if(parts.Count==0)continue;
            cars.Add(new RaceReplayCar{Driver=entry.Index,Label=entry.Label,Source=entry.Car.transform,Parts=parts.ToArray(),
                Roots=new Pose[RaceReplayTake.MaximumFrames],Speeds=new float[RaceReplayTake.MaximumFrames],Visible=new bool[RaceReplayTake.MaximumFrames],Poses=new Pose[RaceReplayTake.MaximumFrames*parts.Count]});
            ranks[entry.Index]=entry.Position;
        }
        Take=new RaceReplayTake{Cars=cars.ToArray(),Times=new float[RaceReplayTake.MaximumFrames],Anchors=anchors};
        Capture();
    }
    public void Advance(float dt)
    {
        elapsed+=Mathf.Max(0,dt);
        if(elapsed<next||Take.Count>=RaceReplayTake.MaximumFrames)return;
        Capture();next=elapsed+1f/RaceReplayTake.Rate;
    }
    private void Capture()
    {
        int frame=Take.Count;
        if(frame>0&&elapsed<=Take.Times[frame-1])return;
        Take.Times[frame]=elapsed;
        System.Array.Copy(ranks,previousRanks,4);
        foreach(var car in Take.Cars)
        {
            var entry=manager.GetParticipant(car.Driver);
            if(car.Source==null)continue;
            car.Roots[frame]=new Pose(car.Source.position,car.Source.rotation);
            float speed=entry?.Body!=null?entry.Body.linearVelocity.magnitude:0;car.Speeds[frame]=speed;
            for(int p=0;p<car.Parts.Length;p++)
            {
                var source=car.Parts[p].Source;
                car.Poses[frame*car.Parts.Length+p]=source!=null?new Pose(source.position,source.rotation):car.Roots[frame];
                var renderer=car.Parts[p].Renderer;
                if(renderer!=null&&renderer.enabled&&renderer.gameObject.activeInHierarchy)car.Visible[frame]=true;
            }
            if(frame>0&&Vector3.Distance(car.Roots[frame-1].position,car.Roots[frame].position)>30)discontinuities[car.Driver]=elapsed;
            if(entry==null)continue;
            if(entry.Result?.didFinish==true&&!finished[car.Driver])
            {timeline.Add(elapsed,car.Driver,RaceHighlightKind.Finish,entry.IsCpu?45:85);finished[car.Driver]=true;}
            if(entry.IsRacing&&frame>15&&elapsed-discontinuities[car.Driver]>3&&speed>12)
            {
                if(entry.IsRacing&&entry.Position<previousRanks[car.Driver])
                    foreach(var other in manager.Participants)
                        if(other.Index!=entry.Index&&other.IsRacing&&previousRanks[other.Index]<previousRanks[car.Driver]&&
                            other.Position>entry.Position&&Vector3.Distance(other.Body.position,entry.Body.position)<28)
                        {timeline.Add(elapsed,car.Driver,RaceHighlightKind.Overtake,entry.IsCpu?110:150);break;}
                int before=Mathf.Max(0,frame-10);
                float turn=Vector3.Angle(Vector3.ProjectOnPlane(car.Roots[before].rotation*Vector3.forward,Vector3.up),Vector3.ProjectOnPlane(car.Source.forward,Vector3.up));
                if(turn>18&&turn<100)timeline.Add(elapsed,car.Driver,RaceHighlightKind.Corner,35+turn+(entry.IsCpu?0:20));
                foreach(var other in manager.Participants)
                    if(other.Index!=entry.Index&&other.Body!=null&&Vector3.Distance(entry.Body.position,other.Body.position)<11)
                    {timeline.Add(elapsed,car.Driver,RaceHighlightKind.Battle,entry.IsCpu?35:65);break;}
            }
            ranks[car.Driver]=entry.Position;
        }
        Take.Count++;
    }
    public RaceReplayTake Finish()
    {
        foreach(var entry in manager.Participants)if(entry.Result?.didFinish==true&&!finished[entry.Index])
            timeline.Add(Take.Duration,entry.Index,RaceHighlightKind.Finish,entry.IsCpu?45:85);
        Take.Clips=timeline.Select(Take.Duration);
        foreach(var clip in Take.Clips)
        {
            if(clip.Kind==RaceHighlightKind.Drive)
            {
                float best=-1;
                foreach(var car in Take.Cars)if(car.Visible[Take.Count-1]&&car.Speeds[Take.Count-1]>best)
                {best=car.Speeds[Take.Count-1];clip.Driver=car.Driver;}
            }
            var focus=System.Array.Find(Take.Cars,car=>car.Driver==clip.Driver);
            if(focus!=null)for(int frame=0;frame<Take.Count;frame++)
                if(Take.Times[frame]>clip.Time&&!focus.Visible[frame]){clip.End=Mathf.Min(clip.End,Take.Times[frame]);break;}
        }
        return Take;
    }
}
