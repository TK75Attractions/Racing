using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Creates editable, self-contained meshes and prefabs, with no runtime generation.</summary>
public static class NeonRaceMarkersBuilder
{
    private const string Folder = "Assets/Prefab/Course/NeonRaceMarkers";
    private static Material metal, inset, cyan, pink, white;

    [MenuItem("Racing/Create Neon Start Line and Goal Gate")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder + "/Materials");
        Directory.CreateDirectory(Folder + "/Meshes");
        AssetDatabase.Refresh();
        metal = Surface("MidnightMetal", new Color(.045f,.06f,.105f), .65f);
        inset = Surface("GraphiteInset", new Color(.009f,.014f,.028f), .3f);
        cyan = Surface("IonCyan", new Color(.015f,.7f,1f), .25f, 3);
        pink = Surface("LaserPink", new Color(1f,.025f,.32f), .25f, 3);
        white = Surface("IceWhite", new Color(.7f,.91f,1f), .15f, 1.8f);
        Save(StartLine(), "NeonStartLine");
        Save(GoalGate(), "NeonGoalGate");
        AssetDatabase.SaveAssets();
        Debug.Log("Neon race markers: both prefabs built and validated.");
    }

    private static Material Surface(string name, Color color, float metallic, float emission = 0)
    {
        string path = Folder + "/Materials/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", .65f);
        mat.SetColor("_EmissionColor", color * emission);
        if (emission > 0) mat.EnableKeyword("_EMISSION");
        else mat.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static GameObject Box(GameObject root, string name, Vector3 p, Vector3 size, Material mat)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(root.transform, false);
        part.transform.localPosition = p;
        part.transform.localScale = size;
        part.GetComponent<Renderer>().sharedMaterial = mat;
        UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        return part;
    }

    private static void Beam(GameObject root, Vector3 a, Vector3 b, float thickness, float depth, Material mat)
    {
        var part = Box(root,"Segment",(a+b)*.5f,new Vector3(thickness,Vector3.Distance(a,b),depth),mat);
        part.transform.localRotation = Quaternion.FromToRotation(Vector3.up,b-a);
    }

    // Geometric stroke lettering: true 3D mesh, no font/texture dependencies.
    private static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
    {
        {'S',"1,1,0,1;0,1,0,.5;0,.5,1,.5;1,.5,1,0;1,0,0,0"},
        {'T',"0,1,1,1;.5,1,.5,0"},
        {'A',"0,0,0,.8;0,.8,.2,1;.2,1,.8,1;.8,1,1,.8;1,.8,1,0;0,.45,1,.45"},
        {'R',"0,0,0,1;0,1,.8,1;.8,1,1,.8;1,.8,1,.6;1,.6,.8,.5;.8,.5,0,.5;.5,.5,1,0"},
        {'F',"0,0,0,1;0,1,1,1;0,.55,.8,.55"},
        {'I',"0,1,1,1;.5,1,.5,0;0,0,1,0"},
        {'N',"0,0,0,1;0,1,1,0;1,0,1,1"},
        {'H',"0,0,0,1;1,0,1,1;0,.5,1,.5"}
    };

    private static void Label(GameObject root, string word, Vector3 position, float height, bool floor, bool back = false)
    {
        var group = new GameObject(word);
        group.transform.SetParent(root.transform, false);
        group.transform.localPosition = position;
        if (floor) group.transform.localRotation = Quaternion.Euler(90,0,0);
        else if (back) group.transform.localRotation = Quaternion.Euler(0,180,0);
        float width = height * .65f, advance = height * .92f;
        float left = -((word.Length-1)*advance+width)*.5f;
        for (int i=0; i<word.Length; i++)
        foreach (string stroke in Glyphs[word[i]].Split(';'))
        {
            string[] coords = stroke.Split(',');
            float[] n = Array.ConvertAll(coords, s => float.Parse(s,System.Globalization.CultureInfo.InvariantCulture));
            Beam(group,new Vector3(left+i*advance+n[0]*width,n[1]*height,0),
                new Vector3(left+i*advance+n[2]*width,n[3]*height,0),height*.085f,.035f,white);
        }
    }

    private static GameObject StartLine()
    {
        var root = new GameObject("NeonStartLine");
        Box(root,"RecessedStrip",new Vector3(0,.012f,0),new Vector3(16,.024f,1.05f),inset);
        for(int row=0;row<2;row++)
        for(int col=0;col<32;col++)
            if((row+col)%2==0)
                Box(root,"Checker",new Vector3(-7.75f+col*.5f,.032f,-.25f+row*.5f),new Vector3(.48f,.014f,.48f),white);
        foreach(float z in new[]{-.66f,.66f})
            Box(root,"TimingLine",new Vector3(0,.035f,z),new Vector3(16,.028f,.075f),z<0?cyan:pink);
        Label(root,"START",new Vector3(0,.045f,-2.15f),.72f,true);
        foreach(int side in new[]{-1,1})
        {
            float x=side*8.55f;
            Box(root,"BeaconFoot",new Vector3(x,.12f,0),new Vector3(.8f,.24f,1.6f),metal);
            Box(root,"Beacon",new Vector3(x,.68f,0),new Vector3(.4f,1.12f,.65f),metal);
            Box(root,"BeaconLight",new Vector3(x,.78f,-.34f),new Vector3(.22f,.75f,.055f),side<0?cyan:pink);
            Box(root,"BeaconCap",new Vector3(x,1.28f,0),new Vector3(.46f,.07f,.7f),white);
            Box(root,"LaneEdge",new Vector3(side*7.95f,.03f,-1.6f),new Vector3(.075f,.02f,1.7f),side<0?cyan:pink);
            for(int i=0;i<3;i++)
            {
                var arrow = new GameObject("LaunchChevron");
                arrow.transform.SetParent(root.transform,false);
                arrow.transform.localPosition=new Vector3(side*5.8f,.04f,-2.5f+i*.6f);
                arrow.transform.localRotation=Quaternion.Euler(90,0,0);
                Beam(arrow,new Vector3(-.4f,0,0),new Vector3(0,.26f,0),.08f,.025f,cyan);
                Beam(arrow,new Vector3(0,.26f,0),new Vector3(.4f,0,0),.08f,.025f,cyan);
            }
        }
        return root;
    }

    private static GameObject GoalGate()
    {
        var root = new GameObject("NeonGoalGate");
        foreach(int side in new[]{-1,1})
        {
            float x = side*8.8f;
            Material neon = side<0?cyan:pink;
            Box(root,"Foundation",new Vector3(x,.18f,0),new Vector3(1.6f,.36f,2.4f),metal);
            Box(root,"FootGlow",new Vector3(x,.4f,0),new Vector3(1.3f,.09f,1.85f),neon);
            Box(root,"Tower",new Vector3(x,2.65f,0),new Vector3(.9f,4.5f,1.2f),metal);
            Beam(root,new Vector3(x,4.65f,0),new Vector3(side*6.95f,6.55f,0),.95f,1.2f,metal);
            foreach(float z in new[]{-.63f,.63f})
            {
                Box(root,"TowerInset",new Vector3(x,2.7f,z),new Vector3(.57f,3.95f,.055f),inset);
                Box(root,"LightSpine",new Vector3(x-side*.22f,2.7f,z*1.06f),new Vector3(.09f,3.85f,.06f),neon);
                Beam(root,new Vector3(x,4.65f,z),new Vector3(side*6.95f,6.55f,z),.12f,.06f,neon);
                for(int i=0;i<5;i++)
                    Box(root,"Telemetry",new Vector3(x+side*.12f,1.1f+i*.58f,z*1.07f),new Vector3(.22f,.075f,.04f),white);
            }
            for(int i=0;i<3;i++)
                Box(root,"CoolingFin",new Vector3(x+side*.54f,1+i*.34f,0),new Vector3(.26f,.14f,1.4f),metal);
        }
        Box(root,"Header",new Vector3(0,6.6f,0),new Vector3(14.3f,.94f,1.2f),metal);
        foreach(float z in new[]{-.64f,.64f})
        {
            Box(root,"Display",new Vector3(0,6.6f,z),new Vector3(7.6f,.79f,.055f),inset);
            Label(root,"FINISH",new Vector3(0,6.32f,z*1.08f),.55f,false,z>0);
            foreach(int side in new[]{-1,1})
            {
                Box(root,"HeaderLight",new Vector3(side*3.6f,7.1f,z),new Vector3(7,.07f,.06f),side<0?cyan:pink);
                Box(root,"HeaderUnderline",new Vector3(side*3.6f,6.08f,z),new Vector3(7,.055f,.06f),side<0?cyan:pink);
                for(int col=0;col<6;col++)
                for(int row=0;row<2;row++)
                    if((row+col)%2==0)
                        Box(root,"FinishChecks",new Vector3(side*(4.7f+col*.3f),6.43f+row*.3f,z*1.07f),new Vector3(.27f,.27f,.045f),white);
            }
        }
        Box(root,"FinishThreshold",new Vector3(0,.028f,0),new Vector3(16,.028f,.13f),pink);
        return root;
    }

    private static void Save(GameObject root, string name)
    {
        try
        {
            // Bake by material: five renderers per prop, while keeping assets editable.
            foreach(Material mat in new[]{metal,inset,cyan,pink,white})
            {
                var combine = new List<CombineInstance>();
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
                    if(filter.GetComponent<Renderer>().sharedMaterial==mat)
                        combine.Add(new CombineInstance { mesh=filter.sharedMesh, transform=filter.transform.localToWorldMatrix });
                if(combine.Count==0) continue;
                string path=Folder+"/Meshes/"+name+"_"+mat.name+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(!mesh) { mesh=new Mesh(); AssetDatabase.CreateAsset(mesh,path); }
                mesh.Clear();
                mesh.name=name+"_"+mat.name;
                mesh.CombineMeshes(combine.ToArray());
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
            }
            while(root.transform.childCount>0) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
            foreach(Material mat in new[]{metal,inset,cyan,pink,white})
            {
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Meshes/"+name+"_"+mat.name+".asset");
                if(!mesh) continue;
                var part=new GameObject(mat.name);
                part.transform.SetParent(root.transform,false);
                part.AddComponent<MeshFilter>().sharedMesh=mesh;
                part.AddComponent<MeshRenderer>().sharedMaterial=mat;
            }
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,Folder+"/"+name+".prefab");
            if(!prefab || prefab.GetComponentsInChildren<Collider>().Length!=0)
                throw new InvalidOperationException("Invalid marker prefab: "+name);
            foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>())
                if(!filter.sharedMesh || filter.sharedMesh.vertexCount==0 || !filter.GetComponent<Renderer>().sharedMaterial)
                    throw new InvalidOperationException("Missing marker geometry/material: "+name);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // Batch-only entry point; never replace an interactive editor's open scenes.
    public static void BuildAndPreview()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Run BuildAndPreview in an isolated batch project. Use Build in the editor.");
        Build();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorSceneManager.SetActiveScene(scene);
        try
        {
            var start=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/NeonStartLine.prefab"));
            start.transform.position=new Vector3(0,0,-3.5f);
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/NeonGoalGate.prefab"));
            var ground=new GameObject("PreviewRoad");
            Box(ground,"Asphalt",new Vector3(0,-.1f,0),new Vector3(60,.15f,80),inset);
            foreach(float x in new[]{-10f,10f})
                Box(ground,"RoadEdge",new Vector3(x,-.015f,0),new Vector3(.05f,.02f,60),metal);
            var sun=new GameObject("KeyLight").AddComponent<Light>();
            sun.type=LightType.Directional;
            sun.intensity=2.2f;
            sun.transform.rotation=Quaternion.Euler(40,-25,0);
            RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.22f,.27f,.4f);
            var volume=new GameObject("PreviewBloom").AddComponent<Volume>();
            volume.isGlobal=true;
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom=profile.Add<Bloom>();
            bloom.threshold.Override(.9f);
            bloom.intensity.Override(.45f);
            bloom.scatter.Override(.65f);
            volume.sharedProfile=profile;
            var camera=new GameObject("PreviewCamera").AddComponent<Camera>();
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            camera.GetUniversalAdditionalCameraData().antialiasing=AntialiasingMode.FastApproximateAntialiasing;
            camera.transform.position=new Vector3(18,11,-27);
            camera.transform.LookAt(new Vector3(0,2.4f,-.5f));
            camera.fieldOfView=42;
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.008f,.012f,.027f);
            camera.allowHDR=true;
            var target=new RenderTexture(1600,1000,24,RenderTextureFormat.DefaultHDR);
            var previousTarget=RenderTexture.active;
            camera.targetTexture=target;
            camera.Render();
            RenderTexture.active=target;
            var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,1600,1000),0,0);
            texture.Apply();
            Directory.CreateDirectory("Artifacts");
            File.WriteAllBytes("Artifacts/NeonRaceMarkers.png",texture.EncodeToPNG());
            camera.targetTexture=null;
            RenderTexture.active=previousTarget;
            UnityEngine.Object.DestroyImmediate(texture);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(profile);
        }
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }
    }
}
