using UnityEngine;

/// <summary>Observes road geometry and the current vehicle, then emits only steering/pedal input.</summary>
public sealed class RoadObservationBot
{
    private float nextDecision;
    private DriveInputState held;
    public DriveInputState Decide(RaceCourse course, Rigidbody body, float sign, int contacts, float now,
        float cruise = RaceSpeedPlanner.Cruise, float lane = 0f, float reaction = .1f, float noise = .25f,
        float speedCap = float.PositiveInfinity, DebugMover vehicle = null)
    {
        if (course == null || body == null || !course.HasValidPath) return DriveInputState.Neutral;
        if (contacts == 0) { held = DriveInputState.Neutral; nextDecision = now; return held; }
        if (now < nextDecision) return held;
        nextDecision = now + Mathf.Max(.02f, reaction);
        float raw = course.GetProgressDistance(body.position);
        float speed = Vector3.ProjectOnPlane(body.linearVelocity, body.rotation * Vector3.up).magnitude;
        course.TryGetSampleAtProgress(raw + sign * Mathf.Clamp(5f + speed * .4f, 8f, 17f), out RaceCourse.CourseSample target);
        course.TryGetSampleAtProgress(raw + sign * 28f, out RaceCourse.CourseSample far);
        float desired = RaceSpeedPlanner.Enabled ? RaceSpeedPlanner.Limit(course, raw, sign, cruise) :
            Vector3.Angle(target.forward, far.forward) > 15f ? Mathf.Min(cruise, 14f) : cruise;
        if (!RaceSpeedPlanner.Enabled && course.IsJumpAhead(body.position, sign, 35f)) { desired = Mathf.Max(24f, desired); }
        desired = Mathf.Min(desired, speedCap);
        float safeLane = Mathf.Max(0f, target.width * .5f - 2f);
        Vector3 local = Quaternion.Inverse(body.rotation) * (target.position + target.right * Mathf.Clamp(lane, -safeLane, safeLane) - body.position);
        float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg * 1.5f - Vector3.Dot(body.angularVelocity, target.up) * 8f;
        angle += Mathf.Sin(now * 2.7f) * noise;
        held = new DriveInputState { steering = Mathf.Clamp(angle, -30f, 30f),
            pedal = CpuRacePace.Pedal(desired, Vector3.Dot(body.linearVelocity, body.rotation * Vector3.forward), vehicle != null ? vehicle.CruisePedalForSpeed(desired) : 0f) };
        return held;
    }
    public void Clear() { held = DriveInputState.Neutral; nextDecision = 0f; }
}
