using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Render-only mesh actors; never instantiate vehicle scripts or move a live Rigidbody.</summary>
public sealed class RaceReplayPlayback
{
    public const int Layer=29;
    private GameObject root;
    private readonly List<Transform[]> parts=new List<Transform[]>();
    public RaceReplayTake Take { get; private set; }
    public int ActorCount => parts.Count;
    public void Bind(RaceReplayTake take,Transform parent)
    {
        Clear();Take=take;
        root=new GameObject("Broadcast replay actors");root.transform.SetParent(parent,false);
        foreach(var car in take.Cars)
        {
            var transforms=new Transform[car.Parts.Length];
            for(int p=0;p<transforms.Length;p++)
            {
                var part=car.Parts[p];var go=new GameObject(car.Label+" replay mesh "+p);go.layer=Layer;
                go.transform.SetParent(root.transform,false);go.transform.localScale=part.Scale;
                go.AddComponent<MeshFilter>().sharedMesh=part.Mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=part.Materials;
                renderer.shadowCastingMode=ShadowCastingMode.Off;
                transforms[p]=go.transform;
            }
            parts.Add(transforms);
        }
    }
    public void SetVisible(bool visible){if(root!=null&&root.activeSelf!=visible)root.SetActive(visible);}
    public void Apply(float time,out int a,out int b,out float blend)
    {
        Take.Sample(time,out a,out b,out blend);
        for(int c=0;c<Take.Cars.Length;c++)
        {
            var car=Take.Cars[c];bool jump=Vector3.Distance(car.Roots[a].position,car.Roots[b].position)>25;
            for(int p=0;p<car.Parts.Length;p++)
            {
                Pose first=car.Poses[a*car.Parts.Length+p],second=car.Poses[b*car.Parts.Length+p];
                float t=jump?0:blend;
                bool visible=car.Visible[blend<.5f?a:b];
                if(parts[c][p].gameObject.activeSelf!=visible)parts[c][p].gameObject.SetActive(visible);
                parts[c][p].SetPositionAndRotation(Vector3.Lerp(first.position,second.position,t),Quaternion.Slerp(first.rotation,second.rotation,t));
            }
        }
    }
    public Pose Root(int index,int a,int b,float blend)
    {
        var car=Take.Cars[index];float t=Vector3.Distance(car.Roots[a].position,car.Roots[b].position)>25?0:blend;
        return new Pose(Vector3.Lerp(car.Roots[a].position,car.Roots[b].position,t),Quaternion.Slerp(car.Roots[a].rotation,car.Roots[b].rotation,t));
    }
    public void Clear()
    {
        if(root!=null){root.SetActive(false);Object.Destroy(root);}
        root=null;parts.Clear();Take=null;
    }
}
