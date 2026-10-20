using System.Collections.Generic;
using UnityEngine;

/// <summary>物理・カメラへ伝わらない、表示用車体だけの小さな姿勢変化。</summary>
[DisallowMultipleComponent]
public sealed class CarBodyMotion : MonoBehaviour
{
    [SerializeField, Min(0f)] private float maximumBounce = .018f;
    [SerializeField, Range(0f, 4f)] private float maximumPitch = 1.6f;
    [SerializeField, Range(0f, 4f)] private float maximumRoll = 2f;
    private readonly List<Transform> visuals = new List<Transform>();
    private readonly List<Vector3> basePositions = new List<Vector3>();
    private Rigidbody body;
    private DebugMover mover;
    private TireForce[] tires;
    private float phase;
    private float amplitude;
    private float landingPulse;
    private bool wasGrounded;
    private Quaternion[] baseRotations;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        mover = GetComponent<DebugMover>();
        tires = GetComponentsInChildren<TireForce>();
        // Only move complete visual branches; physics and tire transforms stay untouched.
        foreach (Transform child in transform)
        {
            if (child.GetComponentInChildren<MeshRenderer>(true) == null ||
                child.GetComponentInChildren<Collider>(true) != null ||
                child.GetComponentInChildren<TireForce>(true) != null ||
                child.GetComponentInChildren<Rigidbody>(true) != null) continue;
            visuals.Add(child);
            basePositions.Add(child.localPosition);
        }
        baseRotations = new Quaternion[visuals.Count];
        for (int index = 0; index < visuals.Count; index++) baseRotations[index] = visuals[index].localRotation;
    }

    private void LateUpdate() => Tick(Time.deltaTime);

    public void Tick(float deltaTime)
    {
        float speed = body != null ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude : 0f;
        bool grounded = false;
        if (mover != null)
            foreach (TireForce tire in tires)
                grounded |= tire.IsGrounded;
        if (grounded && !wasGrounded) landingPulse = 1f;
        wasGrounded = grounded;
        landingPulse = Mathf.MoveTowards(landingPulse, 0f, Mathf.Max(0f, deltaTime) * 4f);
        bool active = mover != null && mover.isActiveAndEnabled && !mover.IsInputSuppressed &&
            Gmanager.Control != null && Gmanager.Control.IsDrivingEnabled;
        float strength = active ? Mathf.InverseLerp(3f, 30f, speed) : 0f;
        float pedal = mover != null ? mover.PedalInput : 0f;
        float steering = mover != null ? mover.AppliedSteeringAngle : 0f;
        float pitchTarget = active ? Mathf.Clamp(-pedal, -1f, 1f) * maximumPitch + landingPulse * .7f : 0f;
        float rollTarget = active ? Mathf.Clamp(-steering / 30f, -1f, 1f) * maximumRoll : 0f;
        float blend = 1f - Mathf.Exp(-8f * Mathf.Max(0f, deltaTime));
        amplitude = Mathf.Lerp(amplitude, maximumBounce * strength * (grounded ? 1f : .35f), blend);
        phase = Mathf.Repeat(phase + deltaTime * Mathf.Lerp(2f, 4f, strength) * Mathf.PI * 2f, Mathf.PI * 2f);
        float offset = Mathf.Sin(phase) * amplitude + landingPulse * maximumBounce * .5f;
        for (int index = 0; index < visuals.Count; index++)
        {
            if (visuals[index] == null) continue;
            visuals[index].localPosition = basePositions[index] + Vector3.up * offset;
            visuals[index].localRotation = Quaternion.Slerp(visuals[index].localRotation,
                baseRotations[index] * Quaternion.Euler(pitchTarget, 0f, rollTarget), blend);
        }
    }

    private void OnDisable()
    {
        amplitude = 0f;
        landingPulse = 0f;
        wasGrounded = false;
        for (int index = 0; index < visuals.Count; index++)
            if (visuals[index] != null)
            {
                visuals[index].localPosition = basePositions[index];
                visuals[index].localRotation = baseRotations[index];
            }
    }
}
