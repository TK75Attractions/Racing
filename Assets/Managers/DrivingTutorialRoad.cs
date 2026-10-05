using System.Collections.Generic;
using UnityEngine;

/// <summary>A continuous road ribbon; its single flat floor is the only drivable collider.</summary>
public sealed class DrivingTutorialRoad : MonoBehaviour
{
    public const float Width = 24f;
    private readonly List<Mesh> meshes = new List<Mesh>();

    public static void Build(Transform parent, Material asphalt, Material cyan, Material pink, Material white)
    {
        GameObject road = new GameObject("Continuous practice road");
        road.transform.SetParent(parent, false);
        DrivingTutorialRoad owner = road.AddComponent<DrivingTutorialRoad>();
        // Fine analytic sampling joins the straight, quarter circle and exit with matching tangents.
        float length = 70f + 30f * Mathf.PI * .5f + 80f;
        int count = Mathf.CeilToInt(length / .65f);
        var left = new List<Vector3>(); var right = new List<Vector3>();
        var outsideLeft = new List<Vector3>(); var outsideRight = new List<Vector3>();
        for (int i = 0; i <= count; i++)
        {
            Sample(length * i / count, out Vector3 center, out Vector3 tangent);
            Vector3 side = Vector3.Cross(Vector3.up, tangent);
            left.Add(center - side * Width * .5f + Vector3.up * .006f);
            right.Add(center + side * Width * .5f + Vector3.up * .006f);
            outsideLeft.Add(center - side * (Width * .5f + .4f));
            outsideRight.Add(center + side * (Width * .5f + .4f));
        }
        owner.Ribbon("Asphalt", left, right, asphalt);
        owner.Barrier("Cyan barrier", left, outsideLeft, cyan);
        owner.Barrier("Pink barrier", right, outsideRight, pink);
        for (float distance = 0f; distance < length - 4f; distance += 8f)
        {
            var dashLeft = new List<Vector3>(); var dashRight = new List<Vector3>();
            for (int i = 0; i <= 8; i++)
            {
                Sample(distance + i * .5f, out Vector3 center, out Vector3 tangent);
                Vector3 side = Vector3.Cross(Vector3.up, tangent) * .11f;
                dashLeft.Add(center - side + Vector3.up * .016f); dashRight.Add(center + side + Vector3.up * .016f);
            }
            owner.Ribbon($"Lane dash {distance:0}", dashLeft, dashRight, white);
        }
    }

    public static void Sample(float distance, out Vector3 center, out Vector3 tangent)
    {
        float turnLength = 30f * Mathf.PI * .5f;
        if (distance <= 70f)
        {
            center = new Vector3(0f, 0f, -64f + distance); tangent = Vector3.forward;
        }
        else if (distance <= 70f + turnLength)
        {
            float angle = (distance - 70f) / 30f;
            center = new Vector3(30f - Mathf.Cos(angle) * 30f, 0f, 6f + Mathf.Sin(angle) * 30f);
            tangent = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        }
        else
        {
            center = new Vector3(30f + distance - 70f - turnLength, 0f, 36f); tangent = Vector3.right;
        }
    }

    private void Ribbon(string name, List<Vector3> a, List<Vector3> b, Material material)
    {
        var vertices = new Vector3[a.Count * 2]; var uv = new Vector2[vertices.Length];
        var triangles = new int[(a.Count - 1) * 6];
        for (int i = 0; i < a.Count; i++)
        {
            vertices[i * 2] = a[i]; vertices[i * 2 + 1] = b[i];
            uv[i * 2] = new Vector2(0f, i); uv[i * 2 + 1] = new Vector2(1f, i);
            if (i == a.Count - 1) continue;
            int v = i * 2, t = i * 6;
            triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        CreateMesh(name, vertices, uv, triangles, material, false);
    }

    private void Barrier(string name, List<Vector3> inner, List<Vector3> outer, Material material)
    {
        // Closed vertical ribbon with continuous seams. Only the walls have mesh colliders.
        var vertices = new Vector3[inner.Count * 4]; var uv = new Vector2[vertices.Length];
        var triangles = new List<int>();
        for (int i = 0; i < inner.Count; i++)
        {
            int v = i * 4;
            vertices[v] = new Vector3(inner[i].x, 0f, inner[i].z);
            vertices[v + 1] = new Vector3(outer[i].x, 0f, outer[i].z);
            vertices[v + 2] = vertices[v] + Vector3.up * .9f;
            vertices[v + 3] = vertices[v + 1] + Vector3.up * .9f;
            if (i + 1 == inner.Count) continue;
            // Front and back faces face the two sides of the wall; no opposing triangles share normals.
            AddFace(triangles, v, v + 4, v + 6, v + 2);
            AddFace(triangles, v + 1, v + 3, v + 7, v + 5);
            AddFace(triangles, v + 2, v + 6, v + 7, v + 3);
        }
        CreateMesh(name, vertices, uv, triangles.ToArray(), material, true);
    }

    private static void AddFace(List<int> triangles, int a, int b, int c, int d)
    {
        triangles.AddRange(new[] { a, b, c, a, c, d });
    }
    private void CreateMesh(string name, Vector3[] vertices, Vector2[] uv, int[] triangles, Material material, bool solid)
    {
        Mesh mesh = new Mesh { name = name, vertices = vertices, uv = uv, triangles = triangles };
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes.Add(mesh);
        GameObject part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        part.transform.SetParent(transform, false);
        part.GetComponent<MeshFilter>().sharedMesh = mesh;
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
        if (solid) part.AddComponent<MeshCollider>().sharedMesh = mesh;
    }
    private void OnDestroy() { foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh); }
}
