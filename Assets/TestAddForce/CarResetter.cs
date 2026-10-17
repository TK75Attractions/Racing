using UnityEngine;

public class CarResetter : MonoBehaviour
{
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Rigidbody rb;
    private DebugMover debugMover;
    private IDriveInputSource inputSource;
    private RaceCourse course;
    private LapManager laps;
    private TireForce[] tires;
    private RaceCourse.CourseSample safeSample;
    private int safeLap;
    private bool hasSafeSample, resetHeld, manualPending;
    private float unsafeSeconds, overturnedSeconds, stuckSeconds;
    private readonly Collider[] occupants = new Collider[32];
    private static readonly float[] RecoverySides = {0f,-1f,1f};
    public int RecoveryCount { get; private set; }
    public int AutomaticRecoveryCount { get; private set; }
    public bool IsRecoveryPending { get; private set; }
    public string RecoveryReason { get; private set; }
    public float LastRecoveryTime { get; private set; } = -100f;

    public void SetCourse(RaceCourse raceCourse, LapManager lapManager)
    {
        course = raceCourse;
        laps = lapManager;
        RememberSafeRoad(true);
    }

    public void SetInputSource(IDriveInputSource source)
    {
        inputSource = source;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        debugMover = GetComponent<DebugMover>();
        tires = GetComponentsInChildren<TireForce>();
        initialPosition = transform.position; initialRotation = transform.rotation;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //初期位置と回転を記録
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        if (course == null) course = FindFirstObjectByType<RaceCourse>();
        if (laps == null) laps = FindFirstObjectByType<LapManager>();
        RememberSafeRoad(true);
    }

    void Update()
    {
        bool pressed = inputSource != null && inputSource.CurrentState.resetPressed;
        if (pressed && !resetHeld) manualPending = true;
        resetHeld = pressed;
        if (debugMover != null && debugMover.IsInputSuppressed) return;
        if (manualPending && TryResetCar()) manualPending = false;
    }

    private void FixedUpdate()
    {
        if (rb == null || rb.isKinematic || debugMover == null || !debugMover.isActiveAndEnabled ||
            Gmanager.Control == null || Gmanager.Control.state != Gmanager.State.Game || course == null) return;
        if (inputSource is PlayerDriveInputGate gate && (gate.IsBlocked || !gate.IsConnected || gate.NeedsPedalRelease))
        { unsafeSeconds = overturnedSeconds = stuckSeconds = 0f; return; }
        RememberSafeRoad(false);
        if (!course.TryGetNearestRoadSample(rb.position, out RaceCourse.CourseSample here)) return;
        int grounded = 0; foreach (TireForce tire in tires) if (tire.IsGrounded) grounded++;
        bool outside = !course.IsPointInsideCourse(rb.position) || rb.position.y < here.position.y - 4f;
        float dt = Time.fixedDeltaTime;
        unsafeSeconds = outside ? unsafeSeconds + dt : 0f;
        overturnedSeconds = Vector3.Dot(transform.up, here.up) < .2f && grounded < 2 ? overturnedSeconds + dt : 0f;
        bool pushing = inputSource != null && Mathf.Abs(inputSource.CurrentState.pedal) > .4f;
        stuckSeconds = pushing && rb.linearVelocity.magnitude < .5f && grounded > 0 ? stuckSeconds + dt : 0f;
        if (unsafeSeconds < 1.8f && overturnedSeconds < 2.5f && stuckSeconds < 4f) return;
        RecoveryReason = outside ? "off-course" : overturnedSeconds >= 2.5f ? "overturned" : "stuck";
        IsRecoveryPending = true;
        if (TryResetCar(true)) AutomaticRecoveryCount++;
    }

