using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Builds the road from the exact triangles used by RaceCourse's band test.</summary>
public static class RaceCourseRoad
{
    [Serializable]
    public sealed class Settings
    {
        [Min(0.01f), Tooltip("中央の白線の幅 (World)")]
        public float centerLineWidth = 0.2f;
        [Min(0.1f), Tooltip("中央の白線1本の長さ (World)")]
        public float dashLength = 3f;
        [Min(0.1f), Tooltip("中央の白線の間隔 (World)")]
        public float dashGap = 3f;
        [Min(0f), Tooltip("道路幅の内側に描く赤白模様の幅 (World)。0で非表示")]
        public float curbWidth = 0.6f;
        [Min(0.1f), Tooltip("赤または白1区画の長さ (World)")]
        public float curbStripeLength = 2f;
        public Material asphaltMaterial;
        public Material whiteMaterial;
        public Material redMaterial;
        public bool generateCollider = true;
        public PhysicsMaterial colliderMaterial;
    }

    public const string RootName = "Generated Race Course Road";
    private const float PaintOffset = 0.01f;

    private struct Vertex
    {
        public Vector3 position;
        public float distance;
        public float side;
        public float halfWidth;
        public float lineHalfWidth;
        public float curbWidth;

        public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex
        {
            position = Vector3.Lerp(a.position, b.position, t),
            distance = Mathf.Lerp(a.distance, b.distance, t),
            side = Mathf.Lerp(a.side, b.side, t),
            halfWidth = Mathf.Lerp(a.halfWidth, b.halfWidth, t),
            lineHalfWidth = Mathf.Lerp(a.lineHalfWidth, b.lineHalfWidth, t),
            curbWidth = Mathf.Lerp(a.curbWidth, b.curbWidth, t)
        };
    }

    public static void Build(Transform owner, List<Vector3> center, List<Vector3> left,
        List<Vector3> right, List<float> distances, bool closedLoop, Settings settings)
    {
        int count = left.Count;
        if (count < 2) return;
        float total = distances[count - 1];
        if (total <= Mathf.Epsilon) return;

        var vertices = new Vector3[count * 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new List<int>((count - 1) * 6);
        var samples = new Vertex[vertices.Length];
        for (int i = 0; i < count; i++)
        {
            float halfWidth = Vector3.Distance(left[i], right[i]) * 0.5f;
            for (int side = 0; side < 2; side++)
            {
                int index = i * 2 + side;
                Vector3 position = side == 0 ? left[i] : right[i];
                vertices[index] = owner.InverseTransformPoint(position);
                uv[index] = new Vector2(side * halfWidth * 2f, distances[i]);
                samples[index] = new Vertex
                {
                    position = position,
                    distance = distances[i],
                    side = side == 0 ? -halfWidth : halfWidth,
                    halfWidth = halfWidth,
                    // Keep all decoration within even very narrow road sections.
                    lineHalfWidth = Mathf.Min(Mathf.Max(0.01f, settings.centerLineWidth) * 0.5f, halfWidth * 0.1f),
                    curbWidth = Mathf.Min(Mathf.Max(0f, settings.curbWidth), halfWidth * 0.4f)
                };
            }
            if (i == 0 || (center[i] - center[i - 1]).sqrMagnitude < 0.00000001f) continue;
            int previous = (i - 1) * 2;
            AddTriangle(triangles, samples, previous, previous + 2, previous + 1);
            AddTriangle(triangles, samples, previous + 1, previous + 2, previous + 3);
        }
        if (triangles.Count == 0) return;

        GameObject root = new GameObject(RootName) { hideFlags = HideFlags.DontSave, layer = owner.gameObject.layer };
        root.transform.SetParent(owner, false);
        Mesh surface = NewMesh("RaceCourseRoadSurface", vertices.Length);
        surface.vertices = vertices;
        surface.uv = uv;
        surface.SetTriangles(triangles, 0);
        surface.RecalculateNormals();
        if (closedLoop)
        {
            Vector3[] normals = surface.normals;
            for (int side = 0; side < 2; side++)
            {
                int last = (count - 1) * 2 + side;
                normals[side] = normals[last] = (normals[side] + normals[last]).normalized;
            }
            surface.normals = normals;
        }
        surface.RecalculateBounds();
        GameObject asphalt = CreatePart(root.transform, "Road Surface", surface,
            new[] { settings.asphaltMaterial != null ? settings.asphaltMaterial : LoadMaterial("Asphalt") });
        if (settings.generateCollider)
        {
            MeshCollider collider = asphalt.AddComponent<MeshCollider>();
            collider.convex = false;
            collider.isTrigger = false;
            collider.sharedMaterial = settings.colliderMaterial;
            collider.sharedMesh = surface;
        }

        // Paint is clipped against each surface triangle, so it follows curves, width changes
        // and slopes without crossing below a twisted road quad. Only the road has a collider.
        float period = Mathf.Max(0.1f, settings.dashLength) + Mathf.Max(0.1f, settings.dashGap);
        float dashFraction = Mathf.Max(0.1f, settings.dashLength) / period;
        float stripeLength = Mathf.Max(0.1f, settings.curbStripeLength);
        if (closedLoop)
        {
            period = total / Mathf.Max(1, Mathf.RoundToInt(total / period));
            // An even number of curb blocks makes the final white block meet the first red.
            stripeLength = total / (2f * Mathf.Max(1, Mathf.RoundToInt(total / (2f * stripeLength))));
        }
        var paint = new PaintBuilder(owner);
        for (int index = 0; index < triangles.Count; index += 3)
        {
            Vertex a = samples[triangles[index]];
            Vertex b = samples[triangles[index + 1]];
            Vertex c = samples[triangles[index + 2]];
            float start = Mathf.Min(a.distance, Mathf.Min(b.distance, c.distance));
            float end = Mathf.Max(a.distance, Mathf.Max(b.distance, c.distance));
            for (int block = Mathf.FloorToInt(start / period); block * period < end; block++)
                paint.Add(a, b, c, block * period, (block + dashFraction) * period, 0, 0);
            if (settings.curbWidth <= 0f) continue;
            for (int block = Mathf.FloorToInt(start / stripeLength); block * stripeLength < end; block++)
            {
                int material = block % 2 == 0 ? 1 : 0;
                paint.Add(a, b, c, block * stripeLength, (block + 1) * stripeLength, -1, material);
                paint.Add(a, b, c, block * stripeLength, (block + 1) * stripeLength, 1, material);
            }
        }
        Mesh markings = paint.ToMesh();
        GameObject markingObject = CreatePart(root.transform, "Road Markings", markings, new[]
        {
            settings.whiteMaterial != null ? settings.whiteMaterial : LoadMaterial("White"),
            settings.redMaterial != null ? settings.redMaterial : LoadMaterial("Red")
        });
        markingObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    private static void AddTriangle(List<int> triangles, Vertex[] vertices, int a, int b, int c)
    {
        if (Vector3.Cross(vertices[b].position - vertices[a].position,
            vertices[c].position - vertices[a].position).sqrMagnitude < 0.00000001f) return;
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
    }

    private static Material LoadMaterial(string name) => Resources.Load<Material>("RaceCourse/M_Road" + name);

    private static Mesh NewMesh(string name, int count) => new Mesh
    {
        name = name,
        hideFlags = HideFlags.DontSave,
        indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
    };

    private static GameObject CreatePart(Transform parent, string name, Mesh mesh, Material[] materials)
    {
        GameObject part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer))
        { hideFlags = HideFlags.DontSave, layer = parent.gameObject.layer };
        part.transform.SetParent(parent, false);
        part.GetComponent<MeshFilter>().sharedMesh = mesh;
        part.GetComponent<MeshRenderer>().sharedMaterials = materials;
        return part;
    }

