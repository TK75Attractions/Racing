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

    public void SetCourse(RaceCourse raceCourse, LapManager lapManager)
    {
        course = raceCourse;
        laps = lapManager;
    }

    public void SetInputSource(IDriveInputSource source)
    {
        inputSource = source;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        debugMover = GetComponent<DebugMover>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //初期位置と回転を記録
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        if (course == null) course = FindFirstObjectByType<RaceCourse>();
        if (laps == null) laps = FindFirstObjectByType<LapManager>();
    }

    // Update is called once per frame
    void Update()
    {
        if (debugMover != null && debugMover.IsInputSuppressed)
        {
            return;
        }

        if (inputSource != null && inputSource.CurrentState.resetPressed)
        {
            ResetCar();
        }
    }

    public void ResetCar()
    {
        if (course != null && course.TryGetNearestRoadSample(transform.position, out RaceCourse.CourseSample sample))
        {
            Vector3 forward = laps != null && laps.IsProgressReversed ? -sample.forward : sample.forward;
            Quaternion rotation = Quaternion.LookRotation(forward, sample.up);
            ApplyReset(sample.position + sample.up * GetGroundClearance(rotation, sample.up), rotation);
            laps?.NotifyManualRespawn(rb);
            return;
        }
        ResetToStart();
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
