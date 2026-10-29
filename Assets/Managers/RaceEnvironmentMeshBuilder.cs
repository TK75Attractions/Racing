using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Combined solid boxes, in the coordinate space supplied by the caller.</summary>
public sealed class RaceEnvironmentMeshBuilder
{
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> triangles = new List<int>();

    public void Box(Vector3 center, Vector3 x, Vector3 y, Vector3 z, Color color)
    {
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
            corners[i] = center + x * ((i & 1) == 0 ? -.5f : .5f) +
                y * ((i & 2) == 0 ? -.5f : .5f) + z * ((i & 4) == 0 ? -.5f : .5f);
        int[] faces = { 0,4,6,2, 1,3,7,5, 0,1,5,4, 2,6,7,3, 0,2,3,1, 4,5,7,6 };
        bool mirrored = Vector3.Dot(Vector3.Cross(x, y), z) < 0f;
        for (int face = 0; face < 6; face++)
        {
            int start = vertices.Count;
            Vector3 normal = Vector3.Cross(corners[faces[face*4+1]] - corners[faces[face*4]],
                corners[faces[face*4+2]] - corners[faces[face*4]]).normalized * (mirrored ? -1f : 1f);
            Color shaded = color * (.68f + .32f * Mathf.Max(0f, Vector3.Dot(normal, new Vector3(.3f,.8f,.5f).normalized)));
            shaded.a = 1f;
            for (int n = 0; n < 4; n++) { vertices.Add(corners[faces[face*4+n]]); colors.Add(shaded); }
            triangles.AddRange(mirrored ? new[] { start, start+2, start+1, start, start+3, start+2 } :
                new[] { start, start+1, start+2, start, start+2, start+3 });
        }
    }

    public Mesh ToMesh(string name)
    {
        var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
}
