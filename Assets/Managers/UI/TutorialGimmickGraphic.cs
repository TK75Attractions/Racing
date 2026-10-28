using UnityEngine;
using UnityEngine.UI;

/// <summary>Small animated diagrams that teach the course language without external textures.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TutorialGimmickGraphic : MaskableGraphic
{
    public enum Kind { Jump, Tube, Drift, Slipstream }
    public Kind Illustration;
    private VertexHelper mesh;
    private Rect area;
    private static readonly Color Road=new Color(.12f,.19f,.25f,1f), Gold=new Color(1f,.70f,.13f,1f), Mint=new Color(.15f,.8f,.58f,1f);
    private float phase;
    private int frame=-1;
    private void Update()
    {
        if(UIMotion.IsPaused)return;
        int next=(int)(UIMotion.Realtime*12);
        if(frame==next)return;frame=next;phase=(float)(UIMotion.Realtime%1d);SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();mesh=vh;area=rectTransform.rect;
        if(area.width<=0||area.height<=0)return;
        switch(Illustration)
        {
            case Kind.Jump:
                Line(.04f,.22f,.37f,.22f,.055f,Road);
                Poly(Gold,new Vector2(.36f,.19f),new Vector2(.64f,.19f),new Vector2(.64f,.48f),new Vector2(.36f,.25f));
                for(int i=0;i<3;i++)Arrow(.42f+i*.06f,.30f+i*.05f,.055f,Gold);
                for(int i=0;i<15;i++)
                { float t=i/15f,u=(i+.55f)/15f;Line(.64f+t*.31f,.48f+.38f*t-.58f*t*t,.64f+u*.31f,.48f+.38f*u-.58f*u*u,.009f,Gold); }
                Line(.86f,.21f,.98f,.21f,.045f,Road);
                Car(.48f,.43f,.13f,23f,color);break;
            case Kind.Tube:
                Poly(Road,new Vector2(.08f,.13f),new Vector2(.93f,.13f),new Vector2(.80f,.49f),new Vector2(.20f,.49f));
                Ellipse(.31f,.47f,.20f,.39f,new Color(.48f,.62f,.70f,1f));Ellipse(.72f,.47f,.16f,.30f,new Color(.65f,.74f,.78f,1f));
                Line(.31f,.86f,.72f,.77f,.012f,Road);Line(.11f,.47f,.56f,.47f,.012f,Road);Line(.51f,.47f,.88f,.47f,.012f,Road);
                Line(.22f,.18f,.79f,.41f,.025f,Mint);Line(.35f,.18f,.86f,.41f,.025f,Mint);
                Car(.42f,.30f,.12f,15f,color);Arrow(.64f,.34f,.08f,Mint);break;
            case Kind.Drift:
                Vector2 previous=new Vector2(.09f,.26f);
                for(int i=1;i<=20;i++)
                { float t=i/20f;var point=new Vector2(.09f+t*.82f,.26f+.50f*t*t);Line(previous,point,.13f,Road);previous=point; }
                for(int i=0;i<4;i++)Line(.26f+i*.06f,.18f+i*.025f,.29f+i*.06f,.19f+i*.025f,.012f,Gold);
                Car(.56f,.45f,.15f,48f,color);Arrow(.78f,.57f,.09f,Gold);break;
            case Kind.Slipstream:
                Poly(Road,new Vector2(.02f,.12f),new Vector2(.98f,.12f),new Vector2(.98f,.85f),new Vector2(.02f,.85f));
                for(int i=0;i<6;i++)Line(.04f+i*.16f,.19f,.12f+i*.16f,.19f,.009f,Color.white);
                for(int i=0;i<4;i++)
                { float x=.10f+i*.12f+phase*.05f;Line(x,.39f,x+.09f,.39f,.014f,Gold);Line(x,.61f,x+.09f,.61f,.014f,Gold); }
                Car(.37f,.50f,.17f,0f,color);Car(.75f,.50f,.17f,0f,new Color(.94f,.45f,.62f,1f));Arrow(.54f,.50f,.055f,Gold);break;
        }
    }
    private Vector2 P(Vector2 p)=>new Vector2(area.xMin+p.x*area.width,area.yMin+p.y*area.height);
    private void Line(Vector2 a,Vector2 b,float width,Color tint)=>RacingPanelGraphic.Line(mesh,P(a),P(b),width*area.height,tint,1f);
    private void Line(float x,float y,float xx,float yy,float width,Color tint)=>Line(new Vector2(x,y),new Vector2(xx,yy),width,tint);
    private void Poly(Color tint,params Vector2[] points)
    { int n=mesh.currentVertCount;foreach(var p in points)mesh.AddVert(P(p),tint,Vector2.zero);for(int i=1;i<points.Length-1;i++)mesh.AddTriangle(n,n+i,n+i+1); }
    private void Arrow(float x,float y,float size,Color tint)
    { Line(x-size,y-size,x,y,.025f,tint);Line(x,y,x-size,y+size,.025f,tint); }
    private void Ellipse(float x,float y,float rx,float ry,Color tint)
    {
        for(int i=0;i<48;i++)
        { float a=i*Mathf.PI/24f,b=(i+1)*Mathf.PI/24f;Line(x+Mathf.Cos(a)*rx,y+Mathf.Sin(a)*ry,x+Mathf.Cos(b)*rx,y+Mathf.Sin(b)*ry,.017f,tint); }
    }
    private void Car(float x,float y,float size,float angle,Color tint)
    {
        float a=angle*Mathf.Deg2Rad;var forward=new Vector2(Mathf.Cos(a)*size,Mathf.Sin(a)*size*area.width/area.height);
        var side=new Vector2(-Mathf.Sin(a)*size*.45f,Mathf.Cos(a)*size*.45f*area.width/area.height);var c=new Vector2(x,y);
        Poly(tint,c-forward-side,c+forward-side,c+forward+side,c-forward+side);
        Poly(Road,c+forward*.15f-side*.8f,c+forward*.55f-side*.8f,c+forward*.55f+side*.8f,c+forward*.15f+side*.8f);
    }
}
