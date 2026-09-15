using System;
using System.Collections.Generic;
using UnityEngine;

public class LapManager : MonoBehaviour
{
    [System.Serializable]
    public class CarTimeData
    {
        public string carName;
        public int lapCount;
        public float currentLapTime = 0f;
        public float bestLapTime = float.MaxValue;
        public float totalRaceTime = 0f;
        /// <summary>現在の周回を含む、コース上の連続的な進捗距離です。</summary>
        [System.NonSerialized] public float raceProgressDistance;
        public int nextCheckpointIndex = 0;
        public int lastCheckpointIndex = -1;
        public bool allCheckpointsPassed = false;
        public float offCourseTimer = 0f;
        public bool isOffCourse = false;

        [System.NonSerialized] public Rigidbody rb;
        [System.NonSerialized] public bool hasCrossedGoal = false;
        [System.NonSerialized] public bool isFinished = false;
        [System.NonSerialized] public bool hasValidRacePosition = false;
        [System.NonSerialized] public Transform respawnPoint;
        [System.NonSerialized] public Vector3 lastValidPosition;
        [System.NonSerialized] public Quaternion lastValidRotation = Quaternion.identity;
        [System.NonSerialized] public int respawnNextCheckpointIndex = 0;
        [System.NonSerialized] public int respawnLastCheckpointIndex = -1;
        [System.NonSerialized] public bool respawnAllCheckpointsPassed = false;
    }

    [Header("Course")]
    [SerializeField] private RaceCourse raceCourse;
    [SerializeField] private CheckpointSensor[] checkpoints = new CheckpointSensor[0];
    [SerializeField] private bool autoFindCheckpoints = true;
    [SerializeField] private bool allowLapWithoutCheckpoints = true;
    [SerializeField] private int goalLap = 3;

    [Header("Off Course")]
    [SerializeField] private bool respawnWhenOffCourse = true;
    [SerializeField] private float offCourseRespawnDelay = 2f;
    [Tooltip("地面を検出できない場合に使う従来のリスポーン高さ。")]
    [SerializeField] private float respawnHeightOffset = 0.5f;
    [Tooltip("リスポーン時に車体と地面の間へ残す隙間。")]
    [SerializeField, Min(0f)] private float respawnGroundClearance = 0.05f;
    [Tooltip("リスポーン地点の地面を探すため、一時的に車体を持ち上げる高さ。")]
    [SerializeField, Min(0.1f)] private float respawnGroundProbeHeight = 10f;
    [SerializeField] private Transform defaultRespawnPoint;
    [SerializeField] private bool resetVelocityOnRespawn = true;

    private readonly Dictionary<Rigidbody, CarTimeData> carDataMap = new Dictionary<Rigidbody, CarTimeData>();
    private bool raceActive;
    private bool hasProgressReference;
    private bool reverseProgressDirection;
    private float progressStartDistance;

    public int GoalLap => goalLap;
    public event Action<Rigidbody, RaceResultRecord> CarFinished;

    private void Awake()
    {
        if (raceCourse == null)
        {
            raceCourse = FindObjectOfType<RaceCourse>();
        }

        if (raceCourse != null)
        {
            raceCourse.RebuildCache();
        }

        RefreshCheckpoints();
        InitializeProgressReference();
    }

    private void Update()
    {
        if (!raceActive)
        {
            return;
        }

        float dt = Time.deltaTime;

        foreach (CarTimeData data in carDataMap.Values)
        {
            if (data.rb == null || data.isFinished)
            {
                continue;
            }

            data.currentLapTime += dt;
            UpdateRaceProgress(data);
            UpdateCourseState(data, dt);
        }
    }

    public void OnCarPassCheckpoint(Rigidbody rb, CheckpointSensor checkpoint)
    {
        if (!raceActive || rb == null || checkpoint == null)
        {
            return;
        }

        CarTimeData data = GetOrCreateCarData(rb);
        if (data.isFinished)
        {
            return;
        }

        int checkpointIndex = checkpoint.CheckpointIndex;
        int checkpointCount = GetCheckpointCount();

        if (data.allCheckpointsPassed)
        {
            return;
        }

        if (checkpointIndex == data.lastCheckpointIndex)
        {
            return;
        }

        if (checkpointIndex != data.nextCheckpointIndex)
        {
            Debug.Log($"{data.carName} invalid checkpoint {checkpointIndex}. Next: {data.nextCheckpointIndex}");
            return;
        }

        data.lastCheckpointIndex = checkpointIndex;
        data.allCheckpointsPassed = checkpointIndex == checkpointCount - 1;
        data.nextCheckpointIndex = data.allCheckpointsPassed ? 0 : checkpointIndex + 1;
        SetRespawnPoint(data, checkpoint.RespawnPoint);

        if (data.allCheckpointsPassed)
        {
            Debug.Log($"{data.carName} all checkpoints passed. Waiting for goal");
        }
        else
        {
            Debug.Log($"{data.carName} checkpoint {checkpointIndex} passed. Next: {data.nextCheckpointIndex}");
        }
    }

