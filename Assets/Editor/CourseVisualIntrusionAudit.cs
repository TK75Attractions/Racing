// Unity -batchmode -nographics -quit -projectPath <project> -executeMethod CourseVisualIntrusionAudit.Run
// Read-only audit: no mesh baking, scene saving, Play Mode, or graphics device is required.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class CourseVisualIntrusionAudit
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string ModelPath = "Assets/Scenes/1016こうしゃ.fbx";
    const string Output = "Artifacts/CourseVisualAudit";
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Affine scalar fields use double precision, including plane offsets at large world coordinates.
    struct Field
    {
        public double x, y, z, d;
        public Field(double x, double y, double z, double d) { this.x=x; this.y=y; this.z=z; this.d=d; }
        public double At(Vector3 p) => x*p.x+y*p.y+z*p.z+d;
        public static Field operator +(Field a, Field b) => new Field(a.x+b.x,a.y+b.y,a.z+b.z,a.d+b.d);
        public static Field operator -(Field a, Field b) => a + b * -1;
        public static Field operator *(Field a, double k) => new Field(a.x*k,a.y*k,a.z*k,a.d*k);
        public static Field Constant(double d) => new Field(0,0,0,d);
    }
    sealed class Volume
    {
        public Field[] planes;
        public Field height, lateral, progress;
        public Bounds bounds;
        public int zone, side;
    }
    sealed class Segment
    {
        public RaceCourse.CourseSample a, b;
        public float start, end;
        public Bounds bounds, moveBounds;
        public Field[][] nearPlanes, floorPlanes;
    }
    sealed class Index<T>
    {
        readonly Dictionary<Vector2Int,List<T>> cells = new Dictionary<Vector2Int,List<T>>();
        const float Cell = 4;
        public void Add(T value, Bounds b)
        {
            for (int x=Mathf.FloorToInt(b.min.x/Cell); x<=Mathf.FloorToInt(b.max.x/Cell); x++)
                for (int z=Mathf.FloorToInt(b.min.z/Cell); z<=Mathf.FloorToInt(b.max.z/Cell); z++)
                {
                    var key=new Vector2Int(x,z);
                    if (!cells.TryGetValue(key,out var list)) cells.Add(key,list=new List<T>());
                    list.Add(value);
                }
        }
        public void Query(Bounds b, HashSet<T> result)
        {
            result.Clear();
            for (int x=Mathf.FloorToInt(b.min.x/Cell); x<=Mathf.FloorToInt(b.max.x/Cell); x++)
                for (int z=Mathf.FloorToInt(b.min.z/Cell); z<=Mathf.FloorToInt(b.max.z/Cell); z++)
                    if (cells.TryGetValue(new Vector2Int(x,z),out var list))
                        foreach (T item in list) result.Add(item);
        }
    }
    sealed class Row
    {
        public string path, group, evidence;
        public int zone, triangles;
        public float depth, pmin=float.PositiveInfinity, pmax=float.NegativeInfinity;
        public float dmin=float.PositiveInfinity, dmax=float.NegativeInfinity, representativeProgress;
        public Vector3 point, outward;
        public int side;
    }
    sealed class Group
    {
        public Transform transform;
        public Bounds bounds;
        public bool hasBounds;
        public string path;
        public readonly List<Row> rows=new List<Row>();
        public Vector3 shift;
        public bool solved;
        public string overlaps;
    }
    sealed class Audit
    {
        public RaceCourse course;
        public MeshCollider tube;
        public float length, goal, direction=1;
        public readonly List<Volume> volumes=new List<Volume>();
        public readonly List<Segment> segments=new List<Segment>();
        public readonly Index<Volume> index=new Index<Volume>();
        public readonly Index<Segment> near=new Index<Segment>();
        public readonly Dictionary<Transform,Group> groups=new Dictionary<Transform,Group>();
        public readonly List<Row> rows=new List<Row>();
        public readonly List<string> notes=new List<string>();
        public readonly HashSet<Volume> candidates=new HashSet<Volume>();
        public readonly HashSet<Segment> nearCandidates=new HashSet<Segment>();
        public readonly List<Vector3> clipA=new List<Vector3>(16), clipB=new List<Vector3>(16);
        public long affectedTriangles, exportedTriangles;
        public bool exportGeometry;
        public int affectedObjects;
        public float Distance(float p) => Mathf.Repeat(direction*(p-goal),length);
    }

    [MenuItem("Racing/Audit Course Visual Intrusions")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before auditing.");
        // Reuse an open scene, including unsaved edits. Otherwise load additively and close without saving.
        Scene scene=SceneManager.GetSceneByPath(ScenePath);
        bool opened=!scene.IsValid() || !scene.isLoaded;
        Scene previous=SceneManager.GetActiveScene();
        bool backfaces=Physics.queriesHitBackfaces;
        try
        {
            if (opened) scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            var audit=new Audit();
            audit.exportGeometry=Environment.GetEnvironmentVariable("COURSE_VISUAL_EXPORT_OBJ")!="0";
            var roots=scene.GetRootGameObjects();
            audit.course=roots.SelectMany(r=>r.GetComponentsInChildren<RaceCourse>(true)).Single();
            audit.course.RebuildCache();
            if (!audit.course.HasValidPath || audit.course.TotalLength<=0)
                throw new InvalidOperationException("SampleScene requires a valid rounded course.");
            audit.length=audit.course.TotalLength;
            audit.tube=new SerializedObject(audit.course).FindProperty("road.tubeInterior").objectReferenceValue as MeshCollider;
            if (audit.tube==null) throw new InvalidOperationException("The authored tube collider is missing.");
            ResolveStart(audit,roots);
            var schools=roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true))
                .Where(t=>AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(t))==ModelPath &&
                    (t.parent==null || AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(t.parent))!=ModelPath))
                .ToArray();
            if (schools.Length==0) throw new InvalidOperationException("No instance of "+ModelPath+" in SampleScene.");
            Physics.queriesHitBackfaces=true;
            Physics.SyncTransforms();
            BuildVolumes(audit);
            Directory.CreateDirectory(Output);
            using (var obj=audit.exportGeometry
                ? new StreamWriter(Path.Combine(Output,"near-road-renderers.obj"),false,new UTF8Encoding(false))
                : new StreamWriter(Stream.Null))
            {
                obj.WriteLine("# World coordinates, Unity axes/metres; source triangles (not clipped). g = hierarchy path.");
                long vertex=1;
                foreach (Transform school in schools.OrderBy(PathOf,StringComparer.Ordinal))
                {
                    Renderer[] renderers=school.GetComponentsInChildren<Renderer>(true);
                    // Include the complete visual bounds of each eligible movement group.
                    foreach (Renderer renderer in renderers)
                    {
                        if (!Visible(renderer) || Excluded(renderer.transform,school)) continue;
                        Group group=GetGroup(audit,renderer.transform,school);
                        if (!group.hasBounds) { group.bounds=renderer.bounds; group.hasBounds=true; }
                        else group.bounds.Encapsulate(renderer.bounds);
                    }
                    foreach (Renderer renderer in renderers.OrderBy(r=>PathOf(r.transform),StringComparer.Ordinal))
                    {
                        if (!Visible(renderer) || Excluded(renderer.transform,school)) continue;
                        if (renderer is SkinnedMeshRenderer) { AuditBounds(audit,renderer,school); continue; }
                        if (!(renderer is MeshRenderer)) continue;
                        MeshFilter filter=renderer.GetComponent<MeshFilter>();
                        if (filter==null || filter.sharedMesh==null) continue;
                        AuditMesh(audit,renderer,filter.sharedMesh,school,obj,ref vertex);
                    }
                }
            }
            EstimateMoves(audit);
            if (!audit.exportGeometry) audit.notes.Add("OBJ出力はCOURSE_VISUAL_EXPORT_OBJ=0で省略。侵入判定は全対象三角形について実施。");
            WriteReports(audit);
            Debug.Log("COURSE_VISUAL_AUDIT_DONE "+audit.affectedObjects+" "+audit.affectedTriangles);
        }
        finally
        {
            Physics.queriesHitBackfaces=backfaces;
            if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene,true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    static bool Visible(Renderer r) => r.enabled && r.gameObject.activeInHierarchy;
    static string PathOf(Transform t) => t.parent==null ? t.name : PathOf(t.parent)+"/"+t.name;
    static bool Excluded(Transform t, Transform school)
    {
        for (Transform p=t; p!=null && p!=school; p=p.parent)
        {
            string n=p.name;
            if (n=="Plane" || n=="BézierCurve.001" || n=="BﾃｩzierCurve.001" ||
                n==RaceCourseBarriers.RootName || n.StartsWith("Course Occlusion ",StringComparison.Ordinal) ||
                p.GetComponent<RaceCourse>()!=null || p.GetComponent<RaceBarrierPath>()!=null ||
                p.GetComponent<Rigidbody>()!=null || p.GetComponent<RaceItemPickup>()!=null ||
                p.GetComponent<GoalSensor>()!=null || n.IndexOf("NeonRaceMarkers",StringComparison.OrdinalIgnoreCase)>=0 ||
                n.IndexOf("Lantern",StringComparison.OrdinalIgnoreCase)>=0 || n.IndexOf("灯籠",StringComparison.Ordinal)>=0 ||
                n.IndexOf("GoalGate",StringComparison.OrdinalIgnoreCase)>=0) return true;
        }
        return false;
    }
    static bool Floor(Transform t,Transform school)
    {
        for (;t!=null && t!=school;t=t.parent)
            if (t.name=="Plane.012" || t.name=="Plane.009" || t.name=="Cube.001") return true;
        return false;
    }
    static Group GetGroup(Audit a,Transform t,Transform school)
    {
        // Highest authored branch below the FBX root is a stable, practical movement unit.
        while (t.parent!=null && t.parent!=school) t=t.parent;
        if (!a.groups.TryGetValue(t,out var g))
            a.groups.Add(t,g=new Group { transform=t,path=PathOf(t) });
        return g;
    }
    static void ResolveStart(Audit a,GameObject[] roots)
    {
        var lap=roots.SelectMany(r=>r.GetComponentsInChildren<LapManager>(true)).Single();
        var serialized=new SerializedObject(lap);
        var checkpoints=serialized.FindProperty("checkpoints");
        var configured=new List<CheckpointSensor>();
        if (serialized.FindProperty("autoFindCheckpoints").boolValue)
            configured.AddRange(roots.SelectMany(r=>r.GetComponentsInChildren<CheckpointSensor>()).Where(c=>c.gameObject.activeInHierarchy));
        else
            for (int i=0;i<checkpoints.arraySize;i++)
                if (checkpoints.GetArrayElementAtIndex(i).objectReferenceValue is CheckpointSensor sensor) configured.Add(sensor);
        var checkpoint=configured.OrderBy(c=>c.CheckpointIndex).FirstOrDefault();
        if (checkpoint==null)
        {
            var manager=roots.SelectMany(r=>r.GetComponentsInChildren<Gmanager>(true)).FirstOrDefault();
            if (manager!=null) checkpoint=new SerializedObject(manager).FindProperty("startCheckpoint").objectReferenceValue as CheckpointSensor;
        }
        if (checkpoint==null) throw new InvalidOperationException("Cannot resolve LapManager's start direction.");
        var goal=roots.SelectMany(r=>r.GetComponentsInChildren<GoalSensor>(true)).Single(g=>g.gameObject.activeInHierarchy);
        a.goal=a.course.GetProgressDistance(goal.transform.position);
        if (a.course.TryGetNearestCenterLineDirection(checkpoint.transform.position,out var forward))
            a.direction=Vector3.Dot(Vector3.ProjectOnPlane(checkpoint.transform.forward,Vector3.up).normalized,forward)<0 ? -1 : 1;
        a.notes.Add("LapManager と同じゴール基準: "+PathOf(goal.transform)+", raw progress="+F(a.goal)+", direction="+a.direction);
    }

    static Vector3[][] ReadProfiles()
    {
        var go=new GameObject("Visual audit profile lookup") { hideFlags=HideFlags.HideAndDontSave };
        go.SetActive(false);
        try
        {
            var barrier=go.AddComponent<RaceBarrierPath>();
            var settings=new SerializedObject(barrier);
            int sections=settings.FindProperty("wallSectionSegments").intValue;
            var defaults=new RaceBarrierPath.ControlPoint();
            Type type=typeof(RaceBarrierPath).GetNestedType("Sample",BindingFlags.NonPublic);
            MethodInfo method=typeof(RaceBarrierPath).GetMethod("GetSectionPoint",Private);
            if (type==null || method==null) throw new InvalidOperationException("Barrier section API changed.");
            object sample=Activator.CreateInstance(type);
            type.GetField("height").SetValue(sample,Mathf.Max(defaults.wallHeight,settings.FindProperty("minimumWallHeight").floatValue));
            type.GetField("outwardFlare").SetValue(sample,defaults.outwardFlare);
            var profiles=new Vector3[2][];
            for (int weight=0;weight<=1;weight++)
            {
                type.GetField("uprightWeight").SetValue(sample,(float)weight);
                profiles[weight]=new Vector3[sections*2+1];
                for (int i=0;i<=sections*2;i++)
                    profiles[weight][i]=(Vector3)method.Invoke(barrier,new object[] {sample,Vector3.right,i});
            }
            return profiles; // GetSectionOffset linearly blends these drive surfaces, excluding collider thickness.
        }
        finally { Object.DestroyImmediate(go); }
    }
    static Vector3 Horizontal(Vector3 v) => new Vector3(v.x,0,v.z).normalized;
    static Bounds TriangleBounds(Vector3 a,Vector3 b,Vector3 c)
    { var bounds=new Bounds(a,Vector3.zero); bounds.Encapsulate(b); bounds.Encapsulate(c); return bounds; }

    static Vector3[] ProfileAt(Vector3[][] profiles,List<RaceBarrierProfiles.ResolvedSection> sections,float progress,int side)
    {
        float weight=0;
        foreach (var section in sections) weight=Mathf.Max(weight,section.Weight(progress,side));
        var points=new Vector3[profiles[0].Length];
        for (int i=0;i<points.Length;i++) points[i]=Vector3.Lerp(profiles[0][i],profiles[1][i],weight);
        return points;
    }

    static void BuildVolumes(Audit a)
    {
        Vector3[][] profiles=ReadProfiles();
        var centers=new List<Vector3>();
        a.course.CopyCenterPathWorld(centers);
        var configured=a.course.GetComponentInParent<RaceBarrierProfiles>();
        var sections=configured==null ? new List<RaceBarrierProfiles.ResolvedSection>() : configured.Resolve(centers,a.course.ClosedLoop);
        // Match RaceCourseBarriers' row subdivisions, including exact transition boundaries.
        var cuts=new List<float> {0,a.length};
        foreach (var section in sections) section.AddBoundaries(cuts);
        float distance=0;
        for (int i=1;i<centers.Count;i++)
        {
            float length=Vector3.Distance(centers[i-1],centers[i]);
            int steps=Mathf.Max(1,Mathf.CeilToInt(length/.5f));
            for (int step=1;step<=steps;step++) cuts.Add(distance+length*step/steps);
            distance+=length;
        }
        cuts.Sort();
        a.notes.Add("RaceBarrierProfiles 解決済み区間: "+sections.Count+"。左右・進捗ごとの最大 uprightWeight と遷移境界を使用。");
        for (int interval=1;interval<cuts.Count;interval++)
        {
            float start=cuts[interval-1],end=Mathf.Min(cuts[interval],a.length);
            if (end-start<=.00001f) continue;
            a.course.TryGetSampleAtProgress(start,out var sa);
            a.course.TryGetSampleAtProgress(end,out var sb);
            // OBJ proximity covers the complete centerline, including tube sections.
            var nearSegment=new Segment {a=sa,b=sb,start=start,end=end};
            nearSegment.bounds=new Bounds(sa.position,Vector3.zero); nearSegment.bounds.Encapsulate(sb.position);
            nearSegment.bounds.Expand(new Vector3(50,0,50));
            Vector3 nearMin=nearSegment.bounds.min,nearMax=nearSegment.bounds.max;
            nearMin.y-=1.5f; nearMax.y+=15; nearSegment.bounds.SetMinMax(nearMin,nearMax);
            BuildNearPlanes(nearSegment);
            a.near.Add(nearSegment,nearSegment.bounds);
            bool ai=RaceCourseBarriers.IsInsideTube(sa.position,sa.right,a.tube);
            bool bi=RaceCourseBarriers.IsInsideTube(sb.position,sb.right,a.tube);
            if (ai && bi) continue;
            float low=start,high=end;
            if (ai!=bi)
            {
                for (int k=0;k<16;k++)
                {
                    float mid=(low+high)*.5f;
                    a.course.TryGetSampleAtProgress(mid,out var s);
                    if (RaceCourseBarriers.IsInsideTube(s.position,s.right,a.tube)==ai) low=mid; else high=mid;
                }
                if (ai) { start=high; a.course.TryGetSampleAtProgress(start,out sa); }
                else { end=low; a.course.TryGetSampleAtProgress(end,out sb); }
            }
            var seg=new Segment {a=sa,b=sb,start=start,end=end};
            var leftA=ProfileAt(profiles,sections,start,-1); var leftB=ProfileAt(profiles,sections,end,-1);
            var rightA=ProfileAt(profiles,sections,start,1); var rightB=ProfileAt(profiles,sections,end,1);
            float reach=Mathf.Max(.5f,Mathf.Max(Mathf.Max(leftA.Max(p=>p.x),leftB.Max(p=>p.x)),
                Mathf.Max(rightA.Max(p=>p.x),rightB.Max(p=>p.x))));
            seg.moveBounds=new Bounds(sa.position,Vector3.zero); seg.moveBounds.Encapsulate(sb.position);
            float extent=Mathf.Max(sa.width,sb.width)*.5f+reach;
            seg.moveBounds.Expand(new Vector3(extent*2,0,extent*2));
            Vector3 min=seg.moveBounds.min,max=seg.moveBounds.max; max.y+=6; seg.moveBounds.SetMinMax(min,max);
            a.segments.Add(seg);
            Vector3 ar=Horizontal(sa.right),br=Horizontal(sb.right);
            Vector3 al=sa.leftEdge-ar*.5f, bl=sb.leftEdge-br*.5f;
            Vector3 ra=sa.rightEdge+ar*.5f, rb=sb.rightEdge+br*.5f;
            AddStrip(a,seg,al,ra,bl,rb,0,0,6,0,0,0,0);
            AddStrip(a,seg,al,ra,bl,rb,2,6,15,0,0,0,0);
            for (int side=-1;side<=1;side+=2)
            {
                Vector3 ea=side<0 ? sa.leftEdge : sa.rightEdge, eb=side<0 ? sb.leftEdge : sb.rightEdge;
                Vector3[] pa=side<0 ? leftA : rightA, pb=side<0 ? leftB : rightB;
                for (int j=1;j<pa.Length;j++)
                {
                    if (Mathf.Min(pa[j-1].y,pb[j-1].y)>=6 || Mathf.Max(pa[j].y,pb[j].y)<=0) continue;
                    if (pa[j-1].Equals(pb[j-1]) && pa[j].Equals(pb[j]))
                    {
                        // Keep the existing compact volumes in constant arc/upright intervals.
                        Vector3 p=pa[j-1],q=pa[j];
                        float h0=Mathf.Max(0,p.y),h1=Mathf.Min(6,q.y);
                        if (h1<=h0 || q.y<=p.y) continue;
                        float slope=(q.x-p.x)/(q.y-p.y),intercept=p.x-slope*p.y;
                        float bandReach=Mathf.Max(slope*h0+intercept,slope*h1+intercept);
                        AddStrip(a,seg,ea,ea+ar*(side*bandReach),eb,eb+br*(side*bandReach),1,h0,h1,side,bandReach,slope,intercept);
                        continue;
                    }
                    // Preserve the wall mesh's two triangles; their heights change with the profile.
                    AddShoulderTriangle(a,ea,ea,eb,ar*side,ar*side,br*side,pa[j-1],pa[j],pb[j-1],start,start,end,side);
                    AddShoulderTriangle(a,ea,eb,eb,ar*side,br*side,br*side,pa[j],pb[j],pb[j-1],start,end,end,side);
                }
            }
        }
    }

    struct ShoulderVertex
    {
        public Vector3 point;
        public float height, progress, depth;
    }
    static void AddShoulderTriangle(Audit audit,Vector3 ea,Vector3 eb,Vector3 ec,
        Vector3 oa,Vector3 ob,Vector3 oc,Vector3 pa,Vector3 pb,Vector3 pc,float da,float db,float dc,int side)
    {
        ShoulderVertex Edge(Vector3 edge,Vector3 offset,float progress) => new ShoulderVertex {
            point=edge+Vector3.up*offset.y,height=offset.y,progress=progress,depth=offset.x };
        ShoulderVertex Surface(ShoulderVertex edge,Vector3 outward,float reach)
        { edge.point+=outward*reach; edge.depth=0; return edge; }
        var a=Edge(ea,pa,da); var b=Edge(eb,pb,db); var c=Edge(ec,pc,dc);
        var x=Surface(a,oa,pa.x); var y=Surface(b,ob,pb.x); var z=Surface(c,oc,pc.x);
        // Three tetrahedra fill the edge-to-surface triangular wedge without fixed-height bands.
        AddShoulderTetrahedron(audit,a,b,c,z,side);
        AddShoulderTetrahedron(audit,a,b,y,z,side);
        AddShoulderTetrahedron(audit,a,x,y,z,side);
    }
    static Field CrossField(Vector3 origin,Vector3 u,Vector3 v)
    {
        double x=(double)u.y*v.z-(double)u.z*v.y;
        double y=(double)u.z*v.x-(double)u.x*v.z;
        double z=(double)u.x*v.y-(double)u.y*v.x;
        return new Field(x,y,z,-x*origin.x-y*origin.y-z*origin.z);
    }
    static void AddShoulderTetrahedron(Audit audit,ShoulderVertex a,ShoulderVertex b,ShoulderVertex c,ShoulderVertex d,int side)
    {
        Vector3 ab=b.point-a.point,ac=c.point-a.point,ad=d.point-a.point;
        Field numerator=CrossField(a.point,ac,ad);
        double determinant=numerator.x*ab.x+numerator.y*ab.y+numerator.z*ab.z;
        if (Math.Abs(determinant)<1e-10) return; // Vertical/zero-reach end faces enclose no volume.
        Field fb=numerator*(1/determinant),fc=CrossField(a.point,ad,ab)*(1/determinant),
            fd=CrossField(a.point,ab,ac)*(1/determinant),fa=Field.Constant(1)-fb-fc-fd;
        Field Value(float va,float vb,float vc,float vd) => fa*va+fb*vb+fc*vc+fd*vd;
        Field h=Value(a.height,b.height,c.height,d.height);
        Bounds bounds=TriangleBounds(a.point,b.point,c.point); bounds.Encapsulate(d.point);
        var volume=new Volume {planes=new[] {fa,fb,fc,fd,h,Field.Constant(6)-h},height=h,
            lateral=Value(a.depth,b.depth,c.depth,d.depth),progress=Value(a.progress,b.progress,c.progress,d.progress),
            bounds=bounds,zone=1,side=side};
        audit.volumes.Add(volume); audit.index.Add(volume,bounds);
    }

    // An affine map from each road footprint triangle to (progress, lateral reach, height).
    // Splitting quads also handles varying widths/slopes without a flat world-Y road assumption.
    static Field Interpolate(Vector3 a,Vector3 b,Vector3 c,double fa,double fb,double fc)
    {
        double bx=b.x-a.x,bz=b.z-a.z,cx=c.x-a.x,cz=c.z-a.z;
        double det=bx*cz-bz*cx;
        double x=((fb-fa)*cz-(fc-fa)*bz)/det,z=(bx*(fc-fa)-cx*(fb-fa))/det;
        return new Field(x,0,z,fa-x*a.x-z*a.z);
    }
    static void AddStrip(Audit audit,Segment s,Vector3 al,Vector3 ar,Vector3 bl,Vector3 br,
        int zone,float bottom,float top,int side,float reach,float slope,float intercept)
    {
        AddVolume(audit,s,al,ar,bl,0,reach,0,s.start,s.start,s.end,zone,bottom,top,side,slope,intercept);
        AddVolume(audit,s,ar,br,bl,reach,reach,0,s.start,s.end,s.end,zone,bottom,top,side,slope,intercept);
    }
    static void AddVolume(Audit audit,Segment s,Vector3 a,Vector3 b,Vector3 c,
        float la,float lb,float lc,float pa,float pb,float pc,int zone,float bottom,float top,int side,float slope,float intercept)
    {
        if (Math.Abs((double)(b.x-a.x)*(c.z-a.z)-(double)(b.z-a.z)*(c.x-a.x))<1e-9) return;
        Field h=new Field(0,1,0,0)-Interpolate(a,b,c,a.y,b.y,c.y);
        Field l=Interpolate(a,b,c,la,lb,lc);
        var planes=new List<Field> { Interpolate(a,b,c,1,0,0),Interpolate(a,b,c,0,1,0),Interpolate(a,b,c,0,0,1),
            h-Field.Constant(bottom),Field.Constant(top)-h };
        if (zone==1) planes.Add(h*slope+Field.Constant(intercept)-l);
        Bounds bounds=TriangleBounds(a+Vector3.up*bottom,b+Vector3.up*bottom,c+Vector3.up*bottom);
        bounds.Encapsulate(a+Vector3.up*top); bounds.Encapsulate(b+Vector3.up*top); bounds.Encapsulate(c+Vector3.up*top);
        var volume=new Volume { planes=planes.ToArray(),height=h,lateral=l,
            progress=Interpolate(a,b,c,pa,pb,pc),bounds=bounds,zone=zone,side=side };
        if (zone==1) volume.lateral=Field.Constant(intercept)-l+h*slope;
        else
        {
            float Signed(Vector3 p,float progress)
            {
                audit.course.TryGetSampleAtProgress(progress,out var sample);
                return Vector3.Dot(p-sample.position,Horizontal(sample.right));
            }
            volume.lateral=Interpolate(a,b,c,Signed(a,pa),Signed(b,pb),Signed(c,pc));
        }
        audit.volumes.Add(volume); audit.index.Add(volume,bounds);
    }

    static List<Vector3> Clip(Audit a,Vector3 p,Vector3 q,Vector3 r,Field[] planes)
    {
        List<Vector3> input=a.clipA,output=a.clipB;
        input.Clear(); input.Add(p); input.Add(q); input.Add(r);
        foreach (Field plane in planes)
        {
            output.Clear();
            Vector3 previous=input[input.Count-1]; double before=plane.At(previous);
            foreach (Vector3 current in input)
            {
                double now=plane.At(current);
                if ((now>=0)!=(before>=0)) output.Add(Vector3.LerpUnclamped(previous,current,(float)(before/(before-now))));
                if (now>=0) output.Add(current);
                previous=current; before=now;
            }
            if (output.Count<3) return null;
            var swap=input; input=output; output=swap;
        }
        Vector3 area=Vector3.zero;
        for (int i=1;i<input.Count-1;i++) area+=Vector3.Cross(input[i]-input[0],input[i+1]-input[0]);
        return area.sqrMagnitude>1e-12f ? input : null;
    }

    static void AuditMesh(Audit a,Renderer renderer,Mesh mesh,Transform school,StreamWriter obj,ref long vertex)
    {
        a.near.Query(renderer.bounds,a.nearCandidates);
        if (!a.nearCandidates.Any(s=>s.bounds.Intersects(renderer.bounds))) return;
        // Editor mesh data access also works for imported meshes with Read/Write disabled; no importer edits.
        Vector3[] world=mesh.vertices;
        Matrix4x4 matrix=renderer.transform.localToWorldMatrix;
        for (int i=0;i<world.Length;i++) world[i]=matrix.MultiplyPoint3x4(world[i]);
        int[] indices=mesh.triangles;
        var rows=new Row[3];
        string path=PathOf(renderer.transform);
        Group group=GetGroup(a,renderer.transform,school);
        bool floor=Floor(renderer.transform,school),wroteGroup=false,affected=false;
        for (int i=0;i+2<indices.Length;i+=3)
        {
            Vector3 p=world[indices[i]],q=world[indices[i+1]],r=world[indices[i+2]];
            Bounds bounds=TriangleBounds(p,q,r);
            if (Vector3.Cross(q-p,r-p).sqrMagnitude<=1e-12f) continue;
            bool hit=false;
            int counted=0;
            a.index.Query(bounds,a.candidates);
            foreach (Volume v in a.candidates)
            {
                if (!v.bounds.Intersects(bounds)) continue;
                List<Vector3> polygon=Clip(a,p,q,r,v.planes);
                if (polygon==null) continue;
                Row row=rows[v.zone];
                if (row==null) rows[v.zone]=row=new Row {path=path,group=group.path,zone=v.zone,evidence="mesh"};
                if ((counted & (1<<v.zone))==0) { row.triangles++; counted|=1<<v.zone; }
                Record(a,row,v,polygon); hit=true;
            }
            if (hit) { a.affectedTriangles++; affected=true; }
            if (!a.exportGeometry || !IsNear(a,p,q,r,bounds,floor)) continue;
            if (!wroteGroup) { obj.WriteLine("g "+ObjName(path)); wroteGroup=true; }
            obj.WriteLine("v "+V(p)); obj.WriteLine("v "+V(q)); obj.WriteLine("v "+V(r));
            obj.WriteLine("f "+vertex+" "+(vertex+1)+" "+(vertex+2)); vertex+=3; a.exportedTriangles++;
        }
        if (affected) a.affectedObjects++;
        foreach (Row row in rows) if (row!=null) { a.rows.Add(row); group.rows.Add(row); }
    }
    static void Record(Audit a,Row row,Volume v,List<Vector3> polygon)
    {
        // The deepest A intersection can be at the centerline inside a large triangle,
        // even when every original/clipped corner is near an outer boundary.
        if (v.zone==0)
        {
            int count=polygon.Count;
            for (int i=0;i<count;i++)
            {
                Vector3 p=polygon[i],q=polygon[(i+1)%count];
                double before=v.lateral.At(p),after=v.lateral.At(q);
                if ((before<0 && after>0) || (before>0 && after<0))
                    polygon.Add(Vector3.LerpUnclamped(p,q,(float)(before/(before-after))));
            }
        }
        foreach (Vector3 p in polygon)
        {
            float progress=(float)v.progress.At(p),distance=a.Distance(progress);
            row.pmin=Mathf.Min(row.pmin,progress); row.pmax=Mathf.Max(row.pmax,progress);
            row.dmin=Mathf.Min(row.dmin,distance); row.dmax=Mathf.Max(row.dmax,distance);
            a.course.TryGetSampleAtProgress(progress,out var sample);
            Vector3 right=Horizontal(sample.right);
            float lateral=Vector3.Dot(p-sample.position,right);
            int side=v.side!=0 ? v.side : lateral<0 ? -1 : 1;
            float boundary=side<0 ? -Vector3.Dot(sample.leftEdge-sample.position,right) : Vector3.Dot(sample.rightEdge-sample.position,right);
            float depth=v.zone==2 ? 15-(float)v.height.At(p) : v.zone==1 ? (float)v.lateral.At(p) :
                boundary+.5f-side*lateral;
            if (row.outward==Vector3.zero || depth>row.depth ||
                (depth==row.depth && progress<row.representativeProgress))
            {
                row.depth=Mathf.Max(0,depth); row.point=p; row.outward=right*side;
                row.side=(a.direction<0 ? -side : side); row.representativeProgress=progress;
            }
        }
    }

    static void BuildNearPlanes(Segment s)
    {
        Vector3 delta=s.b.position-s.a.position;
        double distance=(double)delta.x*delta.x+(double)delta.z*delta.z;
        if (distance<1e-10) return;
        Field t=new Field(delta.x/distance,0,delta.z/distance,-((double)delta.x*s.a.position.x+(double)delta.z*s.a.position.z)/distance);
        s.nearPlanes=new Field[3][]; s.floorPlanes=new Field[3][];
        for (int region=0;region<3;region++)
        {
            Field height=new Field(0,1,0,-(region==2 ? s.b.position.y : s.a.position.y));
            if (region==1) height-=t*delta.y;
            var planes=new List<Field> {height-Field.Constant(-1.5),Field.Constant(15)-height};
            if (region==0) planes.Add(t*-1);
            else if (region==2) planes.Add(t-Field.Constant(1));
            else { planes.Add(t); planes.Add(Field.Constant(1)-t); }
            s.nearPlanes[region]=planes.ToArray();
            planes[0]=height-Field.Constant(-.3);
            s.floorPlanes[region]=planes.ToArray();
        }
    }
    static bool IsNear(Audit a,Vector3 p,Vector3 q,Vector3 r,Bounds bounds,bool floor)
    {
        a.near.Query(bounds,a.nearCandidates);
        foreach (Segment s in a.nearCandidates)
        {
            if (!s.bounds.Intersects(bounds)) continue;
            if (s.nearPlanes==null) continue;
            // Clamp the road height at the segment endpoints for the two circular end caps.
            for (int region=0;region<3;region++)
            {
                Field[] planes=floor ? s.floorPlanes[region] : s.nearPlanes[region];
                if (floor && Math.Max(planes[0].At(p),Math.Max(planes[0].At(q),planes[0].At(r)))<=0) continue;
                List<Vector3> clipped=Clip(a,p,q,r,planes);
                if (clipped!=null && PolygonSegmentDistanceSquared(clipped,s.a.position,s.b.position)<=625f) return true;
            }
        }
        return false;
    }
    static Vector2 XZ(Vector3 v) => new Vector2(v.x,v.z);
    static float PointSegment(Vector2 p,Vector2 a,Vector2 b)
    { Vector2 d=b-a; return (p-(a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(d.sqrMagnitude,1e-12f)))).sqrMagnitude; }
    static float Cross(Vector2 a,Vector2 b) => a.x*b.y-a.y*b.x;
    static float PolygonSegmentDistanceSquared(List<Vector3> polygon,Vector3 start,Vector3 end)
    {
        Vector2 a=XZ(start),b=XZ(end); float best=float.PositiveInfinity;
        bool positive=false,negative=false;
        for (int i=0;i<polygon.Count;i++)
        {
            Vector2 p=XZ(polygon[i]),q=XZ(polygon[(i+1)%polygon.Count]);
            float sign=Cross(q-p,a-p); positive|=sign>1e-5f; negative|=sign< -1e-5f;
            float den=Cross(b-a,q-p);
            if (Mathf.Abs(den)>1e-10f)
            {
                float t=Cross(p-a,q-p)/den,u=Cross(p-a,b-a)/den;
                if (t>=0 && t<=1 && u>=0 && u<=1) return 0;
            }
            best=Mathf.Min(best,Mathf.Min(Mathf.Min(PointSegment(p,a,b),PointSegment(q,a,b)),
                Mathf.Min(PointSegment(a,p,q),PointSegment(b,p,q))));
        }
        // A projected vertical face has no interior; do not classify its entire supporting line as inside.
        float area=0;
        for (int i=1;i<polygon.Count-1;i++) area+=Cross(XZ(polygon[i])-XZ(polygon[0]),XZ(polygon[i+1])-XZ(polygon[0]));
        return Mathf.Abs(area)>1e-8f && !(positive && negative) ? 0 : best;
    }

    static void AuditBounds(Audit a,Renderer renderer,Transform school)
    {
        Group g=GetGroup(a,renderer.transform,school);
        a.index.Query(renderer.bounds,a.candidates);
        var rows=new Row[3];
        foreach (Volume v in a.candidates)
        {
            if (!v.bounds.Intersects(renderer.bounds)) continue;
            // Explicitly a candidate, not a measured triangle intersection.
            if (rows[v.zone]==null)
                rows[v.zone]=new Row {path=PathOf(renderer.transform),group=g.path,zone=v.zone,evidence="skinned bounds candidate"};
            Record(a,rows[v.zone],v,new List<Vector3> {renderer.bounds.ClosestPoint(v.bounds.center)});
        }
        foreach (Row row in rows) if (row!=null) { a.rows.Add(row); g.rows.Add(row); }
        a.notes.Add("SkinnedMeshRenderer は bounds 候補のみ（三角形数 0、OBJ 対象外）: "+PathOf(renderer.transform));
    }

    static void EstimateMoves(Audit a)
    {
        foreach (Group g in a.groups.Values.Where(g=>g.rows.Any(r=>r.zone<2)))
        {
            float best=float.PositiveInfinity;
            Vector3 preferred=Vector3.zero;
            foreach (Row row in g.rows.Where(r=>r.zone<2)) preferred+=row.outward*Mathf.Max(.1f,row.depth);
            var directions=new List<Vector3>();
            if (preferred.sqrMagnitude>1e-6f) directions.Add(preferred.normalized);
            for (int i=0;i<32;i++) directions.Add(new Vector3(Mathf.Cos(i*Mathf.PI/16),0,Mathf.Sin(i*Mathf.PI/16)));
            foreach (Vector3 direction in directions)
            {
                if (preferred.sqrMagnitude>1e-6f && Vector3.Dot(direction,preferred)<0) continue;
                var intervals=new List<Vector2>();
                foreach (Segment s in a.segments)
                {
                    Bounds obstacle=s.moveBounds;
                    if (g.bounds.max.y<obstacle.min.y || g.bounds.min.y>obstacle.max.y) continue;
                    Vector3 min=obstacle.min-g.bounds.extents,max=obstacle.max+g.bounds.extents;
                    float low=0,high=float.PositiveInfinity;
                    if (Slab(g.bounds.center.x,direction.x,min.x,max.x,ref low,ref high) &&
                        Slab(g.bounds.center.z,direction.z,min.z,max.z,ref low,ref high)) intervals.Add(new Vector2(low,high));
                }
                intervals.Sort((x,y)=>x.x.CompareTo(y.x));
                float distance=.05f;
                foreach (Vector2 interval in intervals)
                {
                    if (interval.x>distance) break;
                    if (interval.y>=distance) distance=interval.y+.05f;
                }
                if (!float.IsInfinity(distance) && distance<best) { best=distance; g.shift=direction*distance; g.solved=true; }
            }
            if (!g.solved) { g.overlaps="移動解なし"; continue; }
            Bounds moved=g.bounds; moved.center+=g.shift;
            Bounds sweep=g.bounds; sweep.Encapsulate(moved);
            var overlaps=new List<string>();
            foreach (Group other in a.groups.Values)
            {
                if (other==g || !other.hasBounds) continue;
                if (moved.Intersects(other.bounds)) overlaps.Add(other.path+" (移動先)");
                else if (sweep.Intersects(other.bounds)) overlaps.Add(other.path+" (移動途中の AABB 候補)");
            }
            g.overlaps=overlaps.Count==0 ? "候補なし" : string.Join("; ",overlaps.OrderBy(x=>x,StringComparer.Ordinal));
        }
    }
    static bool Slab(float origin,float direction,float min,float max,ref float low,ref float high)
    {
        if (Mathf.Abs(direction)<1e-7f) return origin>=min && origin<=max;
        float a=(min-origin)/direction,b=(max-origin)/direction;
        low=Mathf.Max(low,Mathf.Min(a,b)); high=Mathf.Min(high,Mathf.Max(a,b));
        return high>=low;
    }

    static string F(float n) => n.ToString("0.###",Inv);
    static string V(Vector3 p) => p.x.ToString("R",Inv)+" "+p.y.ToString("R",Inv)+" "+p.z.ToString("R",Inv);
    static string Csv(string s) => "\""+s.Replace("\"","\"\"")+"\"";
    static string Md(string s) => s.Replace("|","\\|").Replace("\r"," ").Replace("\n"," ");
    static string ObjName(string s) => s.Replace("\r","_").Replace("\n","_").Replace(" ","%20").Replace("#","%23");
    static string Zone(int zone) => zone==0 ? "A 走路" : zone==1 ? "B 路肩" : "C 頭上";
    static void WriteReports(Audit a)
    {
        List<Row> rows=a.rows.OrderBy(r=>r.evidence=="mesh" ? 0 : 1).ThenBy(r=>r.zone)
            .ThenByDescending(r=>r.depth).ThenByDescending(r=>r.triangles).ThenBy(r=>r.path,StringComparer.Ordinal).ToList();
        using (var csv=new StreamWriter(Path.Combine(Output,"intrusions.csv"),false,new UTF8Encoding(true)))
        {
            csv.WriteLine("path,group,zone,evidence,intruding_triangles,max_intrusion_m,progress_min_m,progress_max_m,start_distance_min_m,start_distance_max_m,representative_progress_m,representative_start_distance_m,point_x,point_y,point_z,outward_side,outward_x,outward_z");
            foreach (Row r in rows) csv.WriteLine(string.Join(",",new[] {Csv(r.path),Csv(r.group),Csv(Zone(r.zone)),Csv(r.evidence),
                r.triangles.ToString(Inv),F(r.depth),F(r.pmin),F(r.pmax),F(r.dmin),F(r.dmax),F(r.representativeProgress),F(a.Distance(r.representativeProgress)),
                F(r.point.x),F(r.point.y),F(r.point.z),r.side<0 ? "left" : "right",F(r.outward.x),F(r.outward.z)}));
        }
        var md=new StringBuilder("# Course visual intrusion audit\n\n");
        md.AppendLine("対象: SampleScene の 1016こうしゃ FBX インスタンスの有効な見た目（tree* を含む）。シーン保存なし。");
        md.AppendLine("最大0.5m の丸め後サンプルと断面遷移境界を線形接続し、三角形を立体でクリップ。ジャンプは空中中心線、チューブ内は侵入区域から除外。交差面積ゼロは数えない。");
        md.AppendLine("A: 道路端 +0.5m、0–6m。B: 道路端から高さ別のバリア内面、0–6m（A の余白と重複あり）。C: A 上の 6–15m。");
        md.AppendLine("三角形数は各物体・区域で元三角形を一度だけ計数。DONE の物体数/三角形数は mesh の侵入の一意数（区域間重複なし）。");
        md.AppendLine("A/B の量は代表断面で外向きに抜ける距離、C は上方へ 15m 境界まで抜ける距離。物体全体の移動保証ではない。方向の左右はレース進行方向基準。");
        md.AppendLine("progress とスタート距離は m、範囲は単純 min/max（周回境界や離れた区間を跨ぐと広くなる）。代表点は最大量の交差点。CSV に代表 progress/距離も記録。");
        md.AppendLine("OBJ は指定範囲に一部でも入る元三角形をワールド座標で出力（切断しないため頂点は範囲外の場合あり）。g の空白/# は %20/%23。床3種は -0.3m 以下のみの三角形を除外。");
        md.AppendLine("\n## 侵入物体（実測優先、A → B → C、最大量 → 三角形数）\n");
        md.AppendLine("|物体|区域|根拠|三角形|最大量 m|progress m|スタート距離 m|代表点 XYZ|外向き（左右 / XZ）|");
        md.AppendLine("|---|---|---|---:|---:|---|---|---|---|");
        foreach (Row r in rows) md.AppendLine("|"+Md(r.path)+"|"+Zone(r.zone)+"|"+r.evidence+"|"+r.triangles+"|"+F(r.depth)+"|"+
            F(r.pmin)+"–"+F(r.pmax)+"|"+F(r.dmin)+"–"+F(r.dmax)+"|"+V(r.point)+"|"+(r.side<0 ? "左" : "右")+" / "+F(r.outward.x)+", "+F(r.outward.z)+"|");
        md.AppendLine("\n## 動かす単位の候補\n");
        md.AppendLine("FBX 直下の親ブランチ単位。グループの全対象 Renderer bounds と A/B を囲む区間 AABB から、32方向＋平均外向き方向を探索した保守的概算。最小実移動量ではなく、選択方向で全グループ bounds が全 A/B 区間 bounds を抜ける距離 +0.05m。C のみは XZ 移動案なし。重なりは他グループの現位置との 3D AABB 候補で、メッシュ衝突の確定ではない。");
        md.AppendLine("\n|グループ|侵入子物体|A/B/C 三角形|XZ 移動ベクトル m|方向 XZ / 距離 m|別グループとの重なり候補|");
        md.AppendLine("|---|---:|---|---|---|---|");
        foreach (Group g in a.groups.Values.Where(g=>g.rows.Count>0).OrderBy(g=>g.rows.Min(r=>r.zone)).ThenByDescending(g=>g.rows.Max(r=>r.depth)).ThenBy(g=>g.path,StringComparer.Ordinal))
        {
            string counts=string.Join(" / ",Enumerable.Range(0,3).Select(z=>g.rows.Where(r=>r.zone==z).Sum(r=>r.triangles).ToString(Inv)));
            Vector3 dir=g.shift.normalized;
            md.AppendLine("|"+Md(g.path)+"|"+g.rows.Select(r=>r.path).Distinct().Count()+"|"+counts+"|"+
                (g.solved ? F(g.shift.x)+", "+F(g.shift.z) : "—")+"|"+(g.solved ? F(dir.x)+", "+F(dir.z)+" / "+F(g.shift.magnitude) : "未算出 / C のみ")+"|"+Md(g.overlaps??"—")+"|");
        }
        md.AppendLine("\n## 実行情報・制約\n");
        md.AppendLine("- コース長: "+F(a.length)+"m、侵入物体: "+a.affectedObjects+"、侵入三角形: "+a.affectedTriangles+"、OBJ 三角形: "+a.exportedTriangles);
        md.AppendLine("- バリア断面は RaceCourseBarriers と同じ RaceBarrierProfiles.Resolve、左右別の最大 Weight、既定 ControlPoint と RaceBarrierPath.GetSectionPoint の実装を使用。遷移中は道路端から表示内面の三角形までの領域を四面体分割し、0–6mでクリップ。別途手調整されたバリアは対象外。");
        md.AppendLine("- 監査領域はコース基底経路の区間線形近似。生成済みバリアMeshとの直接比較は行わず、断面遷移と道路端の補間を最大0.5mの間隔で評価する。");
        foreach (string note in a.notes) md.AppendLine("- "+note);
        File.WriteAllText(Path.Combine(Output,"intrusions.md"),md.ToString(),new UTF8Encoding(false));
    }
}
