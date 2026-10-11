using System.Collections.Generic;
using UnityEngine;

/// <summary>タイヤの接地やカメラへ伝わらない、描画用車体だけの小さな上下動。</summary>
[DisallowMultipleComponent]
public sealed class CarBodyMotion : MonoBehaviour
{
    [SerializeField, Min(0f)] private float maximumBounce = .018f;
    private readonly List<Transform> visuals = new List<Transform>();
    private readonly List<Vector3> basePositions = new List<Vector3>();
    private Rigidbody body;
    private DebugMover mover;
    private TireForce[] tires;
    private float phase;
    private float amplitude;

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
    }

    private void LateUpdate() => Tick(Time.deltaTime);

    public void Tick(float deltaTime)
    {
        float speed = body != null ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude : 0f;
        bool grounded = false;
        if (mover != null)
            foreach (TireForce tire in tires)
                grounded |= tire.IsGrounded;
        bool active = mover != null && mover.isActiveAndEnabled && !mover.IsInputSuppressed &&
            Gmanager.Control != null && Gmanager.Control.IsDrivingEnabled && grounded;
        float strength = active ? Mathf.InverseLerp(3f, 30f, speed) : 0f;
        amplitude = Mathf.MoveTowards(amplitude, maximumBounce * strength, maximumBounce * 5f * deltaTime);
        phase = Mathf.Repeat(phase + deltaTime * Mathf.Lerp(2f, 4f, strength) * Mathf.PI * 2f, Mathf.PI * 2f);
        float offset = Mathf.Sin(phase) * amplitude;
        for (int index = 0; index < visuals.Count; index++)
            if (visuals[index] != null) visuals[index].localPosition = basePositions[index] + Vector3.up * offset;
    }

    private void OnDisable()
    {
        amplitude = 0f;
        for (int index = 0; index < visuals.Count; index++)
            if (visuals[index] != null) visuals[index].localPosition = basePositions[index];
    }
}
