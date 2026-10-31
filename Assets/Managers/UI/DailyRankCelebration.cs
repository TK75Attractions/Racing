using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Gold confetti and repeated rank impulses for the day's leading fifth.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DailyRankCelebration : MaskableGraphic
{
    private float age, nextPulse;
    private Color accent;
    private UIValuePulse pulse;
    public bool IsCelebrating { get; private set; }
    public void Configure(TMP_Text rank, Color playerColor)
    {
        accent = playerColor;
        pulse = rank.GetComponent<UIValuePulse>() ?? rank.gameObject.AddComponent<UIValuePulse>();
        raycastTarget = false;
    }
    public void SetCelebrating(bool value)
    {
        if (IsCelebrating == value) return;
        IsCelebrating = value; age = nextPulse = 0f;
        pulse?.ResetPose(); SetVerticesDirty();
    }
    protected override void OnEnable() { base.OnEnable(); age = nextPulse = 0f; }
    protected override void OnDisable() { pulse?.ResetPose(); base.OnDisable(); }
    private void Update()
    {
        if (!IsCelebrating || UIMotion.IsPaused) return;
        age += Time.unscaledDeltaTime;
        if (age >= nextPulse) { pulse?.Play(RacingUITheme.Gold, 5f); nextPulse = age + 1.8f; }
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if (!IsCelebrating) return;
        Rect r = rectTransform.rect;
        for (int i = 0; i < 42; i++)
        {
            float cycle = Mathf.Repeat(age * .42f + i * .137f, 1f);
            float x = Mathf.Repeat(i * .618034f + Mathf.Sin(age * 2f + i) * .025f, 1f);
            Vector2 center = new Vector2(Mathf.Lerp(r.xMin, r.xMax, x), Mathf.Lerp(r.yMax, r.yMin, cycle));
            float angle = age * (i % 2 == 0 ? 3f : -3f) + i;
            Vector2 a = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (i % 3 == 0 ? 6f : 3f);
            Vector2 b = new Vector2(-a.y, a.x) * .42f;
            Color c = i % 3 == 0 ? Color.white : i % 3 == 1 ? RacingUITheme.Gold : accent;
            c.a = Mathf.Sin(cycle * Mathf.PI) * .9f;
            Quad(vh, center-a-b, center-a+b, center+a+b, center+a-b, c);
        }
        float glow = .55f + .35f * Mathf.Sin(age * 4f);
        Color edge = RacingUITheme.Gold; edge.a = glow;
        Quad(vh, new Vector2(r.xMin,r.yMin),new Vector2(r.xMin,r.yMax),new Vector2(r.xMin+3,r.yMax),new Vector2(r.xMin+3,r.yMin),edge);
        Quad(vh, new Vector2(r.xMax-3,r.yMin),new Vector2(r.xMax-3,r.yMax),new Vector2(r.xMax,r.yMax),new Vector2(r.xMax,r.yMin),edge);
    }
    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        int start=vh.currentVertCount;
        vh.AddVert(a,color,Vector2.zero); vh.AddVert(b,color,Vector2.zero); vh.AddVert(c,color,Vector2.zero); vh.AddVert(d,color,Vector2.zero);
        vh.AddTriangle(start,start+1,start+2); vh.AddTriangle(start,start+2,start+3);
    }
}
