using UnityEngine;

/// <summary>A short, non-competing run through ordinary pedal/steering physics after finishing.</summary>
[DefaultExecutionOrder(-90)]
public sealed class FinishRunController : MonoBehaviour, IDriveInputSource
{
    public const float Duration = 3.5f;
    private readonly RoadObservationBot bot = new RoadObservationBot();
    private Gmanager manager;
    private RaceParticipant participant;
    private DebugMover mover;
    private GroundCheck[] wheels;
    private float elapsed, initialSpeed;
    public bool IsActive { get; private set; }
    public float ElapsedSeconds => elapsed;
    public int PlayerIndex => participant?.Index ?? -1;
    public string DeviceId => "Finish run";
    public bool IsConnected => true;
    public DriveInputState CurrentState { get; private set; }

    public void Begin(Gmanager race, RaceParticipant entry)
    {
        manager = race; participant = entry; mover = GetComponent<DebugMover>();
        wheels = GetComponentsInChildren<GroundCheck>();
        initialSpeed = entry.Body.linearVelocity.magnitude;
        var auto = GetComponent<PlayerAutodrive>(); if (auto != null) auto.enabled = false;
        if (entry.Driver != null) entry.Driver.enabled = false;
        var resetter = GetComponent<CarResetter>(); if (resetter != null) resetter.enabled = false;
        var effects = GetComponent<CarItemEffects>(); if (effects != null) { effects.ClearAll(); effects.enabled = false; }
        // Remain grounded and visible, while never pushing cars that are still racing.
        foreach (RaceParticipant other in race.Participants)
        {
            if (other == entry || other.Car == null) continue;
            foreach (Collider own in GetComponentsInChildren<Collider>())
            foreach (Collider theirs in other.Car.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(own, theirs);
        }
        elapsed = 0f; IsActive = true;
        mover.SetInputSource(this);
    }

    private void FixedUpdate()
    {
        if (!IsActive) return;
        if (!RaceFeatureController.Enabled(RaceFeature.FinishRun) ||
            (manager.state != Gmanager.State.Game && manager.state != Gmanager.State.Goal) ||
            elapsed >= Duration)
        { Stop(); return; }
        int contacts = 0; foreach (GroundCheck wheel in wheels) if (wheel.CheckNow()) contacts++;
        float cruise = Mathf.SmoothStep(Mathf.Clamp(initialSpeed, 12f, 26f), 0f, elapsed / Duration);
        CurrentState = bot.Decide(manager.course, participant.Body, manager.CourseProgressSign, contacts, Time.time,
            cruise, reaction: .04f, noise: 0f, vehicle: mover);
        elapsed += Time.fixedDeltaTime;
    }

    public void Stop()
    {
        if (!IsActive) return;
        IsActive = false; CurrentState = DriveInputState.Neutral;
        mover.enabled = false;
        foreach (CarStabilityController stability in GetComponentsInChildren<CarStabilityController>()) stability.enabled = false;
        foreach (CarSoundController sound in GetComponentsInChildren<CarSoundController>()) sound.enabled = false;
        foreach (AudioSource sound in GetComponentsInChildren<AudioSource>()) sound.Stop();
        foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        participant.Body.linearVelocity = Vector3.zero;
        participant.Body.angularVelocity = Vector3.zero;
        participant.Body.isKinematic = true;
    }

    public void UpdateInput(float dt) { }
    public void Dispose() { }
}
