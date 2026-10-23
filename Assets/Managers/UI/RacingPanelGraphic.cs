using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolution-independent surfaces with a one-screen-pixel feathered edge.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public class RacingPanelGraphic : MaskableGraphic
{
    public enum SurfaceStyle { Panel, Primary, Secondary, Danger }
    private const int PointCount = 8;
    private readonly Vector2[] outer = new Vector2[PointCount];
    private readonly Vector2[] inner = new Vector2[PointCount];
    private SurfaceStyle style;
    private Color accent = RacingUITheme.Cyan;
    private float selection, pressure, confirmation;
    private bool disabled, waiting;

    public Color Accent => accent;
    public SurfaceStyle Style => style;

    public void SetButtonModifiers(bool unavailable, bool retained)
    {
        if (disabled == unavailable && waiting == retained) return;
        disabled = unavailable; waiting = retained;
        SetVerticesDirty();
    }

    public void Configure(SurfaceStyle value, Color tint)
    {
        style = value;
        accent = ArcadeUI.For(transform).Primary;
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void SetState(float focus, float pedal, float flash, Color tint)
    {
        Color nextAccent = ArcadeUI.For(transform).Primary;
        if (Mathf.Approximately(selection, focus) && Mathf.Approximately(pressure, pedal)
            && Mathf.Approximately(confirmation, flash) && accent == nextAccent) return;
        selection = Mathf.Clamp01(focus);
        pressure = Mathf.Clamp01(pedal);
        confirmation = Mathf.Clamp01(flash);
        accent = nextAccent;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); Rect r=rectTransform.rect;if(r.width<2||r.height<2)return;
        var p=ArcadeUI.For(transform);bool button=style!=SurfaceStyle.Panel;
        Color fill=!button?p.Ink:style==SurfaceStyle.Primary?p.Primary:style==SurfaceStyle.Danger?ArcadeUI.Hex("edb3b5"):p.Paper;
        if(button)fill=Color.Lerp(fill,p.Soft,selection*.25f);
        if(disabled)fill=Color.Lerp(fill,p.Ink,.6f);
        Rect shadow=r;shadow.y-=button?7:3;ArcadeSurfaceGraphic.Rounded(vh,shadow,p.Ink,10);
        ArcadeSurfaceGraphic.Rounded(vh,r,fill*color,10);
        if(!button)return;
        Color mark=style==SurfaceStyle.Primary?p.Ink:p.Primary;
        for(int i=0;i<5;i++)
        {
            float y=r.center.y-19+i*8;
            Line(vh,new Vector2(r.xMin+18,y),new Vector2(r.xMin+36,y),5,mark,.5f);
        }
        for(int i=0;i<5;i++)
            Line(vh,new Vector2(r.xMax-18-i*11,r.yMin+10),new Vector2(r.xMax-12-i*11,r.yMin+20),4,Alpha(p.Ink,.35f),.5f);
        if(pressure>0)Line(vh,new Vector2(r.xMin+8,r.yMin+4),new Vector2(Mathf.Lerp(r.xMin+8,r.xMax-15,pressure),r.yMin+4),6,p.Accent,.5f);
    }

    private void Shape(Rect r, float inset, Vector2[] points)
    {
        float cut = Mathf.Clamp(r.height * .20f, 8f, 26f);
        float l = r.xMin + inset, rr = r.xMax - inset, b = r.yMin + inset, t = r.yMax - inset;
        cut = Mathf.Max(0f, cut - inset * .4f);
        points[0] = new Vector2(l + cut * .4f, b);
        points[1] = new Vector2(rr - cut, b);
        points[2] = new Vector2(rr, b + cut);
        points[3] = new Vector2(rr, t - cut * .4f);
        points[4] = new Vector2(rr - cut * .4f, t);
        points[5] = new Vector2(l + cut, t);
        points[6] = new Vector2(l, t - cut);
        points[7] = new Vector2(l, b + cut * .4f);
    }

    private void Fill(VertexHelper vh, Rect r, Color bottom, Color top)
    {
        Shape(r, 0f, outer);
        int start = vh.currentVertCount;
        for (int i = 0; i < PointCount; i++)
            vh.AddVert(outer[i], (style == SurfaceStyle.Primary ? Color.Lerp(top, bottom, Mathf.InverseLerp(r.xMin, r.xMax, outer[i].x) * .9f) : Color.Lerp(bottom, top, Mathf.InverseLerp(r.yMin, r.yMax, outer[i].y))) * color, Vector2.zero);
        for (int i = 1; i < PointCount - 1; i++) vh.AddTriangle(start, start + i, start + i + 1);
    }

    private void Ring(VertexHelper vh, Rect r, float outside, float inside, Color outerColor, Color innerColor)
    {
        Shape(r, outside, outer);
        Shape(r, inside, inner);
        int start = vh.currentVertCount;
        for (int i = 0; i < PointCount; i++)
        {
            vh.AddVert(outer[i], outerColor * color, Vector2.zero);
            vh.AddVert(inner[i], innerColor * color, Vector2.zero);
        }
        for (int i = 0; i < PointCount; i++)
        {
            int a = start + i * 2, b = start + (i + 1) % PointCount * 2;
            vh.AddTriangle(a, b, a + 1);
            vh.AddTriangle(a + 1, b, b + 1);
        }
    }

    private void DrawChecks(VertexHelper vh, Rect r, Color tint)
    {
        float h = Mathf.Min(r.height, 110f);
        // Diagonal bands, inset from the silhouette so they never create rectangular corners.
        for (int i = 0; i < 3; i++)
        {
            float x = r.xMax - h * (1.1f + i * .30f);
            int n = vh.currentVertCount;
            Color c = Alpha(tint, .04f + i * .015f) * color;
            vh.AddVert(new Vector2(x, r.yMin + 4f), c, Vector2.zero);
            vh.AddVert(new Vector2(x + h * .20f, r.yMin + 4f), c, Vector2.zero);
            vh.AddVert(new Vector2(x + h * .80f, r.yMin + h - 4f), c, Vector2.zero);
            vh.AddVert(new Vector2(x + h * .60f, r.yMin + h - 4f), c, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
        }
        float cut = Mathf.Clamp(h * .20f, 8f, 26f);
        Line(vh, new Vector2(r.xMin + 2f, r.yMax - cut * .72f), new Vector2(r.xMin + cut * .72f, r.yMax - 2f),
            5f, tint, 1f);
        Line(vh, new Vector2(r.xMax - cut * .82f, r.yMin + 2f), new Vector2(r.xMax - 2f, r.yMin + cut * .82f),
            6f, tint, 1f);
    }

    public static Color Alpha(Color c, float a) { c.a = a; return c; }

    public static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint, float feather = 1f)
    {
        Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized;
        int start = vh.currentVertCount;
        for (int i = 0; i < 4; i++)
        {
            float offset = i == 0 ? -width * 0.5f - feather : i == 1 ? -width * 0.5f : i == 2 ? width * 0.5f : width * 0.5f + feather;
            Color c = i == 0 || i == 3 ? Alpha(tint, 0f) : tint;
            vh.AddVert(a + normal * offset, c, Vector2.zero);
            vh.AddVert(b + normal * offset, c, Vector2.zero);
        }
        for (int i = 0; i < 3; i++)
        {
            int n = start + i * 2;
            vh.AddTriangle(n, n + 1, n + 2);
            vh.AddTriangle(n + 2, n + 1, n + 3);
        }
    }
}
