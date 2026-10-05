using UnityEngine;
using UnityEngine.UI;

/// <summary>Vector wheel and pedal icons; stays sharp on both player displays.</summary>
public sealed class DrivingTutorialControlGraphic : MaskableGraphic
{
    private bool steering;
    private float angle;
    public float Angle { set { if (Mathf.Approximately(angle, value)) return; angle = value; SetVerticesDirty(); } }

    public void Configure(bool isSteering, Color tint)
    {
        steering = isSteering;
        color = tint;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float radius = Mathf.Min(r.width, r.height) * .45f;
        Vector2 center = r.center;
        if (steering)
        {
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI * 2f / 48f, b = (i + 1) * Mathf.PI * 2f / 48f;
                Quad(vh, center + Direction(a) * radius, center + Direction(b) * radius,
                    center + Direction(b) * radius * .77f, center + Direction(a) * radius * .77f, color);
            }
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f - angle - 90f) * Mathf.Deg2Rad;
                Vector2 d = Direction(a), side = new Vector2(-d.y, d.x) * radius * .1f;
                Quad(vh, center + side, center - side, center + d * radius * .8f - side, center + d * radius * .8f + side, color);
            }
            for (int i = 0; i < 20; i++)
            {
                int index = vh.currentVertCount;
                vh.AddVert(center, color, Vector2.zero);
                vh.AddVert(center + Direction(i * Mathf.PI * 2f / 20f) * radius * .23f, color, Vector2.zero);
                vh.AddVert(center + Direction((i + 1) * Mathf.PI * 2f / 20f) * radius * .23f, color, Vector2.zero);
                vh.AddTriangle(index, index + 1, index + 2);
            }
        }
        else
        {
            float w = radius * .6f, h = radius;
            Quad(vh, center + new Vector2(-w, -h), center + new Vector2(w, -h),
                center + new Vector2(w * .65f, h), center + new Vector2(-w * 1.35f, h), color);
            for (int i = -1; i <= 1; i++)
            {
                Vector2 start = center + new Vector2(i * w * .47f, -h * .65f);
                Vector2 end = start + new Vector2(-w * .27f, h * 1.35f);
                Vector2 side = Vector2.right * w * .07f;
                Quad(vh, start - side, start + side, end + side, end - side, new Color(.025f, .04f, .07f, .85f));
            }
        }
    }

    private static Vector2 Direction(float radians) => new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        int index = vh.currentVertCount;
        vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero);
        vh.AddVert(c, tint, Vector2.zero); vh.AddVert(d, tint, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
    }
}