    private void RememberSafeRoad(bool initial)
    {
        if (course == null || rb == null || !course.TryGetNearestRoadSample(rb.position, out RaceCourse.CourseSample sample)) return;
        int lap = laps?.GetCarData(rb)?.lapCount ?? 0;
        if (hasSafeSample && safeLap != lap) hasSafeSample = false;
        if (!initial)
        {
            int grounded=0; foreach (TireForce tire in tires) if (tire.IsGrounded) grounded++;
            if (grounded < 2 || !course.IsPointInsideCourse(rb.position) || Mathf.Abs(rb.position.y-sample.position.y)>3f ||
                Vector3.Dot(transform.up,sample.up)<.7f) return;
        }
        if (!course.HasRoadAtProgress(course.GetProgressDistance(sample.position))) return;
        safeSample = sample; safeLap = lap; hasSafeSample = true;
    }

    public void ResetCar() => TryResetCar();

    public bool TryResetCar(bool preferSafePoint = false)
    {
        RaceCourse.CourseSample sample;
        int lap = laps?.GetCarData(rb)?.lapCount ?? 0;
        bool found = preferSafePoint && hasSafeSample && safeLap == lap;
        sample = safeSample;
        if (!found && course != null) found = course.TryGetNearestRoadSample(transform.position, out sample);
        if (!found) { if (course != null) return false; ResetToStart(); return true; }
        Vector3 forward = laps != null && laps.IsProgressReversed ? -sample.forward : sample.forward;
        Quaternion rotation = Quaternion.LookRotation(forward, sample.up);
        float clearance = GetGroundClearance(rotation, sample.up);
        float lane = Mathf.Max(0f, Mathf.Min(6f,sample.width*.5f-2f));
        foreach (float side in RecoverySides)
        {
            float offset = side * lane;
            Vector3 position = sample.position + sample.right * offset + sample.up * clearance;
            int count = Physics.OverlapBoxNonAlloc(position + sample.up*.4f, new Vector3(1.45f,.65f,2.85f),
                occupants, rotation, ~0, QueryTriggerInteraction.Ignore);
            bool occupied = count == occupants.Length;
            for (int i=0;i<count;i++)
                if (occupants[i].attachedRigidbody != null && occupants[i].attachedRigidbody != rb) occupied=true;
            if (occupied) continue;
            ApplyReset(position,rotation); Physics.SyncTransforms(); laps?.NotifyManualRespawn(rb);
            RecoveryCount++; LastRecoveryTime=Time.time;
            unsafeSeconds=overturnedSeconds=stuckSeconds=0; IsRecoveryPending=false;
            return true;
        }
        IsRecoveryPending=true;
        return false;
    }

    public void ResetToStart() => ApplyReset(initialPosition, initialRotation);

    private void ApplyReset(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
        if (rb != null)
        {
            // Rigidbodyを直接リセットし、次の物理更新へ古い回転を持ち越さない。
            rb.position = position;
            rb.rotation = rotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        debugMover?.SuppressInputAfterRespawn();
        GetComponent<CarItemEffects>()?.GrantRecoveryProtection(2f);
        if (inputSource is PlayerDriveInputGate gate) gate.RequirePedalRelease();

        Debug.Log($"{name} Reset");
    }

    private float GetGroundClearance(Quaternion rotation, Vector3 normal)
    {
        // Measure the support of the actual solid shapes in the new pose, including on slopes.
        Quaternion delta = rotation * Quaternion.Inverse(transform.rotation);
        float bottom = float.PositiveInfinity;
        foreach (Collider collider in GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody != rb) continue;
            if (collider is SphereCollider sphere)
            {
                Vector3 scale = sphere.transform.lossyScale;
                float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                Vector3 offset = delta * (sphere.transform.TransformPoint(sphere.center) - transform.position);
                bottom = Mathf.Min(bottom, Vector3.Dot(offset, normal) - radius);
            }
            else if (collider is BoxCollider box)
            {
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = box.center + Vector3.Scale(box.size * .5f, new Vector3(x, y, z));
                    Vector3 offset = delta * (box.transform.TransformPoint(corner) - transform.position);
                    bottom = Mathf.Min(bottom, Vector3.Dot(offset, normal));
                }
            }
        }
        return float.IsPositiveInfinity(bottom) ? .7f : Mathf.Max(.05f, .04f - bottom);
    }
}
