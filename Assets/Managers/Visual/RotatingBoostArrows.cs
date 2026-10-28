using System.Collections.Generic;
using UnityEngine;

/// <summary>Two rotating curved arrows; the floor chevrons retain the fixed driving direction.</summary>
public sealed class RotatingBoostArrows : MonoBehaviour
{
    private Mesh mesh;
    private Material material;
    private float phase;
    private Quaternion baseRotation;
    public void Configure(Color color,float radius=.85f)
    {
        if(mesh!=null)return;
        baseRotation=transform.localRotation;
        var vertices=new List<Vector3>();var triangles=new List<int>();
        for(int half=0;half<2;half++)
        {
            float start=half*Mathf.PI;
            for(int i=0;i<24;i++)
            {
                float a=start+i*Mathf.PI/36f,b=start+(i+1)*Mathf.PI/36f;
                Quad(Point(a,radius*.83f),Point(a,radius),Point(b,radius),Point(b,radius*.83f));
            }
            float angle=start+24*Mathf.PI/36f;
            Vector3 tip=Point(angle+.35f,radius*.91f),inner=Point(angle,radius*.65f),outer=Point(angle,radius*1.18f);
            int n=vertices.Count;vertices.Add(inner);vertices.Add(outer);vertices.Add(tip);
            triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);triangles.Add(n+2);triangles.Add(n+1);triangles.Add(n);
        }
        mesh=new Mesh{name="Rotating boost arrows",hideFlags=HideFlags.DontSave};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        material=new Material(Shader.Find("Universal Render Pipeline/Unlit")){name="Rotating boost arrow glow",hideFlags=HideFlags.DontSave};
        material.SetColor("_BaseColor",color*1.4f);
        gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {int n=vertices.Count;vertices.AddRange(new[]{a,b,c,d});triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3,n+2,n+1,n,n+3,n+2,n});}
    }
    private static Vector3 Point(float angle,float radius)=>new Vector3(Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius,0f);
    private void Update(){if(UIMotion.IsPaused)return;phase+=Time.deltaTime;SetPhase(phase);}
    public void SetPhase(float seconds){phase=seconds;transform.localRotation=baseRotation*Quaternion.Euler(0,0,seconds*72f);}
    private void OnDestroy(){ItemVisualFactory.DestroyObject(mesh);ItemVisualFactory.DestroyObject(material);}
}
