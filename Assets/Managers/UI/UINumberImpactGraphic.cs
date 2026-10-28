using UnityEngine;
using UnityEngine.UI;

/// <summary>Short, bounded speed marks for a number's landing; cached once, empty at rest.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UINumberImpactGraphic : MaskableGraphic
{
    private int step=64;
    private float direction;
    private Color tint;
    public void SetImpulse(float progress,float travel,Color accent)
    {
        int next=Mathf.RoundToInt(Mathf.Clamp01(progress)*64);
        if(step==next&&direction==travel&&tint==accent)return;
        step=next;direction=travel;tint=accent;SetVerticesDirty();
    }
    protected override void OnEnable(){base.OnEnable();raycastTarget=false;}
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();float t=(step/64f-.14f)/.70f;
        if(t<=0||t>=1)return;
        Rect r=rectTransform.rect;if(r.width<2||r.height<2)return;
        float energy=Mathf.Sin(t*Mathf.PI)*Mathf.Exp(-t*2.2f),travel=UIMotion.Out(t);
        Color c=tint;c.a*=energy*.95f;
        float half=r.width*(.12f+.24f*travel),y=r.yMin+r.height*.08f;
        RacingPanelGraphic.Line(vh,new Vector2(r.center.x-half,y),new Vector2(r.center.x+half,y),2+energy*2,c,.6f);
        float sign=direction<0?-1:1;
        for(int side=0;side<2;side++)for(int i=0;i<3;i++)
        {
            float x=side==0?r.xMin+r.width*(.04f+i*.025f):r.xMax-r.width*(.04f+i*.025f);
            float middle=r.center.y+sign*r.height*(.08f+travel*.16f);
            float length=r.height*(.025f+.06f*energy)*(1-i*.18f);
            RacingPanelGraphic.Line(vh,new Vector2(x,middle-sign*length),new Vector2(x+sign*r.width*.018f,middle+sign*length),2,c,.6f);
        }
    }
}
