using UnityEngine;

/// <summary>Shared physical-driver pace. Uses road curvature/braking distance, never changes a car's motion.</summary>
public static class RaceSpeedPlanner
{
    public const float Cruise = 50f;
    public const float JumpSpeed = 32f;
    public static bool Enabled => RaceFeatureController.Enabled(RaceFeature.FastAiPace);

    public static float Limit(RaceCourse course, float progress, float sign, float cruise)
    {
        float limit = cruise;
        course.TryGetSampleAtProgress(progress, out var here);
        Vector3 previous = here.forward;
        float lateral = RaceFeatureController.Enabled(RaceFeature.HighSpeedCornering) ? 27f : 14f;
        for (float distance = 5f; distance <= 100f; distance += 5f)
        {
            course.TryGetSampleAtProgress(progress + sign * distance, out var ahead);
            float curvature = Vector3.Angle(Vector3.ProjectOnPlane(previous, Vector3.up),
                Vector3.ProjectOnPlane(ahead.forward, Vector3.up)) * Mathf.Deg2Rad / 5f;
            float corner = Mathf.Min(cruise, Mathf.Sqrt(lateral / Mathf.Max(.0001f, curvature)));
            limit = Mathf.Min(limit, Mathf.Sqrt(corner * corner + 2f * 12f * Mathf.Max(0f, distance - 8f)));
            previous = ahead.forward;
            if (!course.HasRoadAtProgress(progress + sign * distance))
                limit = Mathf.Min(limit, Mathf.Sqrt(JumpSpeed * JumpSpeed + 2f * 12f * Mathf.Max(0f, distance - 15f)));
        }
        return limit;
    }

    // Fast pace must retain the live CPU contest switch instead of overriding it.
    public static float CpuCruise(float partnerSpeed, float gap, float time, int index)
    {
        return RaceFeatureController.Enabled(RaceFeature.CpuRacing)
            ? CpuRacePace.FastRequest(partnerSpeed, gap, time, index, Cruise) : Cruise;
    }
}
