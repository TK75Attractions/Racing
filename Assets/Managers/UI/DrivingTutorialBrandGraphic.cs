using UnityEngine;
using UnityEngine.UI;

/// <summary>Diagonal racing ribbons behind the transparent brush wordmark.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DrivingTutorialBrandGraphic : MaskableGraphic
{
    protected override void OnEnable(){base.OnEnable();raycastTarget=false;}
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();Rect r=rectTransform.rect;
        for(int i=0;i<4;i++)
        {
            Color c=i==0?DrivingTutorialUI.Pink:i==1?NeonUI.Violet:DrivingTutorialUI.Cyan;c.a=i<2?.23f:.14f;
            float x=r.xMin-90f+i*47f,shift=r.height*.62f;int k=vh.currentVertCount;
            vh.AddVert(new Vector2(x,r.yMin),c,Vector2.zero);vh.AddVert(new Vector2(x+28f,r.yMin),c,Vector2.zero);
            vh.AddVert(new Vector2(x+28f+shift,r.yMax),c,Vector2.zero);vh.AddVert(new Vector2(x+shift,r.yMax),c,Vector2.zero);
            vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k,k+2,k+3);
        }
    }
}
