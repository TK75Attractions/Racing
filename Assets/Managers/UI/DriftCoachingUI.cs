using UnityEngine;

/// <summary>Short contextual guidance, displayed through the existing safety-notice priority chain.</summary>
public sealed class DriftCoachingUI : MonoBehaviour
{
    private Gmanager manager;
    private DebugMover mover;
    private CarItemEffects effects;
    private TireForce[] tires;
    private int playerIndex;
    private float messageUntil, lastHintAt = -100f;
    private string message = "";
    private bool turnHintShown, releaseHintShown;
    public int SuccessfulReleases { get; private set; }
    public string Notice => RaceFeatureController.Enabled(RaceFeature.DriftCoaching) && Time.time < messageUntil && (effects == null || !(effects.IsConfused || effects.IsOiled || effects.IsSpinning)) ? message : "";

    public void Configure(Gmanager race, DebugMover car, int index)
    {
        manager = race; mover = car; playerIndex = index;
        effects = GetComponent<CarItemEffects>(); tires = GetComponentsInChildren<TireForce>();
        mover.DriftBoostReleased += OnRelease;
    }
    private void OnDestroy() { if (mover != null) mover.DriftBoostReleased -= OnRelease; }
    private void OnRelease(float normalizedCharge)
    {
        if (manager == null || manager.state != Gmanager.State.Game || normalizedCharge < .1f) return;
        SuccessfulReleases++;
        if (!RaceFeatureController.Enabled(RaceFeature.DriftCoaching)) return;
        Show("ドリフト加速！", 1.2f);
        RaceSoundEffects.PlayForPlayer(RaceSoundCue.Ready, playerIndex);
    }
    private void Show(string text, float seconds)
    {
        message = text; messageUntil = Time.time + seconds; lastHintAt = Time.time;
    }
    private void Update()
    {
        if (!RaceFeatureController.Enabled(RaceFeature.DriftCoaching) || manager == null ||
            manager.state != Gmanager.State.Game || mover == null || mover.IsInputSuppressed ||
            mover.InputSource == null || !mover.InputSource.IsConnected ||
            mover.InputSource is PlayerDriveInputGate gate && (gate.NeedsPedalRelease || gate.IsBlocked) ||
            effects != null && (effects.BlocksDriverInput || effects.IsRocketActive || effects.IsConfused || effects.IsOiled))
        { message = ""; messageUntil = 0f; return; }
        if (SuccessfulReleases > 0 || Time.time - lastHintAt < 4f || !SafeRoadAhead()) return;
        if (mover.NormalizedDriftCharge >= .2f && !releaseHintShown && mover.IsDrifting)
        {
            releaseHintShown = true; Show("少し逆に切って加速", 2.4f);
        }
        else if (mover.IsDrifting && !turnHintShown)
        {
            turnHintShown = true; Show("曲がってためる", 2f);
        }
    }
    private bool SafeRoadAhead()
    {
        RaceCourse course = manager.course;
        if (course == null || !course.HasValidPath || course.IsJumpAhead(transform.position, manager.CourseProgressSign, 60f)) return false;
        int grounded = 0; foreach (TireForce tire in tires) if (tire.IsGrounded) grounded++;
        if (grounded < 2) return false;
        float progress = course.GetProgressDistance(transform.position);
        if (!course.TryGetSampleAtProgress(progress, out RaceCourse.CourseSample here) || here.width < 10f ||
            Vector3.Dot(transform.up, Vector3.up) < .85f) return false;
        return course.TryGetSampleAtProgress(progress + manager.CourseProgressSign * 30f, out RaceCourse.CourseSample ahead) &&
            Vector3.Angle(here.forward, ahead.forward) < 35f;
    }
}
