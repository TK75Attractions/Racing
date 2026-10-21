using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Small shared batches for non-colliding campus scenery. No primitive GameObjects or random game state.</summary>
public sealed class CampusSceneryMesh
{
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector3> normals = new List<Vector3>();
    private readonly List<int>[] triangles;
    public int VertexCount => vertices.Count;
    public CampusSceneryMesh(int materials) { triangles = new List<int>[materials]; for(int i=0;i<materials;i++) triangles[i]=new List<int>(); }
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int material)
    {
        int start=vertices.Count; Vector3 cross=Vector3.Cross(b-a,c-a);
        if(cross.sqrMagnitude<.000001f)cross=Vector3.Cross(c-a,d-a);
        Vector3 n=cross.normalized;
        vertices.AddRange(new[]{a,b,c,d}); normals.AddRange(new[]{n,n,n,n});
        triangles[material].AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
    }
    public void Box(Vector3 center, Vector3 size, int material, float yaw=0)
    {
        Vector3 h=size*.5f; Quaternion rotation=Quaternion.Euler(0,yaw,0);
        Vector3[] p={new Vector3(-h.x,-h.y,-h.z),new Vector3(h.x,-h.y,-h.z),new Vector3(h.x,h.y,-h.z),new Vector3(-h.x,h.y,-h.z),
            new Vector3(-h.x,-h.y,h.z),new Vector3(h.x,-h.y,h.z),new Vector3(h.x,h.y,h.z),new Vector3(-h.x,h.y,h.z)};
        for(int i=0;i<p.Length;i++) p[i]=center+rotation*p[i];
        Quad(p[1],p[0],p[3],p[2],material); Quad(p[4],p[5],p[6],p[7],material);
        Quad(p[0],p[4],p[7],p[3],material); Quad(p[5],p[1],p[2],p[6],material);
        Quad(p[3],p[7],p[6],p[2],material); Quad(p[0],p[1],p[5],p[4],material);
    }
    public void Ellipsoid(Vector3 center, Vector3 radius, int material, float phase)
    {
        for(int j=0;j<4;j++) for(int i=0;i<8;i++)
        {
            float a=-Mathf.PI*.5f+j*Mathf.PI/4, b=a+Mathf.PI/4, u=phase+i*Mathf.PI/4,v=u+Mathf.PI/4;
            Vector3 P(float latitude,float longitude) => center+Vector3.Scale(radius,new Vector3(Mathf.Cos(latitude)*Mathf.Cos(longitude),Mathf.Sin(latitude),Mathf.Cos(latitude)*Mathf.Sin(longitude)));
            Quad(P(a,u),P(b,u),P(b,v),P(a,v),material);
        }
    }
    public void Branch(Vector3 from, Vector3 to, float baseRadius, float tipRadius, int material)
    {
        Vector3 axis = (to - from).normalized;
        if (axis.sqrMagnitude < .001f) return;
        Vector3 right = Vector3.Cross(axis, Mathf.Abs(axis.y) > .95f ? Vector3.forward : Vector3.up).normalized;
        Vector3 side = Vector3.Cross(axis, right);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f, b = (i + 1) * Mathf.PI / 3f;
            Vector3 u = right * Mathf.Cos(a) + side * Mathf.Sin(a);
            Vector3 v = right * Mathf.Cos(b) + side * Mathf.Sin(b);
            Quad(from + u * baseRadius, from + v * baseRadius, to + v * tipRadius, to + u * tipRadius, material);
        }
    }
    public void Ring(Vector3 center, Vector2 outer, Vector2 inner, int material)
    {
        for(int i=0;i<64;i++)
        {
            float a=i*Mathf.PI/32,b=(i+1)*Mathf.PI/32;
            Vector3 P(float angle,Vector2 r)=>center+new Vector3(Mathf.Cos(angle)*r.x,0,Mathf.Sin(angle)*r.y);
            Quad(P(a,inner),P(b,inner),P(b,outer),P(a,outer),material);
        }
    }
    public Mesh Build()
    {
        var mesh=new Mesh { name="Campus scenery (batched)",indexFormat=IndexFormat.UInt32 };
        mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.subMeshCount=triangles.Length;
        for(int i=0;i<triangles.Length;i++)mesh.SetTriangles(triangles[i],i);
        mesh.RecalculateBounds();return mesh;
    }
}
