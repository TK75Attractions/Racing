using System.Collections.Generic;
using UnityEngine;

/// <summary>Tripod locations around the circuit, held through each shot while the lens pans and zooms.</summary>
public sealed class TracksideCameraRig
{
    private readonly Gmanager manager;
    private Vector3 tripod;
    private bool fixedShot, initialized;
    private float remaining;
    public int Cuts { get; private set; }
    public TracksideCameraRig(Gmanager race){manager=race;}
    public static Vector3[] BuildAnchors(RaceCourse course)
    {
        var result=new List<Vector3>();
        if(course==null||!course.HasValidPath)return result.ToArray();
        for(float progress=0;progress<course.TotalLength;progress+=70)
        {
            if(!course.TryGetSampleAtProgress(progress,out var sample))continue;
            Vector3 side=Vector3.Cross(Vector3.up,sample.forward).normalized;
            if(side.sqrMagnitude<.1f)side=sample.right;
            for(int sign=-1;sign<=1;sign+=2)result.Add(sample.position+side*sign*(sample.width*.5f+5)+Vector3.up*4.5f);
        }
        return result.ToArray();
    }
    public void Cut(){remaining=0;initialized=false;}
    private bool Occluded(Vector3 from,Vector3 to)
    {
        if(!Physics.Linecast(from,to,out RaycastHit hit,~((1<<28)|(1<<29)),QueryTriggerInteraction.Ignore))return false;
        foreach(var entry in manager.Participants)if(entry.Car!=null&&hit.transform.IsChildOf(entry.Car.transform))return false;
        return true;
    }
    private bool ClearFraming(Vector3 position,Vector3 focus,Vector3 forward)
    {
        int mask=~((1<<28)|(1<<29)|(1<<8));
        if(Physics.CheckSphere(position,2f,mask,QueryTriggerInteraction.Ignore))return false;
        Vector3 side=Vector3.Cross(Vector3.up,forward).normalized*5;
        return !Occluded(position,focus)&&!Occluded(position,focus+side+Vector3.up*2)&&!Occluded(position,focus-side+Vector3.up*2);
    }
    public void Apply(Camera camera,Pose subject,Vector3 companion,float speed,Vector3[] anchors,float dt)
    {
        Vector3 forward=subject.rotation*Vector3.forward;
        Vector3 focus=subject.position+Vector3.up*1.1f;
        float pairDistance=Vector3.Distance(subject.position,companion);
        if(pairDistance<22)focus=Vector3.Lerp(focus,companion+Vector3.up*.8f,.35f);
        remaining-=dt;
        bool tube=manager.course!=null&&manager.course.IsPointInsideTube(subject.position);
        if(remaining<=0||(fixedShot&&(Vector3.Distance(tripod,focus)<8||Vector3.Distance(tripod,focus)>125||Occluded(tripod,focus))))
        {
            float best=float.MaxValue;fixedShot=false;
            if(!tube&&anchors!=null)foreach(Vector3 anchor in anchors)
            {
                float distance=Vector3.Distance(anchor,focus);
                if(distance<18||distance>100||!ClearFraming(anchor,focus,forward))continue;
                float ahead=Vector3.Dot((anchor-subject.position).normalized,forward);
                float score=Mathf.Abs(distance-48)-ahead*18+(initialized&&Vector3.Distance(anchor,tripod)<5?35:0);
                if(score>=best)continue;best=score;tripod=anchor;fixedShot=true;
            }
            remaining=6.5f;Cuts++;initialized=false;
        }
        Vector3 position=tripod;
        if(!fixedShot||tube)
        {
            position=subject.position-forward*(tube?9:16)+Vector3.up*(tube?2.5f:8)+subject.rotation*Vector3.right*(tube?1.5f:7);
            if(Occluded(position,focus))position=subject.position+Vector3.up*(tube?3:18)-forward*4;
        }
        camera.transform.position=!initialized||fixedShot?position:Vector3.Lerp(camera.transform.position,position,1-Mathf.Exp(-dt*4));
        Quaternion rotation=Quaternion.LookRotation(focus-camera.transform.position,Vector3.up);
        camera.transform.rotation=initialized?Quaternion.Slerp(camera.transform.rotation,rotation,1-Mathf.Exp(-dt*7)):rotation;
        float spread=pairDistance<22?Mathf.Max(5,pairDistance*.5f+3):5;
        float fov=Mathf.Clamp(2*Mathf.Atan2(spread,Vector3.Distance(camera.transform.position,focus))*Mathf.Rad2Deg,12,58);
        camera.fieldOfView=initialized?Mathf.Lerp(camera.fieldOfView,fov,1-Mathf.Exp(-dt*2)):fov;
        initialized=true;
    }
}
