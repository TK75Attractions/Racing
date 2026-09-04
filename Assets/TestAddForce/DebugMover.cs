using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(Rigidbody))]
public class DebugMover : MonoBehaviour
{
    [Header("Front-Wheel Drive")]
    [Tooltip("接地している前輪1本あたりの最大駆動力。")]
    [SerializeField, Min(0f)] private float driveForcePerFrontWheel = 15f;

    [Header("Velocity Resistance")]
    [Tooltip("水平速度に比例し、速度と反対方向に加える抵抗力の係数。")]
    [SerializeField, Min(0f)] private float velocityResistance = 0.6f;

    [Header("Steering")]
    [Tooltip("入力ハンドル値を前輪角度に変換する倍率。")]
    [SerializeField, Min(0f)] private float steeringInputMultiplier = 1f;

    [Tooltip("前輪の最大操舵角（度）。")]
    [SerializeField, Range(0f, 60f)] private float maxSteeringAngle = 30f;

    [Tooltip("この速度（m/s）から操舵角を小さくし始めます。")]
    [SerializeField, Min(0f)] private float steeringFadeStartSpeed = 10f;

    [Tooltip("この速度（m/s）で高速時操舵倍率に達します。")]
    [SerializeField, Min(0f)] private float steeringFadeFullSpeed = 30f;

    [Tooltip("高速時に残す操舵角の割合。")]
    [SerializeField, Range(0f, 1f)] private float highSpeedSteeringMultiplier = 0.35f;

    [Header("Respawn")]
    [Tooltip("リスポーン直後にアクセル、ハンドルなどの運転入力を無効化する時間（秒）。")]
    [FormerlySerializedAs("respawnSteeringSuppressionSeconds")]
    [SerializeField, Min(0f)] private float respawnInputSuppressionSeconds = 0.3f;

    [Header("Tire Lateral Force")]
    [Tooltip("前輪の横滑り速度を横力に変換する係数。")]
    [SerializeField, Min(0f)] private float frontCorneringStiffness = 10f;

    [Tooltip("後輪の横滑り速度を横力に変換する係数。")]
    [SerializeField, Min(0f)] private float rearCorneringStiffness = 12f;

    [Tooltip("タイヤ1本あたりの横力上限。")]
    [SerializeField, Min(0f)] private float maxLateralForcePerTire = 15f;

    [Header("Runtime References")]
    [SerializeField] private List<TireForce> tires = new List<TireForce>();

    [Header("Runtime Monitor")]
    [SerializeField] private float speedMetersPerSecond;
    [SerializeField] private float rawPedalInput;
    [SerializeField] private float appliedPedalInput;
    [SerializeField] private float rawSteeringInput;
    [SerializeField] private float appliedSteeringAngle;
    [SerializeField] private float resistanceForce;

    private Rigidbody rb;
    private IDriveInputSource inputSource;
    private float inputSuppressedUntil;

    public IDriveInputSource InputSource => inputSource;
    public bool IsInputSuppressed => Time.time < inputSuppressedUntil;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        RefreshTires();
    }

    private void FixedUpdate()
    {
        if (inputSource == null ||
            Gmanager.Control == null ||
            !Gmanager.Control.IsDrivingEnabled)
        {
            return;
        }

        if (IsInputSuppressed)
        {
            ClearUserInput();
        }
        else
        {
            ReadInput();
        }

        ApplyTireForces();
        ApplyVelocityResistance();
    }

    public void SetInputSource(IDriveInputSource source)
    {
        inputSource = source;
    }

    public void SuppressInputAfterRespawn()
    {
        float duration = Mathf.Max(0f, respawnInputSuppressionSeconds);
        inputSuppressedUntil = Mathf.Max(inputSuppressedUntil, Time.time + duration);
        ClearUserInput();

        foreach (TireForce tire in tires)
        {
            tire.CenterVisualSteering();
        }
    }

    private void RefreshTires()
    {
        tires.Clear();
        tires.AddRange(GetComponentsInChildren<TireForce>());

        foreach (TireForce tire in tires)
        {
            tire.Init(rb);
        }
    }

    private void ReadInput()
    {
        DriveInputState input = inputSource.CurrentState;
        rawPedalInput = input.pedal;
        appliedPedalInput = Mathf.Clamp(rawPedalInput, -1f, 1f);
        rawSteeringInput = input.steering;

        Vector3 planarVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        speedMetersPerSecond = planarVelocity.magnitude;

        float fullSpeed = Mathf.Max(steeringFadeStartSpeed + 0.01f, steeringFadeFullSpeed);
        float speedRatio = Mathf.InverseLerp(steeringFadeStartSpeed, fullSpeed, speedMetersPerSecond);
        float speedSteeringMultiplier = Mathf.Lerp(1f, highSpeedSteeringMultiplier, speedRatio);

        appliedSteeringAngle = Mathf.Clamp(
            rawSteeringInput * steeringInputMultiplier * speedSteeringMultiplier,
            -maxSteeringAngle,
            maxSteeringAngle);
    }

    private void ClearUserInput()
    {
        rawPedalInput = 0f;
        appliedPedalInput = 0f;
        rawSteeringInput = 0f;
        appliedSteeringAngle = 0f;
    }

    private void ApplyTireForces()
    {
        Vector3 vehicleForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        foreach (TireForce tire in tires)
        {
            float corneringStiffness = tire.IsFrontWheel
                ? frontCorneringStiffness
                : rearCorneringStiffness;

            tire.ApplyForces(
                vehicleForward,
                Vector3.up,
                appliedSteeringAngle,
                appliedPedalInput,
                driveForcePerFrontWheel,
                corneringStiffness,
                maxLateralForcePerTire);
        }
    }

    private void ApplyVelocityResistance()
    {
        Vector3 planarVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        Vector3 resistance = -planarVelocity * velocityResistance;
        resistanceForce = resistance.magnitude;
        rb.AddForce(resistance, ForceMode.Force);
    }
}
