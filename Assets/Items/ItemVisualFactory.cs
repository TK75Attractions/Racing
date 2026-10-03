using System.Collections.Generic;
using UnityEngine;

/// <summary>アイテム系オブジェクトの見た目を、外部アセットなしで生成するヘルパーです。</summary>
public static class ItemVisualFactory
{
    /// <summary>縦長の八面体（クリスタル）です。原点が中心です。</summary>
    public static Mesh CreateCrystal(float radius, float height)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        Vector3 top = Vector3.up * height * 0.5f;
        Vector3 bottom = Vector3.down * height * 0.5f;
        const int sides = 6;
        for (int i = 0; i < sides; i++)
        {
            Vector3 a = Ring(radius, i, sides);
            Vector3 b = Ring(radius, i + 1, sides);
            AddTriangle(vertices, triangles, top, b, a);
            AddTriangle(vertices, triangles, bottom, a, b);
        }

        return Finish("ItemCrystalMesh", vertices, triangles);
    }

    /// <summary>角を面取りしたような見た目の立方体です（面ごとに頂点を分けてフラットに陰影付けします）。</summary>
    public static Mesh CreateBox(float size)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        float h = size * 0.5f;
        Vector3[] corners =
        {
            new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(-h, h, -h),
            new Vector3(-h, -h, h), new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h)
        };
        AddQuad(vertices, triangles, corners[0], corners[3], corners[2], corners[1]);
        AddQuad(vertices, triangles, corners[5], corners[6], corners[7], corners[4]);
        AddQuad(vertices, triangles, corners[4], corners[7], corners[3], corners[0]);
        AddQuad(vertices, triangles, corners[1], corners[2], corners[6], corners[5]);
        AddQuad(vertices, triangles, corners[3], corners[7], corners[6], corners[2]);
        AddQuad(vertices, triangles, corners[4], corners[0], corners[1], corners[5]);
        return Finish("ItemBoxMesh", vertices, triangles);
    }

    /// <summary>シールド用の球です。</summary>
    public static Mesh CreateSphere(float radius, int longitude = 24, int latitude = 16)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        for (int lat = 0; lat <= latitude; lat++)
        {
            float theta = Mathf.PI * lat / latitude;
            for (int lon = 0; lon <= longitude; lon++)
            {
                float phi = Mathf.PI * 2f * lon / longitude;
                Vector3 normal = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta),
                    Mathf.Sin(theta) * Mathf.Sin(phi));
                normals.Add(normal);
                vertices.Add(normal * radius);
            }
        }

        for (int lat = 0; lat < latitude; lat++)
        {
            for (int lon = 0; lon < longitude; lon++)
            {
                int current = lat * (longitude + 1) + lon;
                int next = current + longitude + 1;
                triangles.Add(current); triangles.Add(current + 1); triangles.Add(next);
                triangles.Add(current + 1); triangles.Add(next + 1); triangles.Add(next);
            }
        }

        Mesh mesh = new Mesh { name = "ItemShieldMesh", hideFlags = HideFlags.DontSave };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>地面に置く、輪郭が不規則な平たい油だまりです。</summary>
    public static Mesh CreateBlob(float radius, int seed, float height = 0.02f)
    {
        var vertices = new List<Vector3> { Vector3.up * height };
        var triangles = new List<int>();
        const int points = 28;
        System.Random random = new System.Random(seed);
        float phaseA = (float)random.NextDouble() * Mathf.PI * 2f;
        float phaseB = (float)random.NextDouble() * Mathf.PI * 2f;
        for (int i = 0; i < points; i++)
        {
            float angle = Mathf.PI * 2f * i / points;
            float wobble = 1f + 0.18f * Mathf.Sin(angle * 3f + phaseA) + 0.1f * Mathf.Sin(angle * 5f + phaseB);
            vertices.Add(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius * wobble + Vector3.up * height);
        }

        for (int i = 0; i < points; i++)
        {
            triangles.Add(0);
            triangles.Add(1 + (i + 1) % points);
            triangles.Add(1 + i);
        }

        return Finish("ItemOilBlobMesh", vertices, triangles);
    }

    /// <summary>不透明で発光する URP Lit マテリアルを作ります。</summary>
    public static Material CreateEmissiveMaterial(string name, Color color, float emission, float smoothness = 0.6f)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null) return null;
        Material material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
        material.EnableKeyword("_EMISSION");
        SetEmissiveColor(material, color, emission);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
        return material;
    }

    public static void SetEmissiveColor(Material material, Color color, float emission)
    {
        if (material == null) return;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * emission);
    }

    /// <summary>
    /// 半透明のマテリアルです。Sprites/Default はビルドに常に含まれ、URP でも両面・アルファ合成で描画されます。
    /// </summary>
    public static Material CreateTransparentMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) return CreateEmissiveMaterial(name, color, 1f);
        Material material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
        material.color = color;
        material.renderQueue = 3100;
        return material;
    }

    public static void DestroyObject(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Object.Destroy(target);
        else Object.DestroyImmediate(target);
    }

    private static Vector3 Ring(float radius, int index, int count)
    {
        float angle = Mathf.PI * 2f * index / count;
        return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
    }

    private static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
    {
        int start = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
    }

    private static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int start = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
    }

    private static Mesh Finish(string name, List<Vector3> vertices, List<int> triangles)
    {
        Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
