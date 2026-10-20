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
        [Tooltip("チューブ内壁まで道路の両側を埋める対象。未指定のコースでは生成しません。")]
        public MeshCollider tubeInterior;

        [Min(0f), Tooltip("舗装端の幅 (World)。0で無効")]
        public float pavedEdgeWidth = 0.35f;
        [Min(0f), Tooltip("草の縁取り幅 (World)。0で無効")]
        public float grassEdgeWidth = 0.3f;

        [Tooltip("チューブ内を除き、ジャンプの両脇を含めて道路境界へ青いバリアを生成します。")]
        public bool generateBarriers;
        public Material barrierMaterial;

    }

    public const string RootName = "Generated Race Course Road";
    private const float PaintOffset = 0.01f;
    private const float EdgeOffset = 0.006f;
    private static Texture2D roadDetailTexture;
    private static Texture2D roadSpecTexture;

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
        List<Vector3> right, List<float> distances, bool closedLoop, Settings settings, List<bool> roadSegments)
    {
        TubeDriveSurface.Ensure(settings.tubeInterior);
        int count = left.Count;
        if (count < 2) return;
        float total = distances[count - 1];
        if (total <= Mathf.Epsilon) return;

        var vertices = new Vector3[count * 2];
        var uv = new Vector2[vertices.Length];
        var uv2 = new Vector2[vertices.Length];
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
                uv2[index] = new Vector2(0.5f + (side == 0 ? -0.25f : 0.25f), distances[i] / 64f);
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
            if (i == 0 || !roadSegments[i - 1] || (center[i] - center[i - 1]).sqrMagnitude < 0.00000001f) continue;
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
        surface.uv2 = uv2;
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
        surface.RecalculateTangents();
        Material asphaltMaterial = CreateRoadMaterial(settings.asphaltMaterial != null ? settings.asphaltMaterial : LoadMaterial("Asphalt"));
        GameObject asphalt = CreatePart(root.transform, "Road Surface", surface, new[] { asphaltMaterial });
        if (settings.generateCollider)
        {
            MeshCollider collider = asphalt.AddComponent<MeshCollider>();
            collider.convex = false;
            collider.isTrigger = false;
            collider.sharedMaterial = settings.colliderMaterial;
            collider.sharedMesh = surface;
        }

        BuildTubeShoulders(owner, root.transform, center, left, right, distances, roadSegments, settings);
        BuildRoadEdges(owner, root.transform, center, left, right, distances, roadSegments, settings);
        RaceCourseBarriers.Build(root.transform, center, left, right, closedLoop, settings);

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
                paint.Add(a, b, c, block * stripeLength, (block + 1) * stripeLength, -1, material == 0 ? 2 : 1);
                paint.Add(a, b, c, block * stripeLength, (block + 1) * stripeLength, 1, material == 0 ? 2 : 1);
            }
        }
        Mesh markings = paint.ToMesh();
        GameObject markingObject = CreatePart(root.transform, "Road Markings", markings, new[]
        {
            settings.whiteMaterial != null ? settings.whiteMaterial : LoadMaterial("White"),
            settings.redMaterial != null ? settings.redMaterial : LoadMaterial("Red"),
            CreateEmissiveCurbMaterial(settings.whiteMaterial != null ? settings.whiteMaterial : LoadMaterial("White"))
        });
        markingObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    private static Material CreateRoadMaterial(Material source)
    {
        if (source == null) return source;
        Material material = new Material(source) { name = "Generated Road Asphalt", hideFlags = HideFlags.DontSave };
        EnsureRoadTextures();
        material.SetTexture("_DetailAlbedoMap", roadDetailTexture);
        material.SetTextureScale("_DetailAlbedoMap", Vector2.one);
        material.SetFloat("_DetailAlbedoMapScale", 1f);
        material.EnableKeyword("_DETAIL_MULX2");
        material.SetTexture("_SpecGlossMap", roadSpecTexture);
        material.SetTextureScale("_SpecGlossMap", new Vector2(0.02f, 0.02f));
        material.EnableKeyword("_SPECGLOSSMAP");
        return material;
    }

    private static Material CreateEmissiveCurbMaterial(Material source)
    {
        if (source == null) return source;
        Material material = new Material(source) { name = "Generated Curb White", hideFlags = HideFlags.DontSave };
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", new Color(1.15f, 1.1f, 0.95f, 1f));
        return material;
    }

    private static void EnsureRoadTextures()
    {
        if (roadDetailTexture != null && roadSpecTexture != null) return;
        const int size = 128;
        roadDetailTexture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        { name = "Generated Low Frequency Asphalt Variation", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        roadSpecTexture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        { name = "Generated Asphalt Smoothness Variation", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var albedo = new Color[size * size];
        var specular = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x / (float)size, v = y / (float)size;
            float broad = PeriodicNoise(u, v, 6, 19) * 0.65f + PeriodicNoise(u, v, 17, 53) * 0.35f;
            float repair = Mathf.SmoothStep(0.62f, 0.78f, PeriodicNoise(u, v, 3, 97));
            float tire = Mathf.Max(TireTrack(u, 0.43f), TireTrack(u, 0.57f));
            float stain = Mathf.Clamp01((broad - 0.48f) * 1.4f);
            float value = Mathf.Clamp(0.51f - stain * 0.11f + repair * 0.045f - tire * 0.12f, 0.33f, 0.62f);
            int index = y * size + x;
            albedo[index] = new Color(value, value, value, 1f);
            float gloss = Mathf.Clamp(0.04f + tire * 0.045f + stain * 0.018f, 0.02f, 0.11f);
            specular[index] = new Color(0.2f, 0.2f, 0.2f, gloss);
        }
        roadDetailTexture.SetPixels(albedo); roadDetailTexture.Apply(false, true);
        roadSpecTexture.SetPixels(specular); roadSpecTexture.Apply(false, true);
    }

    private static float TireTrack(float u, float center)
    {
        float distance = Mathf.Abs(u - center);
        return 1f - Mathf.SmoothStep(0.012f, 0.045f, distance);
    }

    private static float PeriodicNoise(float u, float v, int cells, int seed)
    {
        float x = u * cells, y = v * cells;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float tx = Mathf.SmoothStep(0f, 1f, x - x0), ty = Mathf.SmoothStep(0f, 1f, y - y0);
        float a = HashNoise(x0, y0, cells, seed), b = HashNoise(x0 + 1, y0, cells, seed);
        float c = HashNoise(x0, y0 + 1, cells, seed), d = HashNoise(x0 + 1, y0 + 1, cells, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    private static float HashNoise(int x, int y, int cells, int seed)
    {
        x = (x % cells + cells) % cells; y = (y % cells + cells) % cells;
        uint n = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
        n = (n ^ (n >> 13)) * 1274126177u;
        return (n ^ (n >> 16)) / (float)uint.MaxValue;
    }

    private static void BuildRoadEdges(Transform owner, Transform root, List<Vector3> center,
        List<Vector3> left, List<Vector3> right, List<float> distances, List<bool> segments, Settings settings)
    {
        if (settings.pavedEdgeWidth <= 0f && settings.grassEdgeWidth <= 0f) return;
        int count = center.Count;
        var vertices = new List<Vector3>(count * 8);
        var uv = new List<Vector2>(count * 8);
        var uv2 = new List<Vector2>(count * 8);
        var pavement = new List<int>(); var grass = new List<int>();
        var previousGround = new bool[2];
        var groundBySide = new bool[2];
        var row = new Vector3[3];
        MeshCollider wall = settings.tubeInterior;
        for (int i = 0; i < count; i++)
        {
            Vector3 side = (right[i] - left[i]).normalized;
            float halfWidth = Vector3.Distance(left[i], right[i]) * 0.5f;
            bool inTube = wall != null && wall.enabled && wall.Raycast(new Ray(center[i], -side), out RaycastHit lh, 25f) &&
                wall.Raycast(new Ray(center[i], side), out RaycastHit rh, 25f) && lh.distance >= halfWidth - .1f && rh.distance >= halfWidth - .1f;
            groundBySide[0] = groundBySide[1] = false;
            bool adjacentRoad = (i > 0 && segments[i - 1]) || (i < count - 1 && segments[i]);
            if (!inTube && adjacentRoad)
            {
                for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                {
                    float sign = sideIndex == 0 ? -1f : 1f;
                    Vector3 probe = center[i] + side * (sign * (halfWidth + 0.6f)) + Vector3.up * 0.2f;
                    groundBySide[sideIndex] = Physics.Raycast(probe, Vector3.down, out RaycastHit ground, 1.2f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        && Mathf.Abs(ground.point.y - center[i].y) < 0.35f;
                }
            }
            int first = vertices.Count;
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? -1f : 1f;
                Vector3 roadEdge = s == 0 ? left[i] : right[i];
                Vector3 pavementOuter = roadEdge + side * (sign * settings.pavedEdgeWidth) + Vector3.up * EdgeOffset;
                Vector3 grassOuter = pavementOuter + side * (sign * settings.grassEdgeWidth);
                row[0] = roadEdge + Vector3.up * EdgeOffset; row[1] = pavementOuter; row[2] = grassOuter;
                for (int band = 0; band < 3; band++)
                {
                    Vector3 point = row[band]; vertices.Add(owner.InverseTransformPoint(point));
                    uv.Add(new Vector2(sign * (halfWidth + settings.pavedEdgeWidth + settings.grassEdgeWidth), distances[i]));
                    uv2.Add(new Vector2(0.5f + sign * 0.25f, distances[i] / 64f));
                }
            }
            if (i == 0 || !segments[i - 1]) { previousGround[0] = groundBySide[0]; previousGround[1] = groundBySide[1]; continue; }
            for (int s = 0; s < 2; s++)
            {
                if (!groundBySide[s] || !previousGround[s]) continue;
                int a = first - 6 + s * 3, b = first + s * 3;
                AddQuad(pavement, a, b, a + 1, b + 1, s == 1);
                AddQuad(grass, a + 1, b + 1, a + 2, b + 2, s == 1);
            }
            previousGround[0] = groundBySide[0]; previousGround[1] = groundBySide[1];
        }
        if (pavement.Count == 0 && grass.Count == 0) return;
        Mesh mesh = NewMesh("RaceCourseRoadEdges", vertices.Count);
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetUVs(1, uv2); mesh.subMeshCount = 2;
        mesh.SetTriangles(pavement, 0); mesh.SetTriangles(grass, 1); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        Material source = settings.asphaltMaterial != null ? settings.asphaltMaterial : LoadMaterial("Asphalt");
        Material edge = new Material(source) { name = "Generated Pavement Edge", hideFlags = HideFlags.DontSave };
        edge.SetColor("_BaseColor", new Color(0.63f, 0.63f, 0.61f, 1f)); edge.SetFloat("_Smoothness", 0.06f);
        Material grassMaterial = new Material(source) { name = "Generated Grass Verge", hideFlags = HideFlags.DontSave };
        grassMaterial.SetColor("_BaseColor", new Color(0.16f, 0.24f, 0.14f, 1f)); grassMaterial.SetFloat("_Smoothness", 0.08f);
        GameObject edgePart = CreatePart(root, "Pavement Edge and Grass", mesh, new[] { edge, grassMaterial });
        edgePart.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    private static void AddQuad(List<int> triangles, int a, int b, int c, int d, bool reverse)
    {
        if (!reverse) triangles.AddRange(new[] { a, c, b, c, d, b });
        else triangles.AddRange(new[] { a, b, c, c, b, d });
    }

    private static void BuildTubeShoulders(Transform owner, Transform root, List<Vector3> center,
        List<Vector3> left, List<Vector3> right, List<float> distances, List<bool> segments, Settings settings)
    {
        MeshCollider wall = settings.tubeInterior;
        if (wall == null || !wall.enabled) return;
        var vertices = new Vector3[center.Count * 4];
        var uv = new Vector2[vertices.Length];
        var triangles = new List<int>();
        bool previousFilled = false;
        for (int i = 0; i < center.Count; i++)
        {
            Vector3 side = (right[i] - left[i]).normalized;
            float halfWidth = Vector3.Distance(left[i], right[i]) * .5f;
            bool hitLeft = wall.Raycast(new Ray(center[i], -side), out RaycastHit lh, 25f);
            bool hitRight = wall.Raycast(new Ray(center[i], side), out RaycastHit rh, 25f);
            bool filled = hitLeft && hitRight && lh.distance >= halfWidth - .1f && rh.distance >= halfWidth - .1f;
            Vector3 outerLeft = filled ? lh.point - side * .05f : left[i];
            Vector3 outerRight = filled ? rh.point + side * .05f : right[i];
            Vector3[] row = { outerLeft, left[i], right[i], outerRight };
            for (int j = 0; j < 4; j++)
            {
                vertices[i * 4 + j] = owner.InverseTransformPoint(row[j]);
                uv[i * 4 + j] = new Vector2(Vector3.Dot(row[j] - center[i], side), distances[i]);
            }
            if (i > 0 && segments[i - 1] && (filled || previousFilled))
            {
                int a = (i - 1) * 4, b = i * 4;
                triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1,
                    a + 2, b + 2, a + 3, a + 3, b + 2, b + 3 });
            }
            previousFilled = filled;
        }
        if (triangles.Count == 0) return;
        Mesh mesh = NewMesh("TubeRoadShoulders", vertices.Length);
        mesh.vertices = vertices; mesh.uv = uv; mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
        GameObject part = CreatePart(root, "Tube Road Shoulders", mesh,
            new[] { settings.asphaltMaterial != null ? settings.asphaltMaterial : LoadMaterial("Asphalt") });
        if (settings.generateCollider)
        {
            MeshCollider collider = part.AddComponent<MeshCollider>();
            collider.sharedMaterial = settings.colliderMaterial;
            collider.sharedMesh = mesh;
        }
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
                if (filter.GetComponentInParent<RaceBarrierPath>() != null) continue; // The path owns and releases both meshes.
                Mesh mesh = filter.sharedMesh;
                if (mesh != null && (mesh.hideFlags & HideFlags.DontSave) == HideFlags.DontSave) DestroyGenerated(mesh);
            }
            var generatedMaterials = new HashSet<Material>();
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null && (material.hideFlags & HideFlags.DontSave) == HideFlags.DontSave) generatedMaterials.Add(material);
            foreach (Material material in generatedMaterials) DestroyGenerated(material);
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
        private readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };
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
            mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
