using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class RaceReplayPart
{
    public Mesh Mesh;
    public Material[] Materials;
    public Color[] Colors;
    public Vector3 Scale;
    public int Group, Index;
    public Transform Source;
    public Renderer Renderer;
}
public sealed class RaceReplayCar
{
    public int Driver;
    public string Label;
    public Transform Source;
    public RaceReplayPart[] Parts;
    public Pose[] Roots, Poses;
    public float[] Speeds;
    public bool[] Visible;
}
public sealed class RaceReplayTake : IDisposable
{
    public const int MaximumFrames=6000, Rate=15;
    public RaceReplayCar[] Cars;
    public float[] Times;
    public int Count;
    public Vector3[] Anchors;
    public List<RaceHighlight> Clips=new List<RaceHighlight>();
    public float Duration => Count>0 ? Times[Count-1] : 0;
    public void Sample(float time, out int a, out int b, out float blend)
    {
        int lo=0,hi=Count-1;
        while(lo<hi){int mid=(lo+hi+1)/2;if(Times[mid]<=time)lo=mid;else hi=mid-1;}
        a=lo;b=Mathf.Min(a+1,Count-1);
        blend=b==a?0:Mathf.InverseLerp(Times[a],Times[b],time);
    }
    public void Dispose()
    {
        if(Cars==null)return;
        foreach(var car in Cars)if(car?.Parts!=null)foreach(var part in car.Parts)if(part?.Materials!=null)
            foreach(var material in part.Materials)if(material!=null)UnityEngine.Object.Destroy(material);
        Cars=null;
    }
}

/// <summary>Only known vehicle mesh assets are referenced; no scripts, colliders or arbitrary objects are deserialized.</summary>
public sealed class RaceReplayAssets
{
    private readonly List<MeshFilter[]> groups=new List<MeshFilter[]>();
    private readonly Dictionary<Mesh,(int group,int index)> keys=new Dictionary<Mesh,(int,int)>();
    public RaceReplayAssets(GameObject racePrefab)
    {
        Add(racePrefab);
        var catalog=CarSelectionCatalog.Load();
        Add(catalog!=null?catalog.wheelModel:null);
        if(catalog!=null)foreach(var vehicle in catalog.vehicles)Add(vehicle.model);
    }
    private void Add(GameObject prefab)
    {
        var meshes=prefab!=null?prefab.GetComponentsInChildren<MeshFilter>(true):Array.Empty<MeshFilter>();
        int group=groups.Count;groups.Add(meshes);
        for(int i=0;i<meshes.Length;i++)if(meshes[i].sharedMesh!=null&&!keys.ContainsKey(meshes[i].sharedMesh))keys.Add(meshes[i].sharedMesh,(group,i));
    }
    public RaceReplayPart Capture(MeshRenderer renderer)
    {
        var filter=renderer.GetComponent<MeshFilter>();
        if(filter==null||filter.sharedMesh==null||!keys.TryGetValue(filter.sharedMesh,out var key))return null;
        var colors=new Color[renderer.sharedMaterials.Length];
        for(int i=0;i<colors.Length;i++) colors[i]=renderer.sharedMaterials[i]!=null&&renderer.sharedMaterials[i].HasProperty("_BaseColor")?renderer.sharedMaterials[i].GetColor("_BaseColor"):Color.white;
        var part=Create(key.group,key.index,renderer.transform.lossyScale,colors,renderer.sharedMaterials,renderer.transform);
        part.Renderer=renderer;return part;
    }
    public RaceReplayPart Restore(int group,int index,Vector3 scale,Color[] colors)
    {
        if(group<0||group>=groups.Count||index<0||index>=groups[group].Length)throw new InvalidDataException("Unknown replay mesh.");
        return Create(group,index,scale,colors,groups[group][index].GetComponent<MeshRenderer>()?.sharedMaterials,null);
    }
    private RaceReplayPart Create(int group,int index,Vector3 scale,Color[] colors,Material[] originals,Transform source)
    {
        if(originals==null||originals.Length!=colors.Length)throw new InvalidDataException("Replay material count changed.");
        var materials=new Material[originals.Length];
        for(int i=0;i<materials.Length;i++)if(originals[i]!=null)
        {
            materials[i]=new Material(originals[i]);
            if(materials[i].HasProperty("_BaseColor"))materials[i].SetColor("_BaseColor",colors[i]);
            if(materials[i].HasProperty("_Color"))materials[i].SetColor("_Color",colors[i]);
        }
        return new RaceReplayPart{Mesh=groups[group][index].sharedMesh,Materials=materials,Colors=colors,Scale=scale,Group=group,Index=index,Source=source};
    }
}