    public bool OnCarPassGoal(Rigidbody rb)
    {
        return OnCarPassGoal(rb, null);
    }

    public void RegisterCar(Rigidbody rb, Transform startTransform = null)
    {
        if (rb == null)
        {
            return;
        }

        CarTimeData data = GetOrCreateCarData(rb);
        data.hasCrossedGoal = true;
        data.lapCount = 0;
        data.currentLapTime = 0f;
        data.totalRaceTime = 0f;
        data.bestLapTime = float.MaxValue;
        data.isFinished = false;
        ResetCheckpointProgress(data);
        SetRespawnPoint(data, startTransform);
        if (!hasProgressReference && startTransform != null)
        {
            InitializeProgressReference(startTransform);
        }
        raceActive = true;
    }

    public bool OnCarPassGoal(Rigidbody rb, Transform goalTransform)
    {
        if (!raceActive || rb == null)
        {
            return false;
        }

        CarTimeData data = GetOrCreateCarData(rb);
        if (data.isFinished)
        {
            return false;
        }

        if (!data.hasCrossedGoal)
        {
            data.hasCrossedGoal = true;
            data.currentLapTime = 0f;
            data.totalRaceTime = 0f;
            ResetCheckpointProgress(data);
            SetRespawnPoint(data, goalTransform);
            Debug.Log($"{data.carName} joined race");
            return false;
        }

        if (!CanCompleteLap(data))
        {
            Debug.Log($"{data.carName} goal ignored. Checkpoint {data.nextCheckpointIndex}/{GetCheckpointCount()}");
            return false;
        }

        data.lapCount++;
        if (data.currentLapTime < data.bestLapTime)
        {
            data.bestLapTime = data.currentLapTime;
        }

        float completedLapTime = data.currentLapTime;
        data.totalRaceTime += completedLapTime;

        Debug.Log($"{data.carName} : Lap {data.lapCount} | Time: {data.currentLapTime:F2}s | Best: {data.bestLapTime:F2}s");

        if (goalLap > 0 && data.lapCount >= goalLap)
        {
            data.isFinished = true;
            CarFinished?.Invoke(rb, CreateResultRecord(data, completedLapTime));
        }

        data.currentLapTime = 0f;
        ResetCheckpointProgress(data);
        SetRespawnPoint(data, goalTransform);
        return true;
    }

    public CarTimeData GetCarData(Rigidbody rb)
    {
        if (rb == null)
        {
            return null;
        }

        carDataMap.TryGetValue(rb, out CarTimeData data);
        return data;
    }

    /// <summary>
    /// 車のワールド座標から計算した最新のレース進捗距離を返します。
    /// 順位表示側が Update の実行順に依存しないよう、要求時にも再計算します。
    /// </summary>
    public float GetRaceProgressDistance(Rigidbody rb)
    {
        CarTimeData data = GetCarData(rb);
        if (data == null || data.rb == null || raceCourse == null)
        {
            return data != null ? data.raceProgressDistance : 0f;
        }

        float lapLength = raceCourse.TotalLength;
        if (lapLength <= Mathf.Epsilon)
        {
            return data.raceProgressDistance;
        }

        data.raceProgressDistance = data.lapCount * lapLength + GetLapProgressDistance(data.rb.position, lapLength);
        return data.raceProgressDistance;
    }

    public void PauseRace()
    {
        raceActive = false;
    }

    public void ResumeRace()
    {
        raceActive = HasActiveCar();
    }

    public void UnregisterCar(Rigidbody rb)
    {
        if (rb != null)
        {
            carDataMap.Remove(rb);
        }
    }

    public void ResetRace()
    {
        raceActive = false;
        carDataMap.Clear();

        foreach (GoalSensor goalSensor in FindObjectsByType<GoalSensor>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            goalSensor.ResetCounter();
        }
    }

