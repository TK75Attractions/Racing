using UnityEngine;
using UnityEngine.UI;

/// <summary>Circular neon step marker with a bright inner rim.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DrivingTutorialBadgeGraphic : MaskableGraphic
{
    private bool active,complete;
    public void SetState(bool selected,bool done)
    {if(active==selected && complete==done)return;active=selected;complete=done;SetVerticesDirty();}
    protected override void OnEnable(){base.OnEnable();raycastTarget=false;}
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();Rect r=rectTransform.rect;Vector2 c=r.center;float radius=Mathf.Min(r.width,r.height)*.45f;
        Color tint=complete?RacingUITheme.Gold:DrivingTutorialUI.Cyan;
        for(int i=0;i<64;i++)
        {
            float a=i*Mathf.PI*2/64,b=(i+1)*Mathf.PI*2/64;
            Vector2 p=new Vector2(Mathf.Cos(a),Mathf.Sin(a)),q=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
            RacingPanelGraphic.Line(vh,c+p*radius,c+q*radius,8f,new Color(tint.r,tint.g,tint.b,active?.25f:.13f));
            RacingPanelGraphic.Line(vh,c+p*radius,c+q*radius,2.5f,active?Color.white:tint);
        }
    }
}
