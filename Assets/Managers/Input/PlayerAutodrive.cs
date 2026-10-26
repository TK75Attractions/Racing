using UnityEngine;

/// <summary>Input-driven test racers retain the production drivetrain, pause and recovery gates.</summary>
[DefaultExecutionOrder(-150)]
public sealed class PlayerAutodrive : MonoBehaviour, IDriveInputSource
{
    private readonly RoadObservationBot bot = new RoadObservationBot();
    private Gmanager manager;
    private RaceParticipant own;
    private PlayerDriveInputGate gate;
    private IDriveInputSource manual;
    private GroundCheck[] wheels;
    private DebugMover mover;
    private RaceHumanPace humanPace;
    private float paceCruise=18f;
    public float PaceCruise => paceCruise;
    public bool IsActive { get; private set; }
    public bool WasUsed { get; private set; }
    public int PlayerIndex => own?.Index ?? 1;
    public string DeviceId => "P" + (PlayerIndex + 1) + " test autodrive";
    public bool IsConnected => true;
    public DriveInputState CurrentState { get; private set; }
    public void Configure(Gmanager race, RaceParticipant participant)
    {
        manager = race; own = participant; mover = GetComponent<DebugMover>();
        humanPace=RaceHumanPace.For(race);
        gate = mover.InputSource as PlayerDriveInputGate; manual = gate?.Source;
        wheels = GetComponentsInChildren<GroundCheck>(); ApplySwitch();
    }
    public void ApplySwitch()
    {
        if (own?.Result != null) return;
        bool next = own != null && (RacingStandalonePerformanceProbe.AutorunEnabled || manager.BothPlayersAutodrive || own.Index == 1 && RaceFeatureController.Enabled(RaceFeature.PlayerTwoAutodrive));
        if (next == IsActive || gate == null) return;
        CurrentState = DriveInputState.Neutral; bot.Clear();
        paceCruise=18f;
        gate.SwitchSource(next ? this : manual); IsActive = next; WasUsed |= next;
        mover.SetInputSource(gate); GetComponent<CarResetter>()?.SetInputSource(gate);
    }
    private void FixedUpdate()
    {
        if (!IsActive || manager == null || manager.state != Gmanager.State.Game || !own.IsRacing ||
            gate.IsBlocked || gate.NeedsPedalRelease || mover.IsInputSuppressed)
        { CurrentState = DriveInputState.Neutral; bot.Clear(); return; }
        int contacts = 0; foreach (GroundCheck wheel in wheels) if (wheel.CheckNow()) contacts++;
        float cruise = RaceSpeedPlanner.Enabled ? RaceSpeedPlanner.Cruise : 24f;
        float cap = cruise;
        var partner=manager.GetParticipant(0);
        if (RaceHumanPace.Enabled && RaceHumanPace.ManualRacer(partner))
        {
            float wanted=CpuRacePace.CompanionRequest(humanPace.Pace(0),partner.Progress-own.Progress,cruise);
            paceCruise=Mathf.MoveTowards(paceCruise,wanted,Time.fixedDeltaTime*(wanted<paceCruise?4f:6f));
            cap=paceCruise;
            if(manager.course.IsJumpAhead(own.Body.position,manager.CourseProgressSign,60f))cap=Mathf.Max(cap,24f);
        }
        if (manager.course.IsJumpAhead(own.Body.position, manager.CourseProgressSign, 90f))
            foreach (RaceParticipant other in manager.Participants)
            {
                if (other == own || !other.IsRacing) continue;
                cap = CpuJumpSpacing.Request(cap, other.Body.linearVelocity.magnitude, other.Progress - own.Progress);
            }
        DriveInputState decision = bot.Decide(manager.course, own.Body, manager.CourseProgressSign, contacts, Time.time,
            cruise, own.Index == 0 ? 1.8f : -1.8f, reaction: .06f + own.Index * .01f, speedCap: cap, vehicle: mover);
        foreach (RaceParticipant other in manager.Participants)
        {
            if (other == own || !other.IsRacing) continue;
            Vector3 local = Quaternion.Inverse(own.Body.rotation) * (other.Body.position - own.Body.position);
            if (local.z <= 0f || local.z >= 12f || Mathf.Abs(local.x) > 2.1f || Mathf.Abs(local.y) > 2f) continue;
            float target = Mathf.Max(0f, other.Body.linearVelocity.magnitude - Mathf.Max(0f, 8f - local.z) * .5f);
            decision.pedal = Mathf.Min(decision.pedal, CpuRacePace.Pedal(target, Vector3.Dot(own.Body.linearVelocity, own.Body.rotation * Vector3.forward), mover.CruisePedalForSpeed(target)));
        }
        CurrentState = decision;
    }
    public void UpdateInput(float dt) { }
    public void Dispose() { }
    private void OnDisable() { CurrentState = DriveInputState.Neutral; bot.Clear(); }
}
