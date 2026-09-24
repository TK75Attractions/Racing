using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Builds ordinary, self-contained course props; no runtime generation is required.</summary>
public static class CourseDirectionPropsBuilder
{
    private const string Folder = "Assets/Prefab/Course/DirectionProps";
    private static Material steel, dark, yellow, white, red;

    [MenuItem("Racing/Create Course Direction Props")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder + "/Materials");
        Directory.CreateDirectory(Folder + "/Meshes");
        AssetDatabase.Refresh();
        steel = Material("GalvanizedSteel", new Color(.48f, .56f, .62f), .75f);
        dark = Material("Charcoal", new Color(.025f, .035f, .045f));
        yellow = Material("SafetyYellow", new Color(1f, .72f, .015f));
        white = Material("ReflectiveWhite", new Color(.95f, .98f, 1f));
        red = Material("DirectionRed", new Color(.85f, .025f, .035f));
        Save(Guardrail(), "ArrowGuardrail");
        Save(Sign(), "ArrowSign");
        Save(Pole(), "NoEntryPole");
        AssetDatabase.SaveAssets();
        Debug.Log("Course direction props created and validated: 3 prefabs.");
    }

    private static Material Material(string name, Color color, float metallic = 0)
    {
        string path = Folder + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("URP Lit shader is unavailable.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", .35f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static GameObject Part(GameObject root, string name, Vector3 position, Vector3 scale,
        Material material, PrimitiveType shape = PrimitiveType.Cube)
    {
        var part = GameObject.CreatePrimitive(shape);
        part.name = name;
        part.transform.SetParent(root.transform, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        return part;
    }

    private static void Box(GameObject root, Vector3 center, Vector3 size)
    {
        var collider = root.AddComponent<BoxCollider>();
        collider.center = center;
        collider.size = size;
    }

    private static void Arrow(GameObject root, float x, float y, float z, float size, Material material)
    {
        // Two-sided arrow: both faces point along the same local +X direction.
        string path = Folder + "/Meshes/Arrow.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh)
        {
            mesh = new Mesh { name = "Arrow" };
            var front = new[] {
                new Vector3(-.6f,-.12f,0), new Vector3(.05f,-.12f,0),
                new Vector3(.05f,.12f,0), new Vector3(-.6f,.12f,0),
                new Vector3(.05f,-.36f,0), new Vector3(.6f,0,0), new Vector3(.05f,.36f,0) };
            var vertices = new Vector3[14];
            Array.Copy(front,0,vertices,0,7);
            Array.Copy(front,0,vertices,7,7);
            mesh.vertices = vertices;
            mesh.triangles = new[] {0,1,2,0,2,3,4,5,6,9,8,7,10,9,7,13,12,11};
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
        }
        var part = new GameObject("Arrow_Right");
        part.transform.SetParent(root.transform, false);
        part.transform.localPosition = new Vector3(x,y,z);
        part.transform.localScale = Vector3.one * size;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        part.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static GameObject Guardrail()
    {
        var root = new GameObject("ArrowGuardrail");
        foreach (float x in new[] {-2.5f, 0f, 2.5f})
        {
            Part(root,"SteelPost",new Vector3(x,.65f,.12f),new Vector3(.14f,1.3f,.18f),steel);
            Part(root,"Foot",new Vector3(x,.045f,.12f),new Vector3(.42f,.09f,.48f),dark);
        }
        Part(root,"Rail",new Vector3(0,1,0),new Vector3(6,.65f,.18f),white);
        foreach (float y in new[] {.7f,1.3f})
            Part(root,"RolledEdge",new Vector3(0,y,0),new Vector3(6,.07f,.25f),red);
        foreach (float z in new[] {-.103f,.103f})
        {
            Part(root,"ArrowPanel",new Vector3(0,1,z),new Vector3(5.75f,.49f,.025f),white);
            foreach (float x in new[] {-1.95f,0f,1.95f}) Arrow(root,x,1,z*1.16f,.58f,red);
        }
        Box(root,new Vector3(0,.7f,0),new Vector3(6,1.4f,.38f));
        return root;
    }

    private static GameObject Sign()
    {
        var root = new GameObject("ArrowSign");
        Part(root,"WeightedBase",new Vector3(0,.1f,0),new Vector3(.95f,.2f,.7f),dark);
        Part(root,"Upright",new Vector3(0,1.05f,0),new Vector3(.12f,2.1f,.12f),steel);
        Part(root,"RedBorder",new Vector3(0,2.05f,0),new Vector3(1.9f,1.05f,.12f),red);
        foreach (float z in new[] {-.068f,.068f})
        {
            Part(root,"WhiteFace",new Vector3(0,2.05f,z),new Vector3(1.78f,.93f,.02f),white);
            Arrow(root,0,2.05f,z*1.2f,1.12f,red);
        }
        Box(root,new Vector3(0,.1f,0),new Vector3(.95f,.2f,.7f));
        Box(root,new Vector3(0,1.05f,0),new Vector3(.12f,2.1f,.12f));
        Box(root,new Vector3(0,2.05f,0),new Vector3(1.9f,1.05f,.16f));
        return root;
    }

    private static GameObject Pole()
    {
        var root = new GameObject("NoEntryPole");
        Part(root,"HeavyBase",new Vector3(0,.08f,0),new Vector3(.64f,.08f,.64f),dark,PrimitiveType.Cylinder);
        Part(root,"PoleCore",new Vector3(0,.95f,0),new Vector3(.28f,.8f,.28f),dark,PrimitiveType.Cylinder);
        string path = Folder + "/Meshes/DiagonalBands.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int band = -1; band < 7; band++)
            for (int segment = 0; segment < 48; segment++)
            {
                int start = vertices.Count;
                for (int corner = 0; corner < 4; corner++)
                {
                    float angle = (segment + (corner == 1 || corner == 2 ? 1 : 0)) * Mathf.PI * 2 / 48;
                    float y = Mathf.Clamp(.15f + band * .3f + (corner >= 2 ? .15f : 0) + .13f * Mathf.Cos(angle),.15f,1.75f);
                    vertices.Add(new Vector3(.142f*Mathf.Cos(angle),y,.142f*Mathf.Sin(angle)));
                }
                triangles.AddRange(new[] {start,start+2,start+1,start,start+3,start+2});
            }
            mesh = new Mesh {name = "DiagonalYellowBands"};
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,path);
        }
        var bands = new GameObject("DiagonalYellowBands");
        bands.transform.SetParent(root.transform,false);
        bands.AddComponent<MeshFilter>().sharedMesh = mesh;
        bands.AddComponent<MeshRenderer>().sharedMaterial = yellow;
        Part(root,"YellowCap",new Vector3(0,1.77f,0),new Vector3(.32f,.04f,.32f),yellow,PrimitiveType.Cylinder);
        Part(root,"ReflectiveCollar",new Vector3(0,1.62f,0),new Vector3(.29f,.025f,.29f),white,PrimitiveType.Cylinder);
        var collider = root.AddComponent<CapsuleCollider>();
        collider.center = new Vector3(0,.94f,0);
        collider.radius = .16f;
        collider.height = 1.76f;
        Box(root,new Vector3(0,.08f,0),new Vector3(.64f,.16f,.64f));
        return root;
    }

    private static void Save(GameObject root, string name)
    {
        try
        {
            string path = Folder + "/" + name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root,path);
            if (!prefab || prefab.GetComponentsInChildren<Collider>().Length == 0)
                throw new InvalidOperationException("Missing prefab or collider: " + path);
            foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>())
                if (!renderer.sharedMaterial || !renderer.GetComponent<MeshFilter>().sharedMesh)
                    throw new InvalidOperationException("Missing mesh/material: " + path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    public static void BuildAndPreview()
    {
        Build();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        string[] names = {"ArrowGuardrail","ArrowSign","NoEntryPole"};
        Vector3[] positions = {new Vector3(-1.4f,0,1.2f),new Vector3(2.75f,0,0),new Vector3(4.25f,0,-.2f)};
        for (int i=0;i<names.Length;i++)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/"+names[i]+".prefab"));
            instance.transform.position = positions[i];
        }
        var ground = new GameObject("GroundRoot");
        Part(ground,"Ground",new Vector3(0,-.08f,0),new Vector3(200,.1f,200),dark);
        var light = new GameObject("KeyLight").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 2;
        light.transform.rotation = Quaternion.Euler(45,-35,0);
        RenderSettings.ambientLight = new Color(.55f,.6f,.7f);
        var camera = new GameObject("PreviewCamera").AddComponent<Camera>();
        camera.transform.position = new Vector3(7,5,-12);
        camera.transform.LookAt(new Vector3(0,1,0));
        camera.orthographic = true;
        camera.orthographicSize = 3.8f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.12f,.16f,.22f);
        var target = new RenderTexture(1500,850,24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(1500,850,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,1500,850),0,0);
        texture.Apply();
        Directory.CreateDirectory("Artifacts");
        File.WriteAllBytes("Artifacts/CourseDirectionProps.png",texture.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(texture);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
    }
}
