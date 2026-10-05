using UnityEngine;
using UnityEngine.UI;

/// <summary>Bevelled controls and action arrows drawn at the display's native resolution.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DrivingTutorialControlGraphic : MaskableGraphic
{
    public enum Kind { WheelHeader, PedalHeader, TurnWheel, PressPedal, ReleasePedal, Crown }
    private Kind kind;
    private float angle;
    public float Angle { set { if (Mathf.Approximately(angle, value)) return; angle = value; SetVerticesDirty(); } }
    public void Configure(Kind value) { kind = value; raycastTarget = false; SetVerticesDirty(); }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        Vector2 center = r.center;
        float radius = Mathf.Min(r.width, r.height) * .46f;
        if (kind == Kind.Crown)
        {
            Vector2 a = center + new Vector2(-radius, -radius * .3f), b = center + new Vector2(radius, -radius * .3f);
            Vector2[] points = { a, center + new Vector2(-radius, radius * .5f), center + new Vector2(-radius * .33f, 0f),
                center + new Vector2(0f, radius), center + new Vector2(radius * .33f, 0f), center + new Vector2(radius, radius * .5f), b };
            for (int i = 0; i + 1 < points.Length; i++) Line(vh, points[i], points[i + 1], 3.5f, Color.white);
            Line(vh, a, b, 3.5f, Color.white);
            return;
        }
        bool wheel = kind == Kind.WheelHeader || kind == Kind.TurnWheel;
        if (wheel)
        {
            bool metallic = kind == Kind.TurnWheel;
            if (metallic) radius *= .77f;
            Color blue = DrivingTutorialUI.Cyan;
            Ring(vh, center, radius, radius * .93f, metallic ? new Color(.55f, .59f, .65f) : new Color(.5f, .84f, 1f), metallic);
            Ring(vh, center, radius * .94f, radius * .77f, metallic ? new Color(.045f, .055f, .07f) : blue, metallic);
            Ring(vh, center, radius * .78f, radius * .75f, metallic ? new Color(.35f, .39f, .45f) : new Color(.025f, .38f, .65f), metallic);
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f - angle - 90f) * Mathf.Deg2Rad;
                Vector2 direction = D(a), side = new Vector2(-direction.y, direction.x);
                Quad(vh, center + direction * radius * .16f - side * radius * .15f,
                    center + direction * radius * .16f + side * radius * .15f,
                    center + direction * radius * .78f + side * radius * .08f,
                    center + direction * radius * .78f - side * radius * .08f,
                    metallic ? new Color(.22f, .26f, .31f) : blue);
            }
            Disc(vh, center, radius * .28f, metallic ? new Color(.38f, .42f, .47f) : new Color(.1f, .58f, .86f));
            Disc(vh, center, radius * .19f, metallic ? new Color(.12f, .15f, .2f) : new Color(.08f, .44f, .73f));
            if (metallic)
            {
                CurveArrow(vh, center, radius * 1.27f, 115f, 240f, DrivingTutorialUI.Cyan, false);
                CurveArrow(vh, center, radius * 1.27f, -60f, 65f, DrivingTutorialUI.Pink, true);
            }
        }
        else
        {
            bool header = kind == Kind.PedalHeader, press = kind == Kind.PressPedal;
            float width = radius * .6f, height = radius * .88f;
            center.y -= press ? radius * .13f : 0f;
            Color tint = header ? DrivingTutorialUI.Pink : new Color(.62f, .66f, .73f);
            if (kind == Kind.ReleasePedal) tint = new Color(.3f, .34f, .42f);
            Vector2 a = center + new Vector2(-width, -height), b = center + new Vector2(width, -height);
            Vector2 c = center + new Vector2(width * .6f, height), d = center + new Vector2(-width * 1.4f, height);
            Vector2 depth = new Vector2(radius * .055f, -radius * .11f);
            Quad(vh, a + depth, b + depth, c + depth, d + depth, new Color(.12f, .14f, .21f));
            Quad(vh, a, b, c, d, tint);
            Line(vh, a, b, 2.2f, new Color(.81f, .85f, .92f, .8f));
            Line(vh, d, c, 2.2f, new Color(1f, 1f, 1f, .65f));
            for (int i = -1; i <= 1; i++)
            {
                Vector2 start = center + new Vector2(i * width * .5f, -height * .66f);
                Vector2 end = start + new Vector2(-width * .3f, height * 1.34f);
                Line(vh, start + Vector2.right * 1.2f, end + Vector2.right * 1.2f, radius * .105f, new Color(.78f, .82f, .89f, .5f));
                Line(vh, start, end, radius * .09f, new Color(.065f, .085f, .13f));
            }
            if (press)
            {
                Vector2 tip = center + Vector2.up * radius * .58f;
                Arrow(vh, tip + Vector2.up * radius * .85f, tip, radius * .42f, DrivingTutorialUI.Pink);
            }
        }
    }

    private static void Ring(VertexHelper vh, Vector2 center, float outer, float inner, Color tint, bool metal)
    {
        for (int i = 0; i < 96; i++)
        {
            float a = i * Mathf.PI * 2f / 96f, b = (i + 1) * Mathf.PI * 2f / 96f;
            Color shade = metal ? Color.Lerp(tint * .42f, tint, .5f + .5f * Mathf.Sin(a)) : tint;
            shade.a = 1f;
            Quad(vh, center + D(a) * outer, center + D(b) * outer, center + D(b) * inner, center + D(a) * inner, shade);
        }
    }
    private static void Disc(VertexHelper vh, Vector2 center, float radius, Color tint)
    {
        for (int i = 0; i < 48; i++)
        {
            int index = vh.currentVertCount;
            vh.AddVert(center, tint, Vector2.zero); vh.AddVert(center + D(i * Mathf.PI * 2f / 48f) * radius, tint, Vector2.zero);
            vh.AddVert(center + D((i + 1) * Mathf.PI * 2f / 48f) * radius, tint, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2);
        }
    }
    private static void CurveArrow(VertexHelper vh, Vector2 center, float radius, float start, float end, Color tint, bool forward)
    {
        for (int i = 0; i < 32; i++)
        {
            float a = Mathf.Lerp(start, end, i / 32f) * Mathf.Deg2Rad, b = Mathf.Lerp(start, end, (i + 1) / 32f) * Mathf.Deg2Rad;
            Quad(vh, center + D(a) * (radius + 4f), center + D(b) * (radius + 4f),
                center + D(b) * (radius - 4f), center + D(a) * (radius - 4f), tint);
        }
        float tipAngle = (forward ? end : start) * Mathf.Deg2Rad;
        Vector2 tip = center + D(tipAngle) * radius;
        Vector2 tangent = new Vector2(-Mathf.Sin(tipAngle), Mathf.Cos(tipAngle)) * (forward ? 1f : -1f);
        Arrow(vh, tip - tangent * 16f, tip + tangent * 8f, 18f, tint);
    }
    private static void Arrow(VertexHelper vh, Vector2 start, Vector2 tip, float width, Color tint)
    {
        Vector2 direction = (tip - start).normalized, side = new Vector2(-direction.y, direction.x);
        Vector2 shoulder = tip - direction * width;
        Line(vh, start, shoulder, width * .55f, tint);
        int index = vh.currentVertCount;
        vh.AddVert(tip, tint, Vector2.zero); vh.AddVert(shoulder + side * width * .6f, tint, Vector2.zero); vh.AddVert(shoulder - side * width * .6f, tint, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2);
    }
    private static Vector2 D(float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
    {
        Vector2 side = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
        Quad(vh, a - side, a + side, b + side, b - side, tint);
    }
    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        int index = vh.currentVertCount;
        vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero); vh.AddVert(c, tint, Vector2.zero); vh.AddVert(d, tint, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
    }
}
