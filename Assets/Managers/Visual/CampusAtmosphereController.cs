using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Campus colour/material refinement, lightweight vegetation and surrounding town; reversible in F8.</summary>
public sealed class CampusAtmosphereController : MonoBehaviour
{
    private sealed class Surface { public Renderer renderer; public Material[] original, styled; public bool visible; }
    private sealed class Vegetation { public Renderer renderer; public bool visible; public Collider[] colliders; public bool[] collision; }
    private readonly List<Surface> surfaces=new List<Surface>();
    private readonly List<Vegetation> vegetation=new List<Vegetation>();
    private readonly List<Material> owned=new List<Material>();
    private readonly Dictionary<string,Material> variants=new Dictionary<string,Material>();
    private Gmanager manager; private RaceCourse course; private GameObject root;
    private readonly List<Mesh> meshes = new List<Mesh>();
    private Material[] palette; private bool applied, configured, night;
    private UniversalRenderPipelineAsset pipeline; private float shadowDistance; private int shadowResolution;
    private bool introShadows;
    public int ReplacedTreeVertices { get; private set; }
    public int TreeCount { get; private set; }
    public int HouseCount { get; private set; }
    public int StartGroveTrees { get; private set; }
    public int StartGroveVertices { get; private set; }
    public int GeneratedVertices { get; private set; }
    public bool IsApplied => applied;
    public bool IntroShadowQualityActive => introShadows;
    private static readonly Color grass=new Color(.22f,.30f,.19f);
    public void Configure(Gmanager race, RaceCourse road)
    {
        if(configured)return; manager=race;course=road;
        // Authored night scenery already owns its materials, trees and lighting.
        // Do not replace those assets with the generated daytime campus palette.
        foreach (var sceneRoot in race.gameObject.scene.GetRootGameObjects())
            if (sceneRoot.name == "NightEnnichi") return;
        var campus=GameObject.Find("1016こうしゃ");if(campus==null)return;
        configured=true;
        palette=new[]{Make("Grass",grass),Make("Foliage olive",new Color(.26f,.39f,.17f)),Make("Foliage green",new Color(.15f,.31f,.21f)),
            Make("Trunks",new Color(.23f,.17f,.12f)),Make("Warm paving",new Color(.53f,.51f,.44f)),Make("Town asphalt",new Color(.17f,.21f,.24f)),
            Make("Track clay",new Color(.51f,.27f,.18f)),Make("Court lines",new Color(.84f,.83f,.71f)),Make("Roof slate",new Color(.22f,.29f,.32f)),
            Make("Town ivory",new Color(.70f,.67f,.57f)),Make("Town blue grey",new Color(.45f,.53f,.56f)),Make("Town brick",new Color(.55f,.40f,.31f)),
            Make("Window blue",new Color(.16f,.31f,.39f),.65f),Make("Roof equipment",new Color(.64f,.67f,.64f)),
            Make("Grove deep foliage",new Color(.08f,.19f,.105f)),Make("Grove leaf tips",new Color(.25f,.41f,.16f))};
        var builder=new CampusSceneryMesh(palette.Length);
        foreach(var renderer in campus.GetComponentsInChildren<MeshRenderer>())
        {
            string name=renderer.name;
            if(name.StartsWith("tree",System.StringComparison.OrdinalIgnoreCase))
            {
                var colliders=renderer.GetComponents<Collider>();var states=new bool[colliders.Length];for(int i=0;i<states.Length;i++)states[i]=colliders[i].enabled;
                vegetation.Add(new Vegetation{renderer=renderer,visible=renderer.enabled,colliders=colliders,collision=states});
                ReplacedTreeVertices+=renderer.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount??0;
                // Retain the old start-side woodland's anchors and irregular silhouette, with new green geometry.
                // Fit the narrow planting strip between the school facade and the starting straight.
                Bounds treeBounds = renderer.bounds;
                if (treeBounds.center.z > 760f && treeBounds.center.z < 800f)
                    BuildStartGroveTree(builder, treeBounds);
                continue;
            }
            if(name.StartsWith("Bézier")||name.Contains("Tube")||name.Contains("Armco"))continue;
            Bounds bounds=renderer.bounds;
            bool roof=(name.Contains("屋上") || (bounds.center.y>8 && bounds.size.y<1.6f && Mathf.Min(bounds.size.x,bounds.size.z)>12));
            var original=renderer.sharedMaterials;var styled=(Material[])original.Clone();bool changed=false;
            for(int i=0;i<styled.Length;i++)
            {
                var mat=original[i];if(mat==null)continue;string category=null;
                if(name=="Plane") category="ground";
                else if(roof)category="roof";
                else if(mat.name.Contains("ガラス") || (mat.HasProperty("_BaseColor")&&mat.GetColor("_BaseColor").a<.9f))category="glass";
                else if(mat.name.Contains("窓枠"))category="frame";
                else if(mat.name.Contains("壁") || mat.name=="Material.008" || mat.name=="Material.009" || mat.name=="Material.012") category="wall";
                if(category==null)continue;styled[i]=Variant(mat,category);changed=true;
            }
            if(changed) surfaces.Add(new Surface{renderer=renderer,original=original,styled=styled,visible=renderer.enabled});
            if(roof && bounds.center.y>25 && bounds.size.x>65)
            {
                for(int i=-1;i<=1;i++)
                {
                    Vector3 p=bounds.center+new Vector3(i*18,bounds.extents.y+.7f,0);
                    builder.Box(p,new Vector3(5,1.4f,3.4f),13);builder.Box(p+Vector3.up*.75f,new Vector3(4,.13f,2.4f),8);
                    builder.Box(p+new Vector3(0,-.3f,5),new Vector3(8,.22f,3.5f),12);
                }
            }
        }
        BuildGround(builder); BuildSportsField(builder); BuildTown(builder); BuildGreenBelt(builder); BuildCourtyard(builder); BuildStartUndergrowth(builder);
        root=new GameObject("Campus atmosphere scenery");root.transform.SetParent(transform,false);root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        meshes.AddRange(builder.BuildChunks());
        GeneratedVertices = builder.VertexCount;
        foreach (Mesh chunk in meshes)
        {
            var region = new GameObject(chunk.name);
            region.transform.SetParent(root.transform, false);
            region.AddComponent<MeshFilter>().sharedMesh = chunk;
            region.AddComponent<MeshRenderer>().sharedMaterials = palette;
        }
        ApplySwitch();
    }
    private Material Make(string name,Color color,float smoothness=.08f)
    {
        var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Campus "+name};mat.SetColor("_BaseColor",color);mat.SetFloat("_Smoothness",smoothness);owned.Add(mat);return mat;
    }
    private Material Variant(Material source,string category)
    {
        string key=source.GetInstanceID()+category;if(variants.TryGetValue(key,out var found))return found;
        var mat=new Material(source){name=source.name+" / campus "+category};owned.Add(mat);variants.Add(key,mat);
        Color color=category=="ground"?grass:category=="roof"?new Color(.23f,.30f,.33f):category=="glass"?new Color(.29f,.45f,.51f,.65f):
            category=="frame"?new Color(.78f,.80f,.77f):new Color(.73f,.70f,.63f);
        mat.SetColor("_BaseColor",color);mat.color=color;
        if(mat.HasProperty("_Smoothness"))mat.SetFloat("_Smoothness",category=="glass"?.72f:.16f);
        return mat;
    }
    private void Tree(CampusSceneryMesh b,Vector3 p,float height,int index)
    {
        float radius=Mathf.Min(4,height*.28f);
        b.Box(p+Vector3.up*(height*.27f),new Vector3(.65f,height*.54f,.65f),3);
        b.Ellipsoid(p+Vector3.up*(height*.69f),new Vector3(radius,height*.36f,radius*.88f),1+index%2,index*.87f);
        b.Ellipsoid(p+new Vector3(radius*.45f,height*.57f,0),new Vector3(radius*.65f,height*.25f,radius*.67f),1+(index+1)%2,index*.57f);
        TreeCount++;
    }
    private void BuildStartGroveTree(CampusSceneryMesh b, Bounds original)
    {
        int before = b.VertexCount;
        var random = new System.Random(18073 + StartGroveTrees * 97);
        float Next(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        bool besideFacade = original.center.x > 350f && original.center.x < 480f;
        // Facade front is z=777.27, kerb z≈778.4: trunks belong in that strip,
        // while the raised crown can overhang the kerb without planting trees on the asphalt.
        Vector3 p = new Vector3(original.center.x, Mathf.Max(0f,original.min.y),
            besideFacade ? 777.95f : original.center.x < 350f ? 773.5f : original.center.z);
        float height = Mathf.Clamp(original.size.y * .95f, 10f, 23f);
        float spread = Mathf.Clamp(height * .32f, 3.6f, 5.2f);
        Vector3 crown = p + new Vector3(Next(-.5f,.5f),height * .72f,Next(-.2f,.2f));
        b.Branch(p,crown,.36f * height / 13f,.13f,3);
        for (int layer = 0; layer < 3; layer++)
        {
            for (int lobe = 0; lobe < 6; lobe++)
            {
                float angle = lobe * Mathf.PI / 3f + layer * .5f + Next(-.22f,.22f);
                float reach = spread * (layer == 2 ? .45f : .75f) * Next(.8f,1.08f);
                Vector3 center = p + new Vector3(Mathf.Cos(angle) * reach,
                    height * (.48f + .16f * layer) + Next(-.45f,.45f), Mathf.Sin(angle) * reach * (besideFacade ? .30f : .55f));
                Vector3 radius = new Vector3(spread * Next(.42f,.57f),height * Next(.13f,.18f),spread * (besideFacade ? Next(.18f,.23f) : Next(.35f,.46f)));
                if (besideFacade) center.z = Mathf.Max(center.z,777.85f + radius.z);
                int material = layer == 0 ? 14 : (lobe + layer) % 3 == 0 ? 1 : 2;
                b.Ellipsoid(center,radius,material,Next(0f,Mathf.PI));
                if (lobe % 2 == 0) b.Branch(p + Vector3.up * height * .34f,center,.13f,.045f,3);
                // Individual leaf sprays break up the rounded canopy silhouette at driving distance.
                for (int leaf = 0; leaf < 6; leaf++)
                {
                    float a = Next(0f,Mathf.PI * 2f), y = Next(-.65f,.7f);
                    Vector3 outward = new Vector3(Mathf.Cos(a),y,Mathf.Sin(a)).normalized;
                    Vector3 at = center + Vector3.Scale(radius,outward) * Next(.9f,1.08f);
                    Vector3 stem = (outward + Vector3.up * .5f).normalized * Next(.25f,.5f);
                    Vector3 across = Vector3.Cross(stem,Vector3.up).normalized * Next(.11f,.23f);
                    Vector3 tip = at + stem, left = at + stem * .5f - across, right = at + stem * .5f + across;
                    if (besideFacade)
                    {
                        at.z = Mathf.Clamp(at.z,777.85f,781.2f); tip.z = Mathf.Clamp(tip.z,777.85f,781.2f);
                        left.z = Mathf.Clamp(left.z,777.85f,781.2f); right.z = Mathf.Clamp(right.z,777.85f,781.2f);
                    }
                    int tint = leaf % 3 == 0 ? 15 : material;
                    b.Quad(at,left,tip,right,tint); b.Quad(right,tip,left,at,tint);
                }
            }
        }
        StartGroveTrees++; TreeCount++;
        StartGroveVertices += b.VertexCount - before;
    }
    private void BuildStartUndergrowth(CampusSceneryMesh b)
    {
        int before = b.VertexCount;
        var random = new System.Random(24691);
        for (int i = 0; i < 26; i++)
        {
            Vector3 p = new Vector3(349f + i * 4.8f,1.2f + (float)random.NextDouble() * .4f,777.95f);
            b.Ellipsoid(p,new Vector3(3.0f,1.5f,.35f),i % 3 == 0 ? 14 : 2,i * .73f);
            b.Ellipsoid(p + new Vector3(1.5f,.4f,-.1f),new Vector3(2.4f,1.9f,.3f),i % 2 == 0 ? 1 : 14,i * .31f);
        }
        StartGroveVertices += b.VertexCount - before;
    }
    private void BuildGround(CampusSceneryMesh b)
    {
        // Continue ground outside the model only; never cap the underground course's openings.
        b.Box(new Vector3(-480,-.55f,600),new Vector3(1400,.3f,2800),0);
        b.Box(new Vector3(1480,-.55f,600),new Vector3(1440,.3f,2800),0);
        b.Box(new Vector3(490,-.55f,-400),new Vector3(540,.3f,1200),0);
        b.Box(new Vector3(490,-.55f,1530),new Vector3(540,.3f,1340),0);
        // A forecourt stays outside the straight's barrier.
        b.Box(new Vector3(480,.04f,837),new Vector3(115,.12f,26),4);
    }
    private void BuildSportsField(CampusSceneryMesh b)
    {
        Vector3 p=new Vector3(452,.045f,397);
        b.Ring(p,new Vector2(105,70),new Vector2(86,49),6);
        for(int i=0;i<4;i++) b.Ring(p+Vector3.up*(.02f+i*.001f),new Vector2(90+i*4.5f,53+i*4.6f),new Vector2(89.8f+i*4.5f,52.8f+i*4.6f),7);
        b.Ring(p+Vector3.up*.025f,new Vector2(10,10),new Vector2(9.8f,9.8f),7);
        b.Box(p+Vector3.up*.02f,new Vector3(.18f,.025f,82),7);
        foreach(int sign in new[]{-1,1})
        {
            b.Box(p+new Vector3(sign*73,.02f,0),new Vector3(.18f,.025f,82),7);
            b.Box(p+new Vector3(0,.02f,sign*41),new Vector3(146,.025f,.18f),7);
            b.Box(p+new Vector3(sign*73,2.8f,0),new Vector3(.2f,.2f,11),7);
            foreach(int z in new[]{-1,1})b.Box(p+new Vector3(sign*73,1.4f,z*5.5f),new Vector3(.2f,2.8f,.2f),7);
        }
    }
    private void House(CampusSceneryMesh b,Vector3 p,int index,System.Random random)
    {
        float h=9+(float)random.NextDouble()*16,w=12+(float)random.NextDouble()*8,d=12+(float)random.NextDouble()*6;
        b.Box(p+Vector3.up*h*.5f,new Vector3(w,h,d),9+index%3);
        b.Box(p+Vector3.up*(h+.4f),new Vector3(w+.7f,.8f,d+.7f),8);
        for(int floor=0;floor<(int)(h/4);floor++)for(int side=-1;side<=1;side+=2)
            b.Box(p+new Vector3(0,2.6f+floor*4,side*(d*.5f+.03f)),new Vector3(w*.65f,1.3f,.07f),12);
        if(index%3==0)b.Box(p+new Vector3(w*.2f,h+1.4f,0),new Vector3(3,2,3),13);
        HouseCount++;
    }
    private void BuildTown(CampusSceneryMesh b)
    {
        var random=new System.Random(90210);int n=0;
        for(int side=-1;side<=1;side+=2)
        {
            float x=side<0?165:815;
            b.Box(new Vector3(x,-.12f,570),new Vector3(16,.12f,740),5);
            for(int z=240;z<900;z+=38)for(int row=0;row<3;row++)
                House(b,new Vector3(x+(side<0?-1:1)*(24+row*29),0,z+(float)random.NextDouble()*6),n++,random);
        }
        foreach(int z in new[]{135,935,1030})
        {
            b.Box(new Vector3(490,-.12f,z),new Vector3(540,.12f,14),5);
            for(int x=250;x<760;x+=33)
                House(b,new Vector3(x,0,z+25+(float)random.NextDouble()*5),n++,random);
        }
    }
    private void BuildGreenBelt(CampusSceneryMesh b)
    {
        var random=new System.Random(714);
        for(int i=0;i<96;i++)
        {
            float x,z;
            if(i<48){x=i%2==0?225:755;z=220+i/2*28;}
            else {x=245+(i-48)%24*21;z=i<72?894:190;}
            x+=(float)random.NextDouble()*5;z+=(float)random.NextDouble()*5;
            Tree(b,new Vector3(x,0,z),11+(float)random.NextDouble()*6,i);
        }
        // Front-of-school planting, benches and low hedges; kept clear of the grid and flight route.
        for(int x=440;x<=530;x+=15)
        {
            Tree(b,new Vector3(x,0,855),10,TreeCount);
            b.Box(new Vector3(x,.45f,848),new Vector3(8,.9f,2.5f),2);
            b.Box(new Vector3(x,1.1f,840),new Vector3(4,.3f,1.1f),3);
        }
    }
    private void BuildCourtyard(CampusSceneryMesh b)
    {
        b.Box(new Vector3(594,.06f,722),new Vector3(77,.12f,78),4);
        foreach(int x in new[]{578,610})
        {
            b.Box(new Vector3(x,.14f,722),new Vector3(23,.08f,58),0);
            foreach(int z in new[]{700,722,744})Tree(b,new Vector3(x,0,z),11+(z%3),TreeCount);
            b.Box(new Vector3(x,1.0f,758),new Vector3(5,.3f,1.2f),3);
        }
        b.Box(new Vector3(452,.04f,496),new Vector3(8,.08f,50),4);
        b.Box(new Vector3(452,.04f,480),new Vector3(220,.08f,6),4);
    }
    private void OnEnable(){if(configured)ApplySwitch();}
    private void OnDisable(){if(configured)ApplySwitch();}
    public void ApplySwitch()
    {
        if(!configured)return;
        bool enabled=isActiveAndEnabled && RaceFeatureController.Enabled(RaceFeature.CampusAtmosphere);
        foreach(var surface in surfaces)if(surface.renderer!=null) surface.renderer.sharedMaterials=enabled?surface.styled:surface.original;
        foreach(var tree in vegetation)
        {
            if(tree.renderer!=null)tree.renderer.enabled=enabled?false:tree.visible;
            for(int i=0;i<tree.colliders.Length;i++)if(tree.colliders[i]!=null)tree.colliders[i].enabled=enabled?false:tree.collision[i];
        }
        if(root!=null)root.SetActive(enabled);applied=enabled;
        manager.GetComponent<CourseIntroDirector>()?.RefreshBackdropMaterial();
        RaceEnvironmentController.Active?.ApplyEnvironment();
        SetNight(); UpdateShadows();
    }
    private void LateUpdate() { if(configured){SetNight();UpdateShadows();} }
    private void SetNight()
    {
        bool target=applied && RaceEnvironmentController.Active!=null && RaceEnvironmentController.Active.CurrentTimeOfDay==RaceEnvironmentController.TimeOfDay.Night;
        if(target==night)return;night=target;
        foreach(var pair in variants)if(pair.Key.EndsWith("glass"))SetEmission(pair.Value,target);
        SetEmission(palette[12],target);
    }
    private static void SetEmission(Material material,bool enabled)
    {
        material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",enabled?new Color(1.5f,.68f,.22f):Color.black);
    }
    private void UpdateShadows()
    {
        bool target=applied&&manager!=null&&(manager.IsCourseIntroPlaying || manager.IsMenuSceneryPlaying);
        if(target==introShadows)return;
        if(target)
        {
            pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;if(pipeline==null)return;
            shadowDistance=pipeline.shadowDistance;shadowResolution=pipeline.mainLightShadowmapResolution;
            pipeline.shadowDistance=Mathf.Max(650,shadowDistance);pipeline.mainLightShadowmapResolution=Mathf.Max(4096,shadowResolution);
            introShadows=true;
        }
        else RestoreShadows();
    }
    private void RestoreShadows()
    {
        if(introShadows&&pipeline!=null){pipeline.shadowDistance=shadowDistance;pipeline.mainLightShadowmapResolution=shadowResolution;}
        introShadows=false;
    }
    private void OnDestroy()
    {
        RestoreShadows();
        foreach(var surface in surfaces)if(surface.renderer!=null)surface.renderer.sharedMaterials=surface.original;
        foreach(var tree in vegetation)
        {
            if(tree.renderer!=null)tree.renderer.enabled=tree.visible;
            for(int i=0;i<tree.colliders.Length;i++)if(tree.colliders[i]!=null)tree.colliders[i].enabled=tree.collision[i];
        }
        if(root!=null)Destroy(root);
        foreach(var mesh in meshes)if(mesh!=null)Destroy(mesh);
        meshes.Clear();
        foreach(var material in owned)if(material!=null)Destroy(material);
    }
}
