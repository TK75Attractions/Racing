using UnityEngine;

/// <summary>Each human has an independent sequential gate and per-lap reward ledger.</summary>
public sealed class TubeChargeLineController : MonoBehaviour
{
    private readonly TubeLineChallenge challenge = new TubeLineChallenge();
    private Gmanager manager;
    private RaceParticipant own;
    private DebugMover mover;
    private CarResetter resetter;
    private GroundCheck[] wheels;
    private int recoveryCount;
    public DriverNoticeSignal Notices { get; } = new DriverNoticeSignal();
    private string guidance = "";
    public TubeChargeLineLayout Layout { get; private set; }
    public int SuccessfulLines { get; private set; }
    public string Guidance => RaceFeatureController.Enabled(RaceFeature.TubeChargeLines) ? guidance : "";
    public bool Pending => challenge.IsPending;
    public string Notice => RaceFeatureController.Enabled(RaceFeature.TubeChargeLines) && Notices.Text.Length > 0 ? Notices.Text : RaceFeatureController.Enabled(RaceFeature.TubeChargeLines) ? guidance : "";
    public void Configure(Gmanager race, RaceParticipant driver)
    {
        manager = race; own = driver; mover = GetComponent<DebugMover>(); resetter = GetComponent<CarResetter>();
        wheels = GetComponentsInChildren<GroundCheck>();
        if (race.course == null) return;
        Layout = race.course.GetComponent<TubeChargeLineLayout>() ?? race.course.gameObject.AddComponent<TubeChargeLineLayout>();
        Layout.Configure(race.course, race.CourseProgressSign);
    }
    public void Clear() { challenge.Clear(); Notices.Clear(); guidance = ""; }
    private void OnDisable() { Clear(); }
    private void FixedUpdate() { Tick(Time.fixedDeltaTime); }
    public void Tick(float dt)
    {
        if (resetter != null && recoveryCount != resetter.RecoveryCount)
        { recoveryCount = resetter.RecoveryCount; Clear(); return; }
        if (!CanDrive()) { Clear(); return; }
        RaceCourse course = manager.course;
        float raw = course.GetProgressDistance(own.Body.position);
        if (!course.TryGetSampleAtProgress(raw, out RaceCourse.CourseSample sample)) { Clear(); return; }
        int contacts = 0; foreach (GroundCheck wheel in wheels) if (wheel.CheckNow()) contacts++;
        float lateral = Vector3.Dot(own.Body.position - sample.position, sample.right);
        int lane = Mathf.Abs(Mathf.Abs(lateral) - Layout.LaneOffset) <= TubeChargeLineLayout.LaneTolerance ? (lateral < 0f ? -1 : 1) : 0;
        bool eligible = contacts >= 2 && course.HasRoadAtProgress(raw) &&
            Mathf.Abs(Vector3.Dot(own.Body.position - sample.position, sample.up)) < 2f &&
            Vector3.Dot(own.Body.rotation * Vector3.forward, sample.forward * manager.CourseProgressSign) > .95f &&
            Vector3.Dot(own.Body.rotation * Vector3.up, sample.up) > .95f;
        float distance = Layout.DistanceFromEntry(raw);
        int lap = manager.GetParticipantLap(own);
        guidance = eligible && distance >= -25f && distance < -2f && !challenge.WasAwarded(lap) ? "横の光るラインでチャージ" : "";
        if (!challenge.Tick(lap, lane, distance, eligible, dt)) return;
        if (!mover.AddDriftCharge(.25f)) return; // Full charge gets no false success notification.
        SuccessfulLines++; Notices.Publish("ライン成功！チャージ増加", DriverNoticePriority.Success, 1.5f);
        own.Effects?.NotifyChargeCollected();
    }
    private bool CanDrive() => manager != null && manager.state == Gmanager.State.Game && Layout != null && Layout.Available &&
        own != null && !own.IsCpu && own.IsRacing && RaceFeatureController.Enabled(RaceFeature.TubeChargeLines) &&
        mover != null && mover.enabled && !mover.IsInputSuppressed && mover.InputSource != null && mover.InputSource.IsConnected &&
        !(mover.InputSource is PlayerDriveInputGate gate && (gate.IsBlocked || gate.NeedsPedalRelease)) &&
        !(resetter != null && resetter.IsRecoveryPending) &&
        Vector3.Dot(own.Body.linearVelocity, own.Body.rotation * Vector3.forward) >= 6f &&
        (own.Effects == null || !(own.Effects.IsRocketActive || own.Effects.BlocksDriverInput || own.Effects.IsOiled ||
            own.Effects.IsConfused || own.Effects.IsRecoveryProtected));
}
