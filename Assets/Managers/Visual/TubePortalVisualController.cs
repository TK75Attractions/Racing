using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Two static, non-physical portal meshes derived from the tube's endpoint sections.</summary>
[DisallowMultipleComponent]
public sealed class TubePortalVisualController : MonoBehaviour
{
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<GameObject> roots = new List<GameObject>();
    private Material material;
    private static readonly Color Body = new Color(.018f, .025f, .05f, 0f);
    private static readonly Color Neon = new Color(.04f, .9f, 1.3f, 0f);
    private static readonly Color Reflector = new Color(.65f, .72f, .8f, 1f);
    private static readonly Color Lantern = new Color(1.25f, .48f, .13f, 0f);

    public void Build(MeshFilter tube, Vector3[] centers, bool reverse, Mesh readableSource = null)
    {
        Release();
        if (tube == null || centers == null || centers.Length < 2) return;
        Mesh sourceMesh = readableSource != null ? readableSource : tube.sharedMesh;
        if (sourceMesh == null || !sourceMesh.isReadable) return;
        Vector3[] source = sourceMesh.vertices;
        Vector2[] uv = sourceMesh.uv;
        if (source.Length == 0 || uv.Length != source.Length) return;
        Shader shader = Resources.Load<Shader>("TubeSpeedPatterns");
        if (shader == null) return;
        material = new Material(shader) { name = "Tube portal vertex glow", hideFlags = HideFlags.DontSave, renderQueue = 2001 };
        material.SetFloat("_PortalMode", 1f);
        material.SetFloat("_SurfaceOffset", 0f);
        material.SetFloat("_Cull", 2f);
        material.SetFloat("_ZWrite", 1f);
        RaceCourse course = GetComponentInParent<RaceCourse>();
        // The overlay is parented to the imported tube, not necessarily to RaceCourse.
        if (course == null) course = FindFirstObjectByType<RaceCourse>();
        for (int end = 0; end < 2; end++)
        {
            int r = end == 0 ? 0 : centers.Length - 1;
            int neighbor = end == 0 ? 1 : r - 1;
            Vector3 outward = (centers[r] - centers[neighbor]).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) continue;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            Vector3 center = centers[r];
            float floor = -10f;
            if (course != null) floor = Vector3.Dot(course.GetNearestPointOnCenterLineWorld(center) - center, up);
            float left = float.PositiveInfinity, rightExtent = float.NegativeInfinity, top = float.NegativeInfinity;
            float endpointUV = end == 0 ? float.PositiveInfinity : float.NegativeInfinity;
            foreach (Vector2 point in uv) endpointUV = end == 0 ? Mathf.Min(endpointUV, point.x) : Mathf.Max(endpointUV, point.x);
            for (int i = 0; i < source.Length; i++)
            {
                if (Mathf.Abs(uv[i].x - endpointUV) > .000001f) continue;
                Vector3 point = tube.transform.TransformPoint(source[i]) - center;
                left = Mathf.Min(left, Vector3.Dot(point, right));
                rightExtent = Mathf.Max(rightExtent, Vector3.Dot(point, right));
                top = Mathf.Max(top, Vector3.Dot(point, up));
            }
            if (float.IsInfinity(left)) continue;
            left -= .6f; rightExtent += .6f; top += .6f;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            // A rectangular gate surrounds the tube aperture; supports stay outside the pipe/road.
            foreach (float x in new[] { left, rightExtent })
            {
                Box(vertices, triangles, colors, center + right * x + up * ((floor + top) * .5f),
                    right * .65f, up * (top - floor + .65f), forward * .9f, Body);
                foreach (float face in new[] { -.48f, .48f })
                {
                    Box(vertices, triangles, colors, center + right * x + up * ((floor + top) * .5f) + forward * face,
                        right * .12f, up * (top - floor + .65f), forward * .08f, Neon);
                    for (float h = floor + .6f; h < Mathf.Min(floor + 5f, top); h += .8f)
                    {
                        Vector3 stripeRight = (right + up * .7f).normalized;
                        Vector3 stripeUp = Vector3.Cross(forward, stripeRight).normalized;
                        Box(vertices, triangles, colors, center + right * x + up * h + forward * (face * 1.12f),
                            stripeRight * .65f, stripeUp * .18f, forward * .035f, Reflector);
                    }
                }
            }
            float mid = (left + rightExtent) * .5f, width = rightExtent - left;
            Box(vertices, triangles, colors, center + right * mid + up * top, right * (width + .65f), up * .65f, forward * .9f, Body);
            foreach (float face in new[] { -.48f, .48f })
                Box(vertices, triangles, colors, center + right * mid + up * top + forward * face,
                    right * (width + .65f), up * .12f, forward * .08f, Neon);

            // Amber lantern blocks continue the ground lighting into the cyan portal.
            // Sample the actual approach road so the sequence follows slopes and bends.
            if (course != null)
            {
                float progress = course.GetProgressDistance(center);
                if (course.TryGetSampleAtProgress(progress, out var mouth))
                {
                    float sign = Vector3.Dot(mouth.forward, forward) < 0f ? -1f : 1f;
                    for (int n = 0; n < 4; n++)
                    {
                        if (!course.TryGetSampleAtProgress(progress + sign * (3f + n * 6f), out var sample)) continue;
                        foreach (float side in new[] { -1f, 1f })
                        {
                            Vector3 point = sample.position + sample.right * (side * (sample.width * .5f + 1.6f));
                            Box(vertices, triangles, colors, point + sample.up * .5f, sample.right * .28f, sample.up, sample.forward * .28f, Body);
                            Box(vertices, triangles, colors, point + sample.up * 1.25f, sample.right * .55f, sample.up * .65f, sample.forward * .55f,
                                Color.Lerp(Neon, Lantern, n / 3f));
                        }
                    }
                }
            }
            bool entry = (end == 0) != reverse;
            var root = new GameObject(entry ? "Tube entrance neon gate" : "Tube exit neon gate", typeof(MeshFilter), typeof(MeshRenderer))
                { hideFlags = HideFlags.DontSave, layer = tube.gameObject.layer };
            root.transform.SetParent(transform, false);
            roots.Add(root);
            var mesh = new Mesh { name = root.name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes.Add(mesh);
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    // Combined boxes, world-space dimensions converted to the overlay's local coordinates.
    private void Box(List<Vector3> vertices, List<int> triangles, List<Color> colors,
        Vector3 center, Vector3 x, Vector3 y, Vector3 z, Color color)
    {
        int start = vertices.Count;
        for (int i = 0; i < 8; i++)
        {
            vertices.Add(transform.InverseTransformPoint(center + x * ((i & 1) == 0 ? -.5f : .5f) +
                y * ((i & 2) == 0 ? -.5f : .5f) + z * ((i & 4) == 0 ? -.5f : .5f)));
            colors.Add(color);
        }
        // Axes form a right-handed basis (right, up, forward).
        int[] faces = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4,
            2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
        foreach (int index in faces) triangles.Add(start + index);
    }

    public void Release()
    {
        foreach (GameObject root in roots)
        {
            if (root == null) continue;
            root.SetActive(false); Dispose(root);
        }
        roots.Clear();
        foreach (Mesh mesh in meshes) Dispose(mesh);
        meshes.Clear(); Dispose(material); material = null;
    }
    private static void Dispose(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
    private void OnDestroy() => Release();
}
