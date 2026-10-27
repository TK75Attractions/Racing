using UnityEngine;

/// <summary>Shared timings and curves. Scenery stays still; only the relevant UI moves.</summary>
public static class UIMotion
{
    public const float ResponseSeconds = .16f;
    public const float RevealSeconds = .32f;
    public static float Out(float t) { t = Mathf.Clamp01(t); return 1f - (1f-t)*(1f-t)*(1f-t); }
    public static float In(float t) { t = Mathf.Clamp01(t); return t*t*t; }
    public static float InOut(float t) { t = Mathf.Clamp01(t); return t*t*(3f-2f*t); }
    public static float Spring(float t) => Mathf.Sin(Mathf.Clamp01(t)*Mathf.PI*2f)*Mathf.Exp(-5f*Mathf.Clamp01(t));
    public static float Follow(float current, float target, float dt, float seconds, float epsilon = .001f)
    {
        if (dt <= 0f || float.IsNaN(dt)) return current;
        float next = Mathf.Lerp(current, target, 1f-Mathf.Exp(-Mathf.Min(dt,.1f)/Mathf.Max(.001f,seconds)));
        return Mathf.Abs(next-target) <= epsilon ? target : next;
    }
}
