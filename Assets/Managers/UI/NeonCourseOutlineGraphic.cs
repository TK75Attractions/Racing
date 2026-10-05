using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Thin course silhouette cached from the game's real centerline.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class NeonCourseOutlineGraphic : MaskableGraphic
{
    private readonly List<Vector3> path=new List<Vector3>();
    private Vector2 min,max;
    protected override void OnEnable(){base.OnEnable();raycastTarget=false;}
    public void Configure(RaceCourse course)
    {
        path.Clear();if(course!=null)course.CopyCenterPathWorld(path);
        min=new Vector2(float.MaxValue,float.MaxValue);max=new Vector2(float.MinValue,float.MinValue);
        foreach(Vector3 p in path){Vector2 q=new Vector2(p.x,p.z);min=Vector2.Min(min,q);max=Vector2.Max(max,q);}
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();if(path.Count<3)return;
        Rect r=rectTransform.rect;Vector2 range=max-min;
        float scale=Mathf.Min(r.width*.83f/Mathf.Max(1f,range.x),r.height*.83f/Mathf.Max(1f,range.y));
        float aa=1f/Mathf.Max(.1f,canvas!=null?canvas.scaleFactor:1f);
        for(int i=0;i<path.Count;i++)
        {
            Vector3 p=path[i],q=path[(i+1)%path.Count];
            Vector2 a=r.center+(new Vector2(p.x,p.z)-(min+max)*.5f)*scale;
            Vector2 b=r.center+(new Vector2(q.x,q.z)-(min+max)*.5f)*scale;
            RacingPanelGraphic.Line(vh,a,b,4f,Color.Lerp(new Color(.7f,.65f,1f),NeonUI.Cyan,i/(float)path.Count),aa);
        }
    }
}
