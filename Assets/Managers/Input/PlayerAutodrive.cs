using UnityEngine;

/// <summary>Opt-in P2 test driver in normal builds, retaining the normal pause/recovery/input gate.</summary>
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
    public bool IsActive { get; private set; }
    public bool WasUsed { get; private set; }
    public int PlayerIndex => own?.Index ?? 1;
    public string DeviceId => "P2 test autodrive";
    public bool IsConnected => true;
    public DriveInputState CurrentState { get; private set; }
    public void Configure(Gmanager race, RaceParticipant participant)
    {
        manager = race; own = participant; mover = GetComponent<DebugMover>();
        gate = mover.InputSource as PlayerDriveInputGate; manual = gate?.Source;
        wheels = GetComponentsInChildren<GroundCheck>(); ApplySwitch();
    }
    public void ApplySwitch()
    {
        bool next = own != null && own.Index == 1 && RaceFeatureController.Enabled(RaceFeature.PlayerTwoAutodrive);
        if (next == IsActive || gate == null) return;
        CurrentState = DriveInputState.Neutral; bot.Clear();
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
        if (manager.course.IsJumpAhead(own.Body.position, manager.CourseProgressSign, 90f))
            foreach (RaceParticipant other in manager.Participants)
            {
                if (other == own || !other.IsRacing) continue;
                cap = CpuJumpSpacing.Request(cap, other.Body.linearVelocity.magnitude, other.Progress - own.Progress);
            }
        DriveInputState decision = bot.Decide(manager.course, own.Body, manager.CourseProgressSign, contacts, Time.time,
            cruise, -1.8f, reaction: .06f, speedCap: cap, vehicle: mover);
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
