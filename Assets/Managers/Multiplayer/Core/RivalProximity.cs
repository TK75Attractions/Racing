using System;

/// <summary>Metres / metres per second. Geometry and visibility are supplied by the live scene.</summary>
public static class RivalProximity
{
    public static float ShortestCourseGap(float from, float to, float length)
    {
        if (!Finite(from) || !Finite(to) || !Finite(length) || length <= 0f) return float.PositiveInfinity;
        float gap = (to - from) % length;
        if (gap > length * .5f) gap -= length;
        if (gap < -length * .5f) gap += length;
        return gap;
    }

    public static float Strength(float longitudinal, float lateral, float height, float courseGap,
        float distance, float closingSpeed, bool rear, bool visible)
    {
        if (!visible || !Finite(longitudinal) || !Finite(lateral) || !Finite(height) ||
            !Finite(courseGap) || !Finite(distance) || !Finite(closingSpeed) || distance < 0f ||
            Math.Abs(lateral) > 9f || Math.Abs(height) > 4.5f || Math.Abs(courseGap) > 70f) return 0f;
        // Parallel cars do not oscillate between front and rear as the steering jitters.
        float along = rear ? -longitudinal : longitudinal;
        float range = rear ? 40f : 55f, close = rear ? 14f : 18f;
        if (along < 1.5f || distance > range || (distance > close && closingSpeed < 1f)) return 0f;
        float proximity = 1f - Math.Min(1f, distance / range);
        float urgency = closingSpeed > 0f ? 1f - Math.Min(1f, distance / closingSpeed / 3f) : 0f;
        return Math.Min(1f, .25f + .75f * Math.Max(proximity, urgency));
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
