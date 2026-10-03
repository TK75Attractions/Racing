using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 全車のタイヤ痕を1つの動的メッシュにまとめて描画します。
/// 区間数が上限に達すると、古い痕から順に上書きします。表示専用で判定には影響しません。
/// </summary>
[DisallowMultipleComponent]
public sealed class TireMarkRenderer : MonoBehaviour
{
    [Tooltip("同時に残せるタイヤ痕の区間数。多いほど長く残りますが、頂点数が増えます。")]
    [SerializeField, Range(64, 8192)] private int maxSegments = 4096;
    [SerializeField] private Color markColor = new Color(0.025f, 0.025f, 0.03f, 0.72f);

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static TireMarkRenderer instance;

    private Vector3[] vertices;
    private Color32[] colors;
    private Mesh mesh;
    private Material material;
    private Bounds bounds;
    private bool hasBounds;
    private int nextSegment;
    private int segmentCount;
    private bool dirty;
    private bool initialized;

    public int SegmentCount => segmentCount;
    public int MaxSegments => maxSegments;

    /// <summary>シーンに1つだけのタイヤ痕描画を返します。なければ生成します。</summary>
    public static TireMarkRenderer GetOrCreate()
    {
        if (instance != null) return instance;
        GameObject root = new GameObject("TireMarks") { hideFlags = HideFlags.DontSave };
        instance = root.AddComponent<TireMarkRenderer>();
        return instance;
    }

    /// <summary>コース上のタイヤ痕をすべて消します。レースの開始時や終了時に呼びます。</summary>
    public static void ClearAll()
    {
        if (instance != null) instance.Clear();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        Initialize();
    }

    /// <summary>1区間分の帯を追加します。始点と終点それぞれの左右の端と濃さ（0〜1）を渡します。</summary>
    public void AddSegment(Vector3 fromLeft, Vector3 fromRight, float fromAlpha,
        Vector3 toLeft, Vector3 toRight, float toAlpha)
    {
        if (!Initialize()) return;

        int vertex = nextSegment * 4;
        vertices[vertex] = fromLeft;
        vertices[vertex + 1] = fromRight;
        vertices[vertex + 2] = toLeft;
        vertices[vertex + 3] = toRight;
        Color32 fromColor = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(fromAlpha) * 255f));
        Color32 toColor = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(toAlpha) * 255f));
        colors[vertex] = fromColor;
        colors[vertex + 1] = fromColor;
        colors[vertex + 2] = toColor;
        colors[vertex + 3] = toColor;

        Encapsulate(fromLeft);
        Encapsulate(fromRight);
        Encapsulate(toLeft);
        Encapsulate(toRight);

        nextSegment = (nextSegment + 1) % maxSegments;
        segmentCount = Mathf.Min(segmentCount + 1, maxSegments);
        dirty = true;
    }

    public void Clear()
    {
        if (!initialized) return;
        Array.Clear(vertices, 0, vertices.Length);
        Array.Clear(colors, 0, colors.Length);
        nextSegment = 0;
        segmentCount = 0;
        hasBounds = false;
        dirty = true;
    }

    private void LateUpdate()
    {
        if (!dirty || mesh == null) return;
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.bounds = hasBounds ? bounds : new Bounds(Vector3.zero, Vector3.zero);
        dirty = false;
    }

    private bool Initialize()
    {
        if (initialized) return material != null;
        initialized = true;

        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        transform.localScale = Vector3.one;

        int vertexCount = maxSegments * 4;
        vertices = new Vector3[vertexCount];
        colors = new Color32[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        int[] triangles = new int[maxSegments * 6];
        for (int segment = 0; segment < maxSegments; segment++)
        {
            int vertex = segment * 4;
            // x=幅方向、y=長さ方向。シェーダーは x で端をぼかします。
            uvs[vertex] = new Vector2(0f, 0f);
            uvs[vertex + 1] = new Vector2(1f, 0f);
            uvs[vertex + 2] = new Vector2(0f, 1f);
            uvs[vertex + 3] = new Vector2(1f, 1f);
            int triangle = segment * 6;
            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 2;
            triangles[triangle + 2] = vertex + 1;
            triangles[triangle + 3] = vertex + 1;
            triangles[triangle + 4] = vertex + 2;
            triangles[triangle + 5] = vertex + 3;
        }

        mesh = new Mesh { name = "TireMarks", hideFlags = HideFlags.DontSave };
        mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.MarkDynamic();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0, false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.zero);

        MeshFilter filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        // Resources参照により、ビルド時にも専用シェーダーが含まれます。
        Shader shader = Resources.Load<Shader>("TireMark");
        if (shader == null)
        {
            Debug.LogError("TireMarkRenderer requires the TireMark shader.", this);
            return false;
        }

        material = new Material(shader) { name = "TireMarks", hideFlags = HideFlags.DontSave };
        material.SetColor(ColorId, markColor);
        meshRenderer.sharedMaterial = material;
        return true;
    }

    private void Encapsulate(Vector3 point)
    {
        if (hasBounds)
        {
            bounds.Encapsulate(point);
        }
        else
        {
            bounds = new Bounds(point, Vector3.zero);
            hasBounds = true;
        }
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        DestroyOwned(mesh);
        DestroyOwned(material);
    }

    private static void DestroyOwned(UnityEngine.Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }
}
