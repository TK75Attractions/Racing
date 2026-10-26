using UnityEngine;

/// <summary>Conservative straight-road drafting. CPUs can be targets but receive no added boost.</summary>
public sealed class SlipstreamController : MonoBehaviour
{
    private readonly SlipstreamCharge charge = new SlipstreamCharge();
    private readonly RaycastHit[] obstructions = new RaycastHit[16];
    private Gmanager manager;
    private RaceParticipant own;
    private DebugMover mover;
    private CarResetter resetter;
    private TireForce[] tires;
    private int recoveryCount;
    private ParticleSystem rewardBurst;
    public int SuccessfulBoosts { get; private set; }
    public float PursuitProgress => charge.Charge / SlipstreamCharge.ChargeSeconds;

    public void Configure(Gmanager race, RaceParticipant participant)
    {
        manager = race; own = participant; mover = GetComponent<DebugMover>();
        resetter = GetComponent<CarResetter>(); tires = GetComponentsInChildren<TireForce>();
    }
    private void FixedUpdate() { Tick(Time.fixedDeltaTime); }
    public void Clear()
    {
        charge.Clear(); mover?.CancelSlipstreamBoost();
    }
    public void Tick(float dt)
    {
        if (resetter != null && recoveryCount != resetter.RecoveryCount)
        { recoveryCount = resetter.RecoveryCount; Clear(); return; }
        if (manager == null || manager.state != Gmanager.State.Game || own == null || !own.IsRacing ||
            !RaceFeatureController.Enabled(RaceFeature.Slipstream) || !CanDrive())
        { Clear(); return; }
        RaceCourse course = manager.course;
        if (course == null || !course.HasValidPath ||
            course.IsJumpAhead(transform.position, manager.CourseProgressSign, 45f)) { Clear(); return; }
        float progress = course.GetProgressDistance(transform.position);
        if (!course.TryGetSampleAtProgress(progress, out RaceCourse.CourseSample here) ||
            !course.TryGetSampleAtProgress(progress + manager.CourseProgressSign * 40f, out RaceCourse.CourseSample ahead) ||
            Vector3.Angle(here.forward, ahead.forward) > 12f || !course.IsPointInsideDrivingArea(transform.position))
        { Clear(); return; }
        Vector3 forwardAxis = own.Body.rotation * Vector3.forward;
        Vector3 rightAxis = own.Body.rotation * Vector3.right;
        Vector3 upAxis = own.Body.rotation * Vector3.up;
        RaceParticipant target = null;
        float nearest = float.MaxValue;
        foreach (RaceParticipant candidate in manager.Participants)
        {
            if (candidate == own || !candidate.IsRacing) continue;
            Vector3 delta = candidate.Body.position - own.Body.position;
            float forward = Vector3.Dot(delta, forwardAxis);
            float raceGap = candidate.Progress - own.Progress;
            if (raceGap < 6f || raceGap > 22f || forward < 6f || forward > 20f ||
                Mathf.Abs(Vector3.Dot(delta, rightAxis)) > 2.5f ||
                Mathf.Abs(Vector3.Dot(delta, upAxis)) > 1.5f ||
                Vector3.Dot(forwardAxis, candidate.Body.rotation * Vector3.forward) < .96f ||
                Vector3.Dot(upAxis, candidate.Body.rotation * Vector3.up) < .96f ||
                candidate.Body.linearVelocity.magnitude < 8f ||
                candidate.Effects != null && (candidate.Effects.IsRocketActive || candidate.Effects.IsSpinning) ||
                forward >= nearest || !ClearSight(candidate)) continue;
            target = candidate; nearest = forward;
        }
        if (charge.Tick(target != null ? target.Index : -1, dt) && mover.StartSlipstreamBoost())
        {
            SuccessfulBoosts++;
            EmitDraftBurst();
            if (own != null && !own.IsCpu) RaceSoundEffects.PlayForPlayer(RaceSoundCue.Confirm, own.Index);
        }
    }
    private void EmitDraftBurst()
    {
        if (rewardBurst == null)
        {
            GameObject effect = new GameObject("Slipstream Release Ring");
            effect.transform.SetParent(transform, false);
            rewardBurst = effect.AddComponent<ParticleSystem>();
            var main = rewardBurst.main; main.loop = false; main.playOnAwake = false;
            main.startLifetime = .22f; main.startSpeed = 2.5f; main.startSize = .16f;
            main.startColor = new Color(.25f, .85f, 1f, .85f); main.maxParticles = 16;
            var shape = rewardBurst.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = .55f;
            var emission = rewardBurst.emission; emission.enabled = false;
            var renderer = rewardBurst.GetComponent<ParticleSystemRenderer>(); renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        rewardBurst.transform.SetPositionAndRotation(transform.position + transform.forward * 1.4f + transform.up * .65f, transform.rotation);
        rewardBurst.Emit(12);
    }
    private bool CanDrive()
    {
        if (mover == null || mover.IsInputSuppressed || mover.InputSource == null || !mover.InputSource.IsConnected ||
            mover.InputSource is PlayerDriveInputGate gate && (gate.NeedsPedalRelease || gate.IsBlocked) ||
            Vector3.Dot(own.Body.linearVelocity, own.Body.rotation * Vector3.forward) < 8f || Vector3.Dot(own.Body.rotation * Vector3.up, Vector3.up) < .85f ||
            own.Effects != null && (own.Effects.IsRocketActive || own.Effects.BlocksDriverInput ||
                own.Effects.IsOiled || own.Effects.IsConfused || own.Effects.IsRecoveryProtected)) return false;
        int grounded = 0; foreach (TireForce tire in tires) if (tire.IsGrounded) grounded++;
        return grounded >= 2;
    }
    private bool ClearSight(RaceParticipant target)
    {
        Vector3 start = own.Body.worldCenterOfMass, delta = target.Body.worldCenterOfMass - start;
        int count = Physics.RaycastNonAlloc(start, delta.normalized, obstructions, delta.magnitude,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (count == obstructions.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Rigidbody body = obstructions[i].rigidbody;
            if (body != own.Body && body != target.Body) return false;
        }
        return true;
    }
    private void OnDisable() { Clear(); }
}
