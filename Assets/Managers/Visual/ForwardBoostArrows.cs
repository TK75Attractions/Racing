using UnityEngine;

/// <summary>Chevrons travel along the pad's local forward direction, never around a ring.</summary>
public sealed class ForwardBoostArrows : MonoBehaviour
{
    private readonly Transform[] arrows = new Transform[3];
    private float length;
    public void Configure(Color color, float padLength, float width)
    {
        length = Mathf.Max(2f,padLength);
        for (int i=0;i<arrows.Length;i++)
        {
            var arrow = new GameObject("Forward chevron"); arrow.transform.SetParent(transform,false);
            arrows[i]=arrow.transform;
            var mesh = new Mesh { name="Forward boost chevron" };
            float x=Mathf.Min(1f,width*.28f);
            mesh.vertices=new[]{new Vector3(-x,0,-.6f),new Vector3(0,0,.3f),new Vector3(x,0,-.6f),new Vector3(x,0,-.15f),new Vector3(0,0,.75f),new Vector3(-x,0,-.15f)};
            mesh.triangles=new[]{0,5,1,5,4,1,1,3,2,1,4,3};mesh.RecalculateNormals();mesh.RecalculateBounds();
            arrow.AddComponent<MeshFilter>().sharedMesh=mesh;
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit")){color=color*1.4f};
            arrow.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
        SetPhase(0);
    }
    private void Update(){if(!UIMotion.IsPaused)SetPhase(Time.time);}
    public void SetPhase(float seconds)
    {
        for(int i=0;i<arrows.Length;i++)if(arrows[i]!=null)
            arrows[i].localPosition=new Vector3(0,0,Mathf.Repeat(seconds*3f+i*length/arrows.Length,length)-length*.5f);
    }
    private void OnDestroy()
    {
        foreach(var arrow in arrows)if(arrow!=null)
        {ItemVisualFactory.DestroyObject(arrow.GetComponent<MeshFilter>().sharedMesh);ItemVisualFactory.DestroyObject(arrow.GetComponent<MeshRenderer>().sharedMaterial);}
    }
}
