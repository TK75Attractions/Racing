using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolution independent, familiar symbols for Japanese racing menus.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RacingIconGraphic : MaskableGraphic
{
    public enum Icon { Back, Pedal, Chevron, Flag, Car, Gear, Book, Power, Home, Retry, Course, Bolt }
    public Icon symbol;
    private VertexHelper mesh;
    private Color ink;
    private Vector2 origin;
    private float size, aa;
    protected override void OnEnable() { base.OnEnable(); raycastTarget = false; }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); mesh = vh; ink = color; Rect r = rectTransform.rect;
        size = Mathf.Min(r.width, r.height); origin = r.center;
        aa = 1f / Mathf.Max(.1f, canvas != null ? canvas.scaleFactor : 1f);
        switch (symbol)
        {
            case Icon.Back: Path(.08f, new Vector2(.16f,.32f),new Vector2(-.16f,0),new Vector2(.16f,-.32f)); break;
            case Icon.Chevron: Path(.07f, new Vector2(-.12f,.29f),new Vector2(.14f,0),new Vector2(-.12f,-.29f)); break;
            case Icon.Pedal: Path(.12f,new Vector2(-.08f,-.28f),new Vector2(.17f,.28f)); Path(.06f,new Vector2(-.3f,-.3f),new Vector2(.3f,-.3f)); break;
            case Icon.Car:
                Poly(new Vector2(-.37f,-.24f),new Vector2(.37f,-.24f),new Vector2(.37f,.02f),new Vector2(.24f,.33f),new Vector2(-.24f,.33f),new Vector2(-.37f,.02f));
                Color old = ink; ink = new Color(.025f,.06f,.15f,old.a);
                Poly(new Vector2(-.24f,.07f),new Vector2(.24f,.07f),new Vector2(.17f,.23f),new Vector2(-.17f,.23f));
                Circle(new Vector2(-.24f,-.07f),.055f,true); Circle(new Vector2(.24f,-.07f),.055f,true); ink = old;
                Path(.1f,new Vector2(-.26f,-.23f),new Vector2(-.26f,-.36f)); Path(.1f,new Vector2(.26f,-.23f),new Vector2(.26f,-.36f)); break;
            case Icon.Flag:
                Path(.06f,new Vector2(-.32f,-.4f),new Vector2(-.16f,.38f));
                for(int y=0;y<3;y++) for(int x=0;x<4;x++) if((x+y)%2==0)
                { float xx=-.15f+x*.14f, yy=.04f+y*.12f; Poly(new Vector2(xx,yy),new Vector2(xx+.14f,yy-.03f),new Vector2(xx+.17f,yy+.09f),new Vector2(xx+.03f,yy+.12f)); } break;
            case Icon.Gear:
                Circle(Vector2.zero,.25f,false,.11f);
                for(int i=0;i<8;i++){float a=i*Mathf.PI/4f; Vector2 d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));Path(.13f,d*.20f,d*.37f);} break;
            case Icon.Book:
                Poly(new Vector2(-.36f,-.29f),new Vector2(-.04f,-.35f),new Vector2(-.04f,.31f),new Vector2(-.36f,.36f));
                Poly(new Vector2(.04f,-.35f),new Vector2(.36f,-.29f),new Vector2(.36f,.36f),new Vector2(.04f,.31f)); break;
            case Icon.Power:
                Circle(Vector2.zero,.31f,false,.075f,125f,415f);Path(.075f,new Vector2(0,.05f),new Vector2(0,.4f)); break;
            case Icon.Home:
                Poly(new Vector2(-.4f,.02f),new Vector2(0,.4f),new Vector2(.4f,.02f));
                Poly(new Vector2(-.26f,-.34f),new Vector2(-.08f,-.34f),new Vector2(-.08f,.04f),new Vector2(-.26f,.04f));
                Poly(new Vector2(.08f,-.34f),new Vector2(.26f,-.34f),new Vector2(.26f,.04f),new Vector2(.08f,.04f)); break;
            case Icon.Retry:
                Circle(Vector2.zero,.30f,false,.085f,-40f,255f);
                Poly(new Vector2(-.38f,.33f),new Vector2(-.30f,-.03f),new Vector2(.02f,.15f)); break;
            case Icon.Course:
                Path(.045f,new Vector2(-.38f,-.25f),new Vector2(-.28f,-.35f),new Vector2(-.05f,-.32f),new Vector2(.08f,-.07f),new Vector2(.30f,.06f),new Vector2(.38f,.29f),new Vector2(.22f,.38f),new Vector2(.04f,.31f),new Vector2(-.05f,.08f),new Vector2(-.27f,.01f),new Vector2(-.38f,-.25f)); break;
            case Icon.Bolt: Poly(new Vector2(.04f,.42f),new Vector2(-.23f,-.02f),new Vector2(-.02f,-.02f),new Vector2(-.08f,-.40f),new Vector2(.25f,.12f),new Vector2(.04f,.12f)); break;
        }
    }
    private void Path(float thickness, params Vector2[] points)
    { for(int i=1;i<points.Length;i++) RacingPanelGraphic.Line(mesh,origin+points[i-1]*size,origin+points[i]*size,thickness*size,ink,aa); }
    private void Poly(params Vector2[] p)
    { int n=mesh.currentVertCount; foreach(Vector2 v in p) mesh.AddVert(origin+v*size,ink,Vector2.zero);for(int i=1;i<p.Length-1;i++)mesh.AddTriangle(n,n+i,n+i+1); }
    private void Circle(Vector2 center,float radius,bool fill,float width=.04f,float from=0,float to=360)
    { Vector2 previous=center+new Vector2(Mathf.Cos(from*Mathf.Deg2Rad),Mathf.Sin(from*Mathf.Deg2Rad))*radius;
      for(int i=1;i<=48;i++){float a=Mathf.Lerp(from,to,i/48f)*Mathf.Deg2Rad;Vector2 next=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius;
      if(fill)Poly(center,previous,next);else Path(width,previous,next);previous=next;} }
}
