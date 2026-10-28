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

    /// <summary>Six inset faces, twelve bevels and eight corners. Face UVs keep the symbol upright.</summary>
    public static Mesh CreateBox(float size)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var uv = new List<Vector2>();
        float h = size * .5f, inset = h * .78f;
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        for (int axis = 0; axis < 3; axis++)
            foreach (float sign in new[] { -1f, 1f })
            {
                Vector3 normal = axes[axis] * sign, u = axes[(axis + 1) % 3], v = Vector3.Cross(normal, u);
                Vector3 center = normal * h;
                AddQuad(vertices, triangles, center - u * inset - v * inset, center + u * inset - v * inset,
                    center + u * inset + v * inset, center - u * inset + v * inset);
                Vector3 right = axis == 1 ? Vector3.right : Vector3.Cross(normal, Vector3.up);
                Vector3 top = axis == 1 ? Vector3.forward * sign : Vector3.up;
                for (int i = vertices.Count - 4; i < vertices.Count; i++)
                {
                    Vector3 local = vertices[i] - center;
                    uv.Add(new Vector2(Vector3.Dot(local, right) / inset + 1f, Vector3.Dot(local, top) / inset + 1f) * .5f);
                }
            }
        for (int a = 0; a < 3; a++) for (int b = a + 1; b < 3; b++)
            foreach (float sa in new[] { -1f, 1f }) foreach (float sb in new[] { -1f, 1f })
            {
                Vector3 x = axes[a] * sa, y = axes[b] * sb, along = axes[3 - a - b] * inset;
                Vector3 p = x * h + y * inset, q = x * inset + y * h;
                AddBevel(vertices, triangles, uv, x + y, p - along, q - along, q + along, p + along);
            }
        foreach (float x in new[] { -1f, 1f }) foreach (float y in new[] { -1f, 1f }) foreach (float z in new[] { -1f, 1f })
            AddBevel(vertices, triangles, uv, new Vector3(x, y, z),
                new Vector3(x * h, y * inset, z * inset), new Vector3(x * inset, y * h, z * inset),
                new Vector3(x * inset, y * inset, z * h));
        Mesh mesh = Finish("BevelledMysteryBoxMesh", vertices, triangles);
        mesh.SetUVs(0, uv);
        return mesh;
    }

    private static void AddBevel(List<Vector3> vertices, List<int> triangles, List<Vector2> uv,
        Vector3 normal, params Vector3[] points)
    {
        if (Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), normal) < 0f)
            System.Array.Reverse(points);
        int first = vertices.Count;
        vertices.AddRange(points);
        foreach (Vector3 unused in points) uv.Add(new Vector2(.025f, .025f));
        for (int i = 1; i < points.Length - 1; i++)
        { triangles.Add(first); triangles.Add(first + i); triangles.Add(first + i + 1); }
    }

    /// <summary>Small mipmapped circuit-pattern skin; no font dependency, lights or per-frame pixel uploads.</summary>
    public static Texture2D CreateMysteryBoxTexture()
    {
        const int size = 128;
        var pixels = new Color32[size * size];
        Vector2[] question = { new Vector2(-.16f,.24f), new Vector2(-.13f,.33f), new Vector2(-.04f,.38f),
            new Vector2(.09f,.36f), new Vector2(.17f,.28f), new Vector2(.16f,.18f), new Vector2(.05f,.09f),
            new Vector2(0f,.04f), new Vector2(0f,-.09f) };
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            Vector2 point = new Vector2((x + .5f) / size - .5f, (y + .5f) / size - .5f);
            float edge = Mathf.Max(Mathf.Abs(point.x), Mathf.Abs(point.y));
            float stripe = Mathf.Repeat((point.x + point.y) * 8f, 1f) < .12f ? 1f : 0f;
            Color color = Color.Lerp(new Color(.025f,.055f,.12f), new Color(.055f,.11f,.22f), stripe);
            if (edge > .44f) color = new Color(.7f,.46f,.13f);
            else if (edge > .395f && edge < .41f) color = new Color(.2f,.83f,1f);
            float distance = Vector2.Distance(point, new Vector2(0f,-.25f)) - .055f;
            for (int i = 0; i < question.Length - 1; i++)
                distance = Mathf.Min(distance, DistanceToSegment(point, question[i], question[i+1]) - .045f);
            color = Color.Lerp(color, new Color(.18f,.75f,1f), Mathf.Clamp01((.035f-distance)/.035f) * .65f);
            color = Color.Lerp(color, new Color(1f,.97f,.72f), Mathf.Clamp01(.5f-distance*size));
            // Four little sparkles remain readable as the cube turns.
            Vector2 corner = new Vector2(Mathf.Abs(point.x)-.31f, Mathf.Abs(point.y)-.31f);
            if (Mathf.Abs(corner.x) + Mathf.Abs(corner.y) < .035f) color = Color.white;
            pixels[y * size + x] = color;
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        { name = "MysteryBoxCircuitSkin", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear, anisoLevel = 2 };
        texture.SetPixels32(pixels); texture.Apply(true, true);
        return texture;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 segment = b - a;
        return Vector2.Distance(point, a + segment * Mathf.Clamp01(Vector2.Dot(point - a, segment) / segment.sqrMagnitude));
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