public static class RaceReplayArchive
{
    private const int Magic=0x52504c32;
    public static string Save(string folder,RaceReplayTake take)
    {
        Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"race-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".rpl");
        var frames=new SortedSet<int>();
        foreach(var clip in take.Clips)
        {
            take.Sample(clip.Start,out int a,out _,out _);take.Sample(clip.End,out _,out int b,out _);
            for(int i=a;i<=b;i++)frames.Add(i);
        }
        using(var writer=new BinaryWriter(File.Create(path+".tmp")))
        {
            writer.Write(Magic);writer.Write(take.Cars.Length);writer.Write(frames.Count);writer.Write(take.Anchors.Length);
            foreach(Vector3 anchor in take.Anchors)Write(writer,anchor);
            writer.Write(take.Clips.Count);
            foreach(var clip in take.Clips){writer.Write(clip.Start);writer.Write(clip.End);writer.Write(clip.Driver);writer.Write((int)clip.Kind);}
            foreach(var car in take.Cars)
            {
                writer.Write(car.Driver);writer.Write(car.Label);writer.Write(car.Parts.Length);
                foreach(var part in car.Parts)
                {
                    writer.Write(part.Group);writer.Write(part.Index);Write(writer,part.Scale);writer.Write(part.Colors.Length);
                    foreach(Color color in part.Colors){writer.Write(color.r);writer.Write(color.g);writer.Write(color.b);writer.Write(color.a);}
                }
            }
            foreach(int frame in frames)
            {
                writer.Write(take.Times[frame]);
                foreach(var car in take.Cars)
                {
                    Write(writer,car.Roots[frame]);writer.Write(car.Speeds[frame]);writer.Write(car.Visible[frame]);
                    for(int p=0;p<car.Parts.Length;p++)Write(writer,car.Poses[frame*car.Parts.Length+p]);
                }
            }
        }
        File.Move(path+".tmp",path);
        var files=new DirectoryInfo(folder).GetFiles("race-*.rpl");Array.Sort(files,(a,b)=>b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
        for(int i=3;i<files.Length;i++)files[i].Delete();
        return path;
    }
    public static RaceReplayTake Load(string path,RaceReplayAssets assets)
    {
        if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("Replay archive too large.");
        var take=new RaceReplayTake();
        try
        {
            using(var reader=new BinaryReader(File.OpenRead(path)))
            {
                if(reader.ReadInt32()!=Magic)throw new InvalidDataException("Unsupported replay version.");
                int cars=ReadCount(reader,4), frames=ReadCount(reader,2048), anchors=ReadCount(reader,512);
                if(cars==0||frames<2)throw new InvalidDataException("Empty replay.");
                take.Count=frames;take.Times=new float[frames];take.Cars=new RaceReplayCar[cars];take.Anchors=new Vector3[anchors];
                for(int i=0;i<anchors;i++)take.Anchors[i]=ReadVector(reader);
                int clips=ReadCount(reader,8);
                if(clips==0)throw new InvalidDataException("Replay contains no clips.");
                for(int i=0;i<clips;i++)
                {
                    float start=ReadFloat(reader),end=ReadFloat(reader);int driver=reader.ReadInt32(),kind=reader.ReadInt32();
                    if(start<0||end-start<1||end-start>12||driver<0||driver>3||kind<0||kind>4)throw new InvalidDataException("Invalid replay window.");
                    take.Clips.Add(new RaceHighlight{Start=start,End=end,Driver=driver,Kind=(RaceHighlightKind)kind});
                }
                for(int i=0;i<cars;i++)
                {
                    int driver=reader.ReadInt32();string label=reader.ReadString();int parts=ReadCount(reader,64);
                    if(driver<0||driver>3||label.Length>64||parts==0)throw new InvalidDataException("Invalid replay vehicle.");
                    var car=new RaceReplayCar{Driver=driver,Label=label,Parts=new RaceReplayPart[parts],Roots=new Pose[frames],Speeds=new float[frames],Visible=new bool[frames],Poses=new Pose[frames*parts]};take.Cars[i]=car;
                    for(int p=0;p<parts;p++)
                    {
                        int group=reader.ReadInt32(),index=reader.ReadInt32();Vector3 scale=ReadVector(reader);int count=ReadCount(reader,32);var colors=new Color[count];
                        for(int c=0;c<count;c++)colors[c]=new Color(ReadFloat(reader),ReadFloat(reader),ReadFloat(reader),ReadFloat(reader));
                        car.Parts[p]=assets.Restore(group,index,scale,colors);
                    }
                }
                for(int frame=0;frame<frames;frame++)
                {
                    take.Times[frame]=ReadFloat(reader);
                    if(take.Times[frame]<0||(frame>0&&take.Times[frame]<=take.Times[frame-1]))throw new InvalidDataException("Replay time must increase.");
                    foreach(var car in take.Cars)
                    {
                        car.Roots[frame]=ReadPose(reader);car.Speeds[frame]=ReadFloat(reader);car.Visible[frame]=reader.ReadBoolean();
                        for(int p=0;p<car.Parts.Length;p++)car.Poses[frame*car.Parts.Length+p]=ReadPose(reader);
                    }
                }
                foreach(var clip in take.Clips)if(clip.Start<take.Times[0]||clip.End>take.Duration+.1f)throw new InvalidDataException("Replay window outside samples.");
            }
            return take;
        }
        catch {take.Dispose();throw;}
    }
    private static int ReadCount(BinaryReader reader,int maximum){int value=reader.ReadInt32();if(value<0||value>maximum)throw new InvalidDataException("Replay count exceeds budget.");return value;}
    private static float ReadFloat(BinaryReader reader){float x=reader.ReadSingle();if(float.IsNaN(x)||float.IsInfinity(x)||Mathf.Abs(x)>1000000)throw new InvalidDataException("Invalid replay number.");return x;}
    private static Vector3 ReadVector(BinaryReader reader)=>new Vector3(ReadFloat(reader),ReadFloat(reader),ReadFloat(reader));
    private static Pose ReadPose(BinaryReader reader){Vector3 p=ReadVector(reader);var q=new Quaternion(ReadFloat(reader),ReadFloat(reader),ReadFloat(reader),ReadFloat(reader));if(q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w<.01f)throw new InvalidDataException("Invalid replay rotation.");return new Pose(p,Quaternion.Normalize(q));}
    private static void Write(BinaryWriter writer,Vector3 p){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}
    private static void Write(BinaryWriter writer,Pose pose){Write(writer,pose.position);writer.Write(pose.rotation.x);writer.Write(pose.rotation.y);writer.Write(pose.rotation.z);writer.Write(pose.rotation.w);}
}
