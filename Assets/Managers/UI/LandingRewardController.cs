using System.Collections.Generic;
using UnityEngine;

/// <summary>Human-only rewards for the course's explicitly missing-road jump sections.</summary>
public sealed class LandingRewardController : MonoBehaviour
{
    private readonly LandingChallenge challenge = new LandingChallenge();
    private readonly List<RaceCourse.JumpSection> jumps = new List<RaceCourse.JumpSection>();
    private Gmanager manager;
    private RaceParticipant own;
    private DebugMover mover;
    private CarResetter resetter;
    private GroundCheck[] wheels;
    private RaceCourse.JumpSection activeJump;
    private int recoveryCount;
    private float noticeUntil, previousProgress, boostPendingUntil;
    private bool hasProgress;
    public int SuccessfulLandings { get; private set; }
    public int RewardBoosts { get; private set; }
    public string Notice => RaceFeatureController.Enabled(RaceFeature.LandingFeedback) && Time.time < noticeUntil ? "着地成功！" : "";
    public void Configure(Gmanager race, RaceParticipant driver)
    {
        manager = race; own = driver; mover = GetComponent<DebugMover>();
        resetter = GetComponent<CarResetter>(); wheels = GetComponentsInChildren<GroundCheck>();
        race.course?.CopyJumpSections(jumps, race.CourseProgressSign);
    }
    public void ApplySwitches()
    {
        if (!RaceFeatureController.Enabled(RaceFeature.LandingBoost)) { mover?.CancelLandingBoost(); boostPendingUntil = 0f; }
        if (!RaceFeatureController.Enabled(RaceFeature.LandingFeedback)) noticeUntil = 0;
        if (!RaceFeatureController.Enabled(RaceFeature.LandingFeedback) && !RaceFeatureController.Enabled(RaceFeature.LandingBoost)) Clear();
    }
    public void Clear() { challenge.Clear(); boostPendingUntil = 0f; noticeUntil = 0; mover?.CancelLandingBoost(); hasProgress = false; }
    private void FixedUpdate() { Tick(Time.fixedDeltaTime); }
    public void Tick(float dt)
    {
        if (resetter != null && recoveryCount != resetter.RecoveryCount)
        { recoveryCount = resetter.RecoveryCount; Clear(); return; }
        bool enabled = RaceFeatureController.Enabled(RaceFeature.LandingFeedback) || RaceFeatureController.Enabled(RaceFeature.LandingBoost);
        if (!enabled || !CanAttempt()) { Clear(); return; }
        if (boostPendingUntil > 0f)
        {
            if (!RaceFeatureController.Enabled(RaceFeature.LandingBoost) || Time.time > boostPendingUntil) boostPendingUntil = 0f;
            else if (mover.StartLandingBoost()) { RewardBoosts++; boostPendingUntil = 0f; }
        }
        RaceCourse course = manager.course;
        float raw = course.GetProgressDistance(own.Body.position);
        float delta = hasProgress ? DirectedDelta(previousProgress, raw, course) : 0f;
        previousProgress = raw; hasProgress = true;
        if (delta < -.15f || delta > Mathf.Max(4f, own.Body.linearVelocity.magnitude * dt * 3f)) { Clear(); return; }
        if (!course.TryGetSampleAtProgress(raw, out RaceCourse.CourseSample sample)) { Clear(); return; }
        int contacts = 0; foreach (GroundCheck wheel in wheels) if (wheel.CheckNow()) contacts++;
        Vector3 forward = own.Body.rotation * Vector3.forward;
        bool aligned = Vector3.Dot(forward, sample.forward * manager.CourseProgressSign) > .65f;
        bool stable = contacts >= 2 && aligned && own.Body.angularVelocity.magnitude < 4.5f &&
            Vector3.Dot(own.Body.rotation * Vector3.up, sample.up) > .65f &&
            Mathf.Abs(Vector3.Dot(own.Body.linearVelocity, sample.up)) < 8f &&
            Mathf.Abs(Vector3.Dot(own.Body.position - sample.position, sample.up)) < 3f &&
            Mathf.Abs(Vector3.Dot(own.Body.position - sample.position, sample.right)) < sample.width * .5f - .5f;
        if (!challenge.Armed && stable)
            foreach (RaceCourse.JumpSection jump in jumps)
            {
                float until = DirectedDelta(raw, jump.takeoff, course);
                if (until < 0f || until > 20f) continue;
                if (challenge.TryArm(jump.id, manager.GetParticipantLap(own))) { activeJump = jump; break; }
            }
        float afterLanding = DirectedDelta(activeJump.landing, raw, course);
        bool landing = course.HasRoadAtProgress(raw) && afterLanding >= 0f && afterLanding <= 60f && stable;
        if (!challenge.Tick(true, contacts, !course.HasRoadAtProgress(raw), landing, dt)) return;
        SuccessfulLandings++;
        if (RaceFeatureController.Enabled(RaceFeature.LandingFeedback))
        { noticeUntil = Time.time + 1.8f; RaceSoundEffects.PlayForPlayer(RaceSoundCue.Ready, own.Index); }
        if (RaceFeatureController.Enabled(RaceFeature.LandingBoost))
        {
            if (mover.StartLandingBoost()) RewardBoosts++;
            else boostPendingUntil = Time.time + .9f; // A front-axle touchdown may precede stable rear-wheel contact.
        }
    }
    private float DirectedDelta(float from, float to, RaceCourse course)
    {
        float delta = (to - from) * manager.CourseProgressSign;
        if (course.ClosedLoop) delta = Mathf.Repeat(delta + course.TotalLength * .5f, course.TotalLength) - course.TotalLength * .5f;
        return delta;
    }
    private bool CanAttempt() => manager != null && manager.state == Gmanager.State.Game && manager.course != null &&
        own != null && !own.IsCpu && own.IsRacing && mover != null && mover.enabled && !mover.IsInputSuppressed &&
        mover.InputSource != null && mover.InputSource.IsConnected &&
        !(mover.InputSource is PlayerDriveInputGate gate && (gate.IsBlocked || gate.NeedsPedalRelease)) &&
        !(resetter != null && resetter.IsRecoveryPending) &&
        Vector3.Dot(own.Body.linearVelocity, own.Body.rotation * Vector3.forward) >= 4f &&
        (own.Effects == null || !(own.Effects.IsRocketActive || own.Effects.BlocksDriverInput || own.Effects.IsOiled ||
            own.Effects.IsConfused || own.Effects.IsRecoveryProtected));
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.relativeVelocity.magnitude < 6f) return;
        for (int i = 0; i < collision.contactCount; i++)
            if (Vector3.Dot(collision.GetContact(i).normal, Vector3.up) < .35f) { Clear(); return; }
    }
    private void OnDisable() { Clear(); }
}
