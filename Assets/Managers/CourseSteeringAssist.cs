using UnityEngine;

/// <summary>Human-only edge aid. Never changes driver input, drift detection, pose or speed.</summary>
public sealed class CourseSteeringAssist : MonoBehaviour
{
    private Gmanager manager;
    private RaceParticipant participant;
    private DebugMover mover;
    private CarResetter resetter;
    private GroundCheck[] wheels;
    public float CorrectionDegrees { get; private set; }
    public float ActiveSeconds { get; private set; }
    public void Configure(Gmanager race, RaceParticipant driver)
    {
        manager = race; participant = driver; mover = GetComponent<DebugMover>();
        resetter = GetComponent<CarResetter>(); wheels = GetComponentsInChildren<GroundCheck>();
    }
    public void Clear() { CorrectionDegrees = 0f; }
    public float Assist(float driverAngle, float limit, Vector3 drivingUp, float dt)
    {
        if (!Eligible(drivingUp)) { Clear(); return driverAngle; }
        RaceCourse course = manager.course;
        Vector3 position = participant.Body.position, velocity = participant.Body.linearVelocity;
        float progress = course.GetProgressDistance(position);
        if (!course.TryGetSampleAtProgress(progress, out RaceCourse.CourseSample here) ||
            !course.HasRoadAtProgress(progress) || Mathf.Abs(Vector3.Dot(position - here.position, here.up)) > 2f ||
            Vector3.Dot(drivingUp, here.up) < .95f ||
            Vector3.Dot(participant.Body.rotation * Vector3.forward, here.forward * manager.CourseProgressSign) < .6f ||
            course.IsJumpAhead(position, manager.CourseProgressSign, Mathf.Max(15f, velocity.magnitude)) ||
            !course.TryGetSampleAtProgress(progress + Vector3.Dot(velocity, here.forward) * .55f, out RaceCourse.CourseSample ahead))
        { Clear(); return driverAngle; }
        float sign = manager.CourseProgressSign;
        float lateral = Vector3.Dot(position - here.position, here.right * sign);
        float predicted = Vector3.Dot(position + velocity * .55f - ahead.position, ahead.right * sign);
        float target = EdgeSteeringAssist.Correction(lateral, predicted, Mathf.Min(here.width, ahead.width) * .5f, limit);
        // The course band is not the tube's drivable boundary. Leave the complete tube to the driver.
        if ((target != 0f || CorrectionDegrees != 0f) && course.IsPointInsideTube(position))
        { Clear(); return driverAngle; }
        CorrectionDegrees = Mathf.MoveTowards(CorrectionDegrees, target, 12f * Mathf.Max(0f, dt));
        if (Mathf.Abs(CorrectionDegrees) > .1f) ActiveSeconds += dt;
        return Mathf.Clamp(driverAngle + CorrectionDegrees, -limit, limit);
    }
    private bool Eligible(Vector3 up)
    {
        if (manager == null || participant == null || participant.IsCpu || !participant.IsRacing ||
            manager.state != Gmanager.State.Game || manager.course == null ||
            !RaceFeatureController.Enabled(RaceFeature.SteeringAssist) || mover == null || !mover.enabled ||
            mover.IsInputSuppressed || mover.InputSource == null || !mover.InputSource.IsConnected ||
            mover.InputSource is PlayerDriveInputGate gate && (gate.IsBlocked || gate.NeedsPedalRelease) ||
            resetter != null && resetter.IsRecoveryPending || Vector3.Dot(up, Vector3.up) < .85f ||
            Vector3.Dot(participant.Body.rotation * Vector3.up, up) < .9f ||
            Vector3.Dot(participant.Body.linearVelocity, participant.Body.rotation * Vector3.forward) < 4f ||
            participant.Effects != null && (participant.Effects.BlocksDriverInput || participant.Effects.IsRocketActive ||
                participant.Effects.IsOiled || participant.Effects.IsConfused || participant.Effects.IsRecoveryProtected)) return false;
        int contacts = 0; foreach (GroundCheck wheel in wheels) if (wheel.CheckNow()) contacts++;
        return contacts >= 2;
    }
    private void OnDisable() { Clear(); }
}