    public void RefreshCheckpoints()
    {
        if (autoFindCheckpoints)
        {
            checkpoints = FindObjectsOfType<CheckpointSensor>();
        }

        if (checkpoints == null)
        {
            checkpoints = new CheckpointSensor[0];
            return;
        }

        System.Array.Sort(checkpoints, (a, b) =>
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            return a.CheckpointIndex.CompareTo(b.CheckpointIndex);
        });
    }

    private CarTimeData GetOrCreateCarData(Rigidbody rb)
    {
        if (carDataMap.TryGetValue(rb, out CarTimeData data))
        {
            return data;
        }

        data = new CarTimeData
        {
            rb = rb,
            carName = rb.name,
            lastValidPosition = rb.position,
            lastValidRotation = rb.rotation
        };

        carDataMap[rb] = data;
        return data;
    }

    private void UpdateCourseState(CarTimeData data, float dt)
    {
        if (raceCourse == null || !respawnWhenOffCourse)
        {
            return;
        }

        Vector2 carPoint = new Vector2(data.rb.position.x, data.rb.position.z);
        if (raceCourse.IsPointInsideCourse(carPoint))
        {
            data.isOffCourse = false;
            data.offCourseTimer = 0f;
            data.hasValidRacePosition = true;
            data.lastValidPosition = data.rb.position;
            data.lastValidRotation = data.rb.rotation;
            return;
        }

        if (!data.isOffCourse)
        {
            Debug.Log($"{data.carName} left course");
        }

        data.isOffCourse = true;
        data.offCourseTimer += dt;

        if (data.offCourseTimer >= Mathf.Max(0.1f, offCourseRespawnDelay))
        {
            RespawnCar(data);
        }
    }

    private void UpdateRaceProgress(CarTimeData data)
    {
        if (data == null || data.rb == null || raceCourse == null)
        {
            return;
        }

        float lapLength = raceCourse.TotalLength;
        if (lapLength <= Mathf.Epsilon)
        {
            return;
        }

        data.raceProgressDistance = data.lapCount * lapLength + GetLapProgressDistance(data.rb.position, lapLength);
    }

    private void InitializeProgressReference()
    {
        Transform startTransform = checkpoints != null && checkpoints.Length > 0 && checkpoints[0] != null
            ? checkpoints[0].transform
            : null;
        InitializeProgressReference(startTransform);
    }

    private void InitializeProgressReference(Transform startTransform)
    {
        hasProgressReference = false;
        reverseProgressDirection = false;
        progressStartDistance = 0f;

        if (raceCourse == null || startTransform == null)
        {
            return;
        }

        float lapLength = raceCourse.TotalLength;
        if (lapLength <= Mathf.Epsilon)
        {
            return;
        }

        if (raceCourse.TryGetNearestCenterLineDirection(startTransform.position, out Vector3 courseDirection))
        {
            Vector3 startForward = Vector3.ProjectOnPlane(startTransform.forward, Vector3.up).normalized;
            reverseProgressDirection = startForward.sqrMagnitude > Mathf.Epsilon &&
                                       Vector3.Dot(startForward, courseDirection) < 0f;
        }

        float rawStartDistance = raceCourse.GetProgressDistance(startTransform.position);
        progressStartDistance = ToDirectedProgress(rawStartDistance, lapLength);
        hasProgressReference = true;
    }

    private float GetLapProgressDistance(Vector3 worldPosition, float lapLength)
    {
        float directedDistance = ToDirectedProgress(raceCourse.GetProgressDistance(worldPosition), lapLength);
        if (!hasProgressReference)
        {
            return directedDistance;
        }

        return Mathf.Repeat(directedDistance - progressStartDistance, lapLength);
    }

    private float ToDirectedProgress(float rawDistance, float lapLength)
    {
        return reverseProgressDirection
            ? Mathf.Repeat(lapLength - rawDistance, lapLength)
            : Mathf.Repeat(rawDistance, lapLength);
    }

    private bool CanCompleteLap(CarTimeData data)
    {
        int checkpointCount = GetCheckpointCount();
        if (checkpointCount == 0)
        {
            return allowLapWithoutCheckpoints;
        }

        return data.allCheckpointsPassed;
    }

    private int GetCheckpointCount()
    {
        return checkpoints == null ? 0 : checkpoints.Length;
    }

    private void ResetCheckpointProgress(CarTimeData data)
    {
        data.nextCheckpointIndex = 0;
        data.lastCheckpointIndex = -1;
        data.allCheckpointsPassed = false;
    }

    private void SetRespawnPoint(CarTimeData data, Transform respawnTransform)
    {
        if (respawnTransform == null)
        {
            return;
        }

        data.respawnPoint = respawnTransform;
        data.lastValidPosition = respawnTransform.position;
        data.lastValidRotation = respawnTransform.rotation;
        data.hasValidRacePosition = true;
        data.respawnNextCheckpointIndex = data.nextCheckpointIndex;
        data.respawnLastCheckpointIndex = data.lastCheckpointIndex;
        data.respawnAllCheckpointsPassed = data.allCheckpointsPassed;
    }

    private void RestoreCheckpointProgressFromRespawnPoint(CarTimeData data)
    {
        data.nextCheckpointIndex = data.respawnNextCheckpointIndex;
        data.lastCheckpointIndex = data.respawnLastCheckpointIndex;
        data.allCheckpointsPassed = data.respawnAllCheckpointsPassed;
    }

    public RaceResultRecord CreateResultRecord(CarTimeData data, float finalLapTime)
    {
        return new RaceResultRecord
        {
            carName = data.carName,
            completedLaps = data.lapCount,
            goalLap = goalLap,
            totalRaceTime = data.totalRaceTime,
            finalLapTime = finalLapTime,
            bestLapTime = data.bestLapTime
        };
    }

    private bool HasActiveCar()
    {
        foreach (CarTimeData data in carDataMap.Values)
        {
            if (data.rb != null && !data.isFinished)
            {
                return true;
            }
        }

        return false;
    }

    private void RespawnCar(CarTimeData data)
    {
        Vector3 targetPosition;
        Quaternion targetRotation;

        if (data.respawnPoint != null)
        {
            targetPosition = data.respawnPoint.position;
            targetRotation = data.respawnPoint.rotation;
        }
        else if (data.hasValidRacePosition)
        {
            targetPosition = data.lastValidPosition;
            targetRotation = data.lastValidRotation;
        }
        else if (defaultRespawnPoint != null)
        {
            targetPosition = defaultRespawnPoint.position;
            targetRotation = defaultRespawnPoint.rotation;
        }
        else if (raceCourse != null)
        {
            targetPosition = raceCourse.GetNearestPointOnCenterLineWorld(data.rb.position);
            targetRotation = data.rb.rotation;
        }
        else
        {
            targetPosition = data.rb.position;
            targetRotation = data.rb.rotation;
        }

        targetPosition = FindPositionJustAboveGround(data.rb, targetPosition, targetRotation);
        data.rb.position = targetPosition;
        data.rb.rotation = targetRotation;

        if (resetVelocityOnRespawn)
        {
            data.rb.linearVelocity = Vector3.zero;
            data.rb.angularVelocity = Vector3.zero;
        }

        data.rb.GetComponent<DebugMover>()?.SuppressInputAfterRespawn();

        data.isOffCourse = false;
        data.offCourseTimer = 0f;
        data.hasValidRacePosition = true;
        data.lastValidPosition = targetPosition;
        data.lastValidRotation = targetRotation;
        RestoreCheckpointProgressFromRespawnPoint(data);

        Debug.Log($"{data.carName} respawned");
    }

    private Vector3 FindPositionJustAboveGround(
        Rigidbody rb,
        Vector3 respawnPoint,
        Quaternion respawnRotation)
    {
        Vector3 fallbackPosition = respawnPoint + Vector3.up * respawnHeightOffset;
        Collider[] carColliders = rb.GetComponentsInChildren<Collider>();
        if (carColliders.Length == 0)
        {
            return fallbackPosition;
        }

        float probeHeight = Mathf.Max(0.1f, respawnGroundProbeHeight);
        float groundClearance = Mathf.Max(0f, respawnGroundClearance);
        Vector3 probePosition = respawnPoint + Vector3.up * probeHeight;
        rb.position = probePosition;
        rb.rotation = respawnRotation;
        Physics.SyncTransforms();

        bool foundGround = false;
        float requiredVerticalAdjustment = float.NegativeInfinity;

        foreach (Collider carCollider in carColliders)
        {
            if (carCollider == null || !carCollider.enabled || carCollider.isTrigger ||
                carCollider.attachedRigidbody != rb)
            {
                continue;
            }

            Bounds bounds = carCollider.bounds;
            Vector3 rayOrigin = new Vector3(
                bounds.center.x,
                bounds.max.y + groundClearance,
                bounds.center.z);
            float rayDistance = probeHeight * 2f + bounds.size.y + Mathf.Abs(respawnHeightOffset);

            if (!TryFindGroundBelow(rayOrigin, rayDistance, out RaycastHit groundHit))
            {
                continue;
            }

            float adjustment = groundHit.point.y + groundClearance - bounds.min.y;
            requiredVerticalAdjustment = Mathf.Max(requiredVerticalAdjustment, adjustment);
            foundGround = true;
        }

        if (!foundGround)
        {
            return fallbackPosition;
        }

        probePosition.y += requiredVerticalAdjustment;
        return probePosition;
    }

    private static bool TryFindGroundBelow(
        Vector3 origin,
        float distance,
        out RaycastHit groundHit)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);

        groundHit = default;
        float nearestDistance = float.MaxValue;
        bool foundGround = false;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.attachedRigidbody != null ||
                hit.normal.y < 0.2f || hit.distance >= nearestDistance)
            {
                continue;
            }

            groundHit = hit;
            nearestDistance = hit.distance;
            foundGround = true;
        }

        return foundGround;
    }
}
