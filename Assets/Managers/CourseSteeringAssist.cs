using UnityEngine;

/// <summary>Human-only edge aid. Never changes driver input, drift detection, pose or speed.</summary>
public sealed class CourseSteeringAssist : MonoBehaviour
{
    private Gmanager manager;
    private RaceParticipant participant;
    private DebugMover mover;
    private CarResetter resetter;
    private GroundCheck[] wheels;
    private float returnUntil;
    public float CorrectionDegrees { get; private set; }
    public float ActiveSeconds { get; private set; }
    public void Configure(Gmanager race, RaceParticipant driver)
    {
        manager = race; participant = driver; mover = GetComponent<DebugMover>();
        resetter = GetComponent<CarResetter>(); wheels = GetComponentsInChildren<GroundCheck>();
    }
    public void Clear() { CorrectionDegrees = 0f; returnUntil = 0f; }
    public float Assist(float driverAngle, float limit, Vector3 drivingUp, float dt)
    {
        bool inTube = manager?.course != null && participant?.Body != null && manager.course.IsPointInsideTube(participant.Body.position);
        if (!Eligible(drivingUp, inTube)) { Clear(); return driverAngle; }
        RaceCourse course = manager.course;
        Vector3 position = participant.Body.position, velocity = participant.Body.linearVelocity;
        float progress = course.GetProgressDistance(position);
        if (inTube) return AssistTube(driverAngle, limit, drivingUp, dt, progress);
        if (!course.TryGetSampleAtProgress(progress, out RaceCourse.CourseSample here) ||
            !course.HasRoadAtProgress(progress) || Mathf.Abs(Vector3.Dot(position - here.position, here.up)) > 2f ||
            Vector3.Dot(drivingUp, here.up) < .95f ||
            Vector3.Dot(participant.Body.rotation * Vector3.forward, here.forward * manager.CourseProgressSign) < .3f ||
            course.IsJumpAhead(position, manager.CourseProgressSign, Mathf.Max(15f, velocity.magnitude)) ||
            !course.TryGetSampleAtProgress(progress + Vector3.Dot(velocity, here.forward) * .55f, out RaceCourse.CourseSample ahead))
        { Clear(); return driverAngle; }
        float sign = manager.CourseProgressSign;
        float lateral = Vector3.Dot(position - here.position, here.right * sign);
        float predicted = Vector3.Dot(position + velocity * .55f - ahead.position, ahead.right * sign);
        float halfWidth = Mathf.Min(here.width, ahead.width) * .5f;
        float intent = EdgeSteeringAssist.WallRideIntent(driverAngle, lateral, predicted, halfWidth, limit);
        // Releasing or turning inward restores departure protection. An intentional wall
        // entry must also cancel the lingering return-to-road correction immediately.
        if (intent >= .99f) { Clear(); return driverAngle; }
        float target = EdgeSteeringAssist.Correction(lateral, predicted, Mathf.Min(here.width, ahead.width) * .5f, limit);
        if (target != 0f) returnUntil = Time.time + .35f;
        course.TryGetSampleAtProgress(progress + sign * Mathf.Clamp(8f + velocity.magnitude * .35f, 10f, 20f), out RaceCourse.CourseSample recovery);
        float laneLimit = Mathf.Max(0f, Mathf.Min(here.width, recovery.width) * .5f - 3f);
        Vector3 aim = recovery.position + recovery.right * sign * Mathf.Clamp(lateral, -laneLimit, laneLimit) - position;
        Vector3 local = Quaternion.Inverse(participant.Body.rotation) * aim;
        float roadAngle = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg * 1.5f -
            Vector3.Dot(participant.Body.angularVelocity, drivingUp) * 6f, -limit, limit);
        float assisted = EdgeSteeringAssist.Blend(driverAngle, roadAngle, lateral, predicted,
            Mathf.Min(here.width, ahead.width) * .5f, limit);
        if (target == 0f && Time.time < returnUntil && intent <= 0f) assisted = roadAngle;
        CorrectionDegrees = Mathf.MoveTowards(CorrectionDegrees, assisted - driverAngle, 90f * Mathf.Max(0f, dt));
        if (Mathf.Abs(CorrectionDegrees) > .1f) ActiveSeconds += dt;
        return Mathf.Clamp(driverAngle + CorrectionDegrees, -limit, limit);
    }
    private float AssistTube(float driverAngle, float limit, Vector3 up, float dt, float progress)
    {
        // Follow the tube's axis on the current contact plane, including banked walls.
        // Do not pull a wall rider toward the flat road's centre or change their pose.
        float sign=manager.CourseProgressSign;
        float lead=Mathf.Clamp(participant.Body.linearVelocity.magnitude*.35f,8f,20f);
        if (!manager.course.TryGetSampleAtProgress(progress+sign*lead,out var ahead))
        { Clear(); return driverAngle; }
        Vector3 forward=Vector3.ProjectOnPlane(participant.Body.rotation*Vector3.forward,up);
        Vector3 direction=Vector3.ProjectOnPlane(ahead.forward*sign,up);
        if (forward.sqrMagnitude<.01f || direction.sqrMagnitude<.01f || Vector3.Dot(forward.normalized,direction.normalized)<.3f)
        { Clear(); return driverAngle; }
        float heading=Vector3.SignedAngle(forward,direction,up);
        float yaw=Vector3.Dot(participant.Body.angularVelocity,up)*Mathf.Rad2Deg;
        float assisted=TubeSteeringAssist.Blend(driverAngle,heading,yaw,limit);
        returnUntil=0f;
        CorrectionDegrees=Mathf.MoveTowards(CorrectionDegrees,assisted-driverAngle,90f*Mathf.Max(0f,dt));
        if (Mathf.Abs(CorrectionDegrees)>.1f) ActiveSeconds+=Mathf.Max(0f,dt);
        return Mathf.Clamp(driverAngle+CorrectionDegrees,-limit,limit);
    }
    private bool Eligible(Vector3 up, bool inTube)
    {
        if (manager == null || participant == null || participant.IsCpu || !participant.IsRacing ||
            manager.state != Gmanager.State.Game || manager.course == null ||
            !RaceFeatureController.Enabled(RaceFeature.SteeringAssist) || mover == null || !mover.enabled ||
            mover.IsInputSuppressed || mover.InputSource == null || !mover.InputSource.IsConnected ||
            mover.InputSource is PlayerDriveInputGate gate && (gate.IsBlocked || gate.NeedsPedalRelease) ||
            resetter != null && resetter.IsRecoveryPending || !inTube && Vector3.Dot(up, Vector3.up) < .85f ||
            Vector3.Dot(participant.Body.rotation * Vector3.up, up) < .9f ||
            Vector3.Dot(participant.Body.linearVelocity, participant.Body.rotation * Vector3.forward) < 4f ||
            participant.Effects != null && (participant.Effects.BlocksDriverInput || participant.Effects.IsRocketActive ||
                participant.Effects.IsOiled || participant.Effects.IsConfused || participant.Effects.IsRecoveryProtected)) return false;
        int contacts = 0; foreach (GroundCheck wheel in wheels) if (wheel.CheckNow()) contacts++;
        return contacts >= 2;
    }
    private void OnDisable() { Clear(); }
}
