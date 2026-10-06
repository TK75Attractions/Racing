using UnityEngine;
using UnityEngine.UI;

/// <summary>Finite highlight sweep and confirmation sparks; the settled state has no mesh work.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingButtonFXGraphic : MaskableGraphic
{
    private float sweep, burst;
    private Color tint;
    protected override void OnEnable(){base.OnEnable();raycastTarget=false;}
    public void SetEffect(float highlightSweep,float confirmation,Color accent)
    {
        if(Mathf.Approximately(sweep,highlightSweep)&&Mathf.Approximately(burst,confirmation)&&tint==accent)return;
        sweep=highlightSweep;burst=confirmation;tint=accent;SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();Rect r=rectTransform.rect;float cut=Mathf.Min(24f,r.height*.20f);
        if(sweep>0f && sweep<1f)
        {
            float x=Mathf.Lerp(r.xMin+cut,r.xMax-cut,sweep);
            Color c=new Color(.75f,.91f,1f,Mathf.Sin(sweep*Mathf.PI)*.19f);
            RacingPanelGraphic.Line(vh,new Vector2(Mathf.Clamp(x-r.height*.25f,r.xMin+cut,r.xMax-cut),r.yMin+cut),new Vector2(Mathf.Clamp(x+r.height*.25f,r.xMin+cut,r.xMax-cut),r.yMax-cut),14f,c,5f);
        }
        if(burst<=.001f)return;
        Color light=Color.Lerp(tint,Color.white,.65f);light.a=burst*.95f;
        for(int i=0;i<4;i++)
        {
            float x=r.xMin+cut+i*10f;
            RacingPanelGraphic.Line(vh,new Vector2(x,r.yMax+3),new Vector2(x-5f,r.yMax+6f+burst*11f),2f,light);
            x=r.xMax-cut-i*10f;
            RacingPanelGraphic.Line(vh,new Vector2(x,r.yMin-3),new Vector2(x+5f,r.yMin-6f-burst*11f),2f,light);
        }
    }
}
