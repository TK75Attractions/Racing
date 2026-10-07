using UnityEngine;
using UnityEngine.UI;

/// <summary>Decorated neon glass: chamfered double frames, corner cuts and diagonal racing bands.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DrivingTutorialPanelGraphic : MaskableGraphic
{
    private Color accent;
    private bool selected=true,instrument,lesson;
    private readonly Vector2[] outer=new Vector2[8],inner=new Vector2[8];
    public void Configure(Color tint,bool active=true,bool inset=false,bool lessonCard=false)
    {accent=tint;selected=active;instrument=inset;lesson=lessonCard;raycastTarget=false;SetVerticesDirty();}
    public void SetSelected(bool value){if(selected==value)return;selected=value;SetVerticesDirty();}
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();Rect r=rectTransform.rect;
        Color edge=accent;edge.a=selected?1f:.65f;
        if(!instrument)
            for(int i=8;i>0;i--)
                Ring(vh,r,-i*1.8f,-(i-1)*1.8f,Alpha(accent,(9-i)*.007f),Alpha(accent,(10-i)*.007f));
        Shape(r,0,outer);int k=vh.currentVertCount;
        foreach(Vector2 p in outer)
            vh.AddVert(p,Color.Lerp(new Color(.002f,.007f,.025f,.96f),new Color(.009f,.024f,.058f,.95f),Mathf.InverseLerp(r.yMin,r.yMax,p.y)),Vector2.zero);
        for(int i=1;i<7;i++)vh.AddTriangle(k,k+i,k+i+1);
        Bands(vh,r);
        Ring(vh,r,-1,0,Alpha(edge,0),edge);Ring(vh,r,0,instrument?1.5f:3f,edge,edge);
        Ring(vh,r,3f,4f,Alpha(edge,.4f),Alpha(edge,0));
        Ring(vh,r,7f,8f,Alpha(accent,.48f),Alpha(accent,.48f));
        if(!instrument)
        {
            float cut=Cut(r);
            Corner(vh,new Vector2(r.xMin+1,r.yMax-cut),new Vector2(r.xMin+cut,r.yMax-1),accent,selected);
            Corner(vh,new Vector2(r.xMax-cut,r.yMin+1),new Vector2(r.xMax-1,r.yMin+cut),lesson?DrivingTutorialUI.Pink:accent,selected);
            if(lesson)
            {
                Color pink=DrivingTutorialUI.Pink;
                RacingPanelGraphic.Line(vh,new Vector2(r.xMax-r.width*.21f,r.yMin+2),new Vector2(r.xMax-cut,r.yMin+2),3f,pink);
                k=vh.currentVertCount;
                vh.AddVert(new Vector2(r.xMax-1,r.center.y-13),accent,Vector2.zero);
                vh.AddVert(new Vector2(r.xMax+11,r.center.y),accent,Vector2.zero);
                vh.AddVert(new Vector2(r.xMax-1,r.center.y+13),accent,Vector2.zero);vh.AddTriangle(k,k+1,k+2);
            }
        }
    }
    private void Bands(VertexHelper vh,Rect r)
    {
        float h=Mathf.Min(r.height,r.width*.48f);
        for(int i=0;i<4;i++)
        {
            float x=r.xMax-h*(.85f+i*.24f);Color c=Alpha(accent,instrument?.012f:.045f);int k=vh.currentVertCount;
            vh.AddVert(new Vector2(x,r.yMin+10),c,Vector2.zero);vh.AddVert(new Vector2(x+h*.13f,r.yMin+10),c,Vector2.zero);
            vh.AddVert(new Vector2(x+h*.70f,r.yMax-10),c,Vector2.zero);vh.AddVert(new Vector2(x+h*.57f,r.yMax-10),c,Vector2.zero);
            vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k,k+2,k+3);
        }
        if(lesson)
        {
            Color c=Alpha(DrivingTutorialUI.Pink,.11f);float x=r.xMax-r.height*.33f;int k=vh.currentVertCount;
            vh.AddVert(new Vector2(x,r.yMin+6),c,Vector2.zero);vh.AddVert(new Vector2(x+24,r.yMin+6),c,Vector2.zero);
            vh.AddVert(new Vector2(r.xMax-10,r.yMin+r.height*.43f),c,Vector2.zero);vh.AddVert(new Vector2(r.xMax-34,r.yMin+r.height*.43f),c,Vector2.zero);
            vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k,k+2,k+3);
        }
    }
    private static void Corner(VertexHelper vh,Vector2 a,Vector2 b,Color c,bool active)
    {RacingPanelGraphic.Line(vh,a,b,12f,Alpha(c,active?.28f:.12f));RacingPanelGraphic.Line(vh,a,b,6f,c);if(active)RacingPanelGraphic.Line(vh,a,b,2.5f,Color.white);}
    private float Cut(Rect r)=>Mathf.Min(instrument?18f:27f,Mathf.Min(r.width,r.height)*.16f);
    private void Shape(Rect r,float inset,Vector2[] p)
    {
        float l=r.xMin+inset,rr=r.xMax-inset,b=r.yMin+inset,t=r.yMax-inset,cut=Mathf.Max(0,Cut(r)-inset*.35f);
        p[0]=new Vector2(l+cut,b);p[1]=new Vector2(rr-cut,b);p[2]=new Vector2(rr,b+cut);p[3]=new Vector2(rr,t-cut);
        p[4]=new Vector2(rr-cut,t);p[5]=new Vector2(l+cut,t);p[6]=new Vector2(l,t-cut);p[7]=new Vector2(l,b+cut);
    }
    private void Ring(VertexHelper vh,Rect r,float outside,float inside,Color a,Color b)
    {
        Shape(r,outside,outer);Shape(r,inside,inner);int k=vh.currentVertCount;
        for(int i=0;i<8;i++){vh.AddVert(outer[i],a,Vector2.zero);vh.AddVert(inner[i],b,Vector2.zero);}
        for(int i=0;i<8;i++){int x=k+i*2,y=k+(i+1)%8*2;vh.AddTriangle(x,y,x+1);vh.AddTriangle(x+1,y,y+1);}
    }
    private static Color Alpha(Color c,float a){c.a=a;return c;}
}
