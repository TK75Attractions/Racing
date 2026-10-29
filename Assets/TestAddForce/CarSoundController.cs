using UnityEngine;

public class CarSoundController : MonoBehaviour
{
    public EngineAudioCore audioCore;
    public Rigidbody carRigidbody;
    [Range(0, 1)] public float masterVolume = .55f;
    [SerializeField] private int currentGear = 1;
    [SerializeField] private float currentRpm = AutomaticGearbox.IdleRpm;
    [SerializeField, Range(0, 1)] private float tunnelAmount;
    private readonly AutomaticGearbox gearbox = new AutomaticGearbox();
    private DebugMover mover;
    private TireForce[] tires;
    private float nextEnvironmentCheck;

    public int CurrentGear => currentGear;
    public float CurrentRpm => currentRpm;
    public float TunnelAmount => tunnelAmount;
    public bool IsShifting => gearbox.IsShifting;
    public int VehicleProfile => vehicleProfile;
    public bool IsReverse => carRigidbody != null && Vector3.Dot(carRigidbody.linearVelocity, transform.forward) < -.8f;
    private int vehicleProfile;

    public void ConfigureVehicleProfile(int profile)
    {
        vehicleProfile = Mathf.Clamp(profile, 0, 2);
        if (audioCore == null) audioCore = GetComponentInChildren<EngineAudioCore>();
        audioCore?.ConfigureVehicleProfile(vehicleProfile);
    }

    private void Awake()
    {
        if (carRigidbody == null) carRigidbody = GetComponentInParent<Rigidbody>();
        if (audioCore == null) audioCore = GetComponentInChildren<EngineAudioCore>();
        mover = GetComponentInParent<DebugMover>();
        tires = GetComponentsInChildren<TireForce>();
    }

    private void OnEnable() { gearbox.Reset(); nextEnvironmentCheck = 0f; }

    private void Update()
    {
        if (carRigidbody == null || audioCore == null) return;
        if (Gmanager.Control != null && !Gmanager.Control.IsEngineAudioActive(carRigidbody))
        {
            MuteEngine();
            return;
        }
        if (Time.time >= nextEnvironmentCheck)
        {
            nextEnvironmentCheck = Time.time + .1f;
            tunnelAmount = TunnelAcoustics.EnclosureAt(carRigidbody.worldCenterOfMass);
        }
        bool grounded = tires.Length == 0;
        foreach (TireForce tire in tires) grounded |= tire.IsGrounded;
        float pedal = mover != null && mover.isActiveAndEnabled ? mover.PedalInput : 0f;
        float throttle = Mathf.Clamp01(IsReverse ? -pedal : pedal);
        float boost = mover != null ? mover.BoostVisualIntensity : 0f;
        FinishRunController finishRun = GetComponentInParent<FinishRunController>();
        float finishGain = finishRun != null ? finishRun.EngineAudioGain : 1f;
        gearbox.Step(Time.deltaTime, Vector3.Dot(carRigidbody.linearVelocity, transform.forward), throttle, grounded, boost);
        currentGear = gearbox.Gear; currentRpm = gearbox.Rpm;
        audioCore.SetEngineState(currentRpm, gearbox.Load, Mathf.Clamp01(masterVolume) * finishGain * RaceAudioSettings.Engine * RaceAudioSettings.NotificationDuck *
            (Time.timeScale > 0f ? 1f : 0f), gearbox.Combustion, gearbox.ShiftSerial, boost, tunnelAmount);
    }

    private void OnDisable() => MuteEngine();

    private void MuteEngine()
    {
        if (audioCore != null)
            audioCore.SetEngineState(AutomaticGearbox.IdleRpm, 0f, 0f, 0f, 0, 0f, 0f);
    }
}
