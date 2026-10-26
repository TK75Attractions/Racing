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

        [Tooltip("チューブ内を除き、ジャンプの両脇を含めて道路境界へ青いバリアを生成します。")]
        public bool generateBarriers;
        public Material barrierMaterial;

    }

    public const string RootName = "Generated Race Course Road";
    public const int ShoulderSegments = 16;
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
        List<Vector3> right, List<float> distances, bool closedLoop, Settings settings, List<bool> roadSegments)
    {
        TubeDriveSurface.Ensure(settings.tubeInterior);
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

        BuildTubeShoulders(owner, root.transform, center, left, right, distances, roadSegments, settings);
        RaceCourseBarriers.Build(root.transform, center, left, right, closedLoop, settings);
        RaceJumpGuidance.Build(owner, root.transform, left, right, distances, roadSegments, closedLoop);

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

    private struct ShoulderRow
    {
        public Vector3 edge, center, side;
        public float distance;
        public bool inside, portal, connect;
    }
    private static void BuildTubeShoulders(Transform owner, Transform root, List<Vector3> center,
        List<Vector3> left, List<Vector3> right, List<float> distances, List<bool> segments, Settings settings)
    {
        MeshCollider tube = settings.tubeInterior;
        if (tube == null || !tube.enabled) return;
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        foreach (int sign in new[] {-1, 1})
        {
            var edge = sign < 0 ? left : right;
            var rows = new List<ShoulderRow>();
            ShoulderRow At(int i, float t)
            {
                var row = new ShoulderRow { edge = Vector3.Lerp(edge[i-1],edge[i],t), center = Vector3.Lerp(center[i-1],center[i],t),
                    side = (Vector3.Lerp(right[i-1],right[i],t)-Vector3.Lerp(left[i-1],left[i],t)).normalized,
                    distance = Mathf.Lerp(distances[i-1],distances[i],t), connect = segments[i-1] };
                row.inside = TubePortalProfile.IsInsideAtEdge(tube,row.edge,row.center,row.side); return row;
            }
            rows.Add(At(1,0));
            for (int i=1;i<center.Count;i++)
            {
                bool near = TubePortalProfile.TryGetEndFrame(tube,edge[i],out _,out _);
                int steps = near ? Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(edge[i-1],edge[i])/.4f)) : 1;
                for (int step=1;step<=steps;step++)
                {
                    var next = At(i,step/(float)steps); var previous = rows[rows.Count-1];
                    if (segments[i-1] && previous.inside != next.inside)
                    {
                        float low=(step-1)/(float)steps, high=step/(float)steps;
                        for (int n=0;n<18;n++) { float t=(low+high)*.5f; if (At(i,t).inside==previous.inside) low=t; else high=t; }
                        var portal=At(i,(low+high)*.5f); portal.inside=true; portal.portal=true; rows.Add(portal);
                    }
                    rows.Add(next);
                }
            }
            var portals = rows.FindAll(row => row.portal);
            bool lastFilled=false; int lastIndex=-1;
            foreach (var row in rows)
            {
                Vector3 outer=row.edge; bool filled=row.inside;
                Vector3 outward=row.side*sign, probe=row.edge;
                if (filled && TubePortalProfile.TryGetEndFrame(tube,row.edge,out var mouth,out var normal))
                {
                    Vector3 portalSide=Vector3.Cross(Vector3.up,normal).normalized;
                    if (Vector3.Dot(portalSide,outward)<0) portalSide=-portalSide;
                    float t=Mathf.SmoothStep(0,1,Mathf.Clamp01(-Vector3.Dot(row.edge-mouth,normal)/8));
                    outward=Vector3.Slerp(portalSide,outward,t).normalized;
                    // Continue the tube's longitudinal tangent through the paved join.
                    // Let the outer floor regain the road grade over eight metres,
                    // while its inner edge stays exactly on the existing road.
                    float best=float.PositiveInfinity, height=row.edge.y;
                    foreach (var portal in portals)
                    {
                        float delta=(portal.edge-row.edge).sqrMagnitude;
                        if(delta<best) { best=delta; height=portal.edge.y; }
                    }
                    probe.y=Mathf.Lerp(height,row.edge.y,t);
                }
                if (filled && row.portal)
                {
                    var profile=TubePortalProfile.Create(tube,row.edge,outward,32);
                    if (profile!=null) outer=profile.points[0]; else filled=false;
                }
                else if (filled)
                {
                    if (tube.Raycast(new Ray(probe-outward*.05f,outward),out var hit,25)) outer=hit.point;
                    else filled=false;
                }
                int index=vertices.Count;
                for (int k=0;k<=ShoulderSegments;k++)
                {
                    float t=k/(float)ShoulderSegments;
                    vertices.Add(owner.InverseTransformPoint(Vector3.Lerp(row.edge,outer,t)));
                    uv.Add(new Vector2(Vector3.Distance(row.edge,outer)*t,row.distance));
                }
                if (filled && lastFilled && row.connect && lastIndex>=0)
                {
                    int a=lastIndex,b=index;
                    bool flip=Vector3.Cross(vertices[b]-vertices[a],vertices[a+ShoulderSegments]-vertices[a]+vertices[b+ShoulderSegments]-vertices[b]).y<0;
                    for(int k=0;k<ShoulderSegments;k++)
                    {
                        int c=a+k,d=b+k;
                        triangles.AddRange(flip ? new[] {c,c+1,d,c+1,d+1,d} : new[] {c,d,c+1,c+1,d,d+1});
                    }
                }
                lastFilled=filled; lastIndex=index;
            }
        }
        if (triangles.Count==0) return;
        Mesh mesh=NewMesh("TubeRoadShoulders",vertices.Count);
        mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetTriangles(triangles,0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
        GameObject part=CreatePart(root,"Tube Road Shoulders",mesh,
            new[] {settings.asphaltMaterial!=null ? settings.asphaltMaterial : LoadMaterial("Asphalt")});
        if (settings.generateCollider)
        {
            var collider=part.AddComponent<MeshCollider>(); collider.sharedMaterial=settings.colliderMaterial; collider.sharedMesh=mesh;
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