    /// <summary>Also finds transient meshes after a script reload loses managed references.</summary>
    public static void Clear(Transform owner)
    {
        for (int i = owner.childCount - 1; i >= 0; i--)
        {
            GameObject root = owner.GetChild(i).gameObject;
            if (root.name != RootName || (root.hideFlags & HideFlags.DontSave) != HideFlags.DontSave) continue;
            root.SetActive(false);
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh != null && (mesh.hideFlags & HideFlags.DontSave) == HideFlags.DontSave) DestroyGenerated(mesh);
            }
            DestroyGenerated(root);
        }
    }

    private static void DestroyGenerated(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }

    private sealed class PaintBuilder
    {
        private readonly Transform owner;
        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<int>[] triangles = { new List<int>(), new List<int>() };
        private List<Vertex> polygon = new List<Vertex>(8);
        private List<Vertex> buffer = new List<Vertex>(8);

        public PaintBuilder(Transform owner) => this.owner = owner;

        public void Add(Vertex a, Vertex b, Vertex c, float start, float end, int side, int material)
        {
            polygon.Clear(); polygon.Add(a); polygon.Add(b); polygon.Add(c);
            Clip(v => start - v.distance);
            Clip(v => v.distance - end);
            if (side == 0)
            {
                Clip(v => -v.side - v.lineHalfWidth);
                Clip(v => v.side - v.lineHalfWidth);
            }
            else if (side < 0) Clip(v => v.side + v.halfWidth - v.curbWidth);
            else Clip(v => v.halfWidth - v.side - v.curbWidth);
            if (polygon.Count < 3) return;
            int first = positions.Count;
            foreach (Vertex vertex in polygon)
            {
                positions.Add(owner.InverseTransformPoint(vertex.position + Vector3.up * PaintOffset));
                uv.Add(new Vector2(vertex.side, vertex.distance));
            }
            for (int i = 1; i < polygon.Count - 1; i++)
            {
                if (Vector3.Cross(polygon[i].position - polygon[0].position,
                    polygon[i + 1].position - polygon[0].position).sqrMagnitude < 0.000000000001f) continue;
                triangles[material].Add(first);
                triangles[material].Add(first + i);
                triangles[material].Add(first + i + 1);
            }
        }

        // Sutherland-Hodgman clipping. Boundary functions are linear in the interpolated data.
        private void Clip(Func<Vertex, float> boundary)
        {
            buffer.Clear();
            if (polygon.Count == 0) return;
            Vertex previous = polygon[polygon.Count - 1];
            float previousValue = boundary(previous);
            foreach (Vertex current in polygon)
            {
                float value = boundary(current);
                if ((value <= 0f) != (previousValue <= 0f))
                    buffer.Add(Vertex.Lerp(previous, current, previousValue / (previousValue - value)));
                if (value <= 0f) buffer.Add(current);
                previous = current;
                previousValue = value;
            }
            List<Vertex> swap = polygon; polygon = buffer; buffer = swap;
        }

        public Mesh ToMesh()
        {
            Mesh mesh = NewMesh("RaceCourseRoadMarkings", positions.Count);
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 2;
            for (int i = 0; i < 2; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
