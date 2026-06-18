using UnityEngine;
using System.Collections.Generic;

public class LapManager : MonoBehaviour
{
    [System.Serializable]
    public class CarTimeData
    {
        public string carName;
        public int lapCount;
        public float currentLapTime = 0f;
        public float bestLapTime = float.MaxValue;
        public int nextCheckpointIndex = 0;
        public int lastCheckpointIndex = -1;
        public float offCourseTimer = 0f;
        public bool isOffCourse = false;

        [System.NonSerialized] public Rigidbody rb;
        [System.NonSerialized] public bool hasCrossedGoal = false;
        [System.NonSerialized] public bool hasValidRacePosition = false;
        [System.NonSerialized] public Transform respawnPoint;
        [System.NonSerialized] public Vector3 lastValidPosition;
        [System.NonSerialized] public Quaternion lastValidRotation = Quaternion.identity;
    }

    [Header("Course")]
    [SerializeField] private RaceCourse raceCourse;
    [SerializeField] private CheckpointSensor[] checkpoints = new CheckpointSensor[0];
    [SerializeField] private bool autoFindCheckpoints = true;
    [SerializeField] private bool allowLapWithoutCheckpoints = true;

    [Header("Off Course")]
    [SerializeField] private bool respawnWhenOffCourse = true;
    [SerializeField] private float offCourseRespawnDelay = 2f;
    [SerializeField] private float respawnHeightOffset = 0.5f;
    [SerializeField] private Transform defaultRespawnPoint;
    [SerializeField] private bool resetVelocityOnRespawn = true;

    private readonly Dictionary<Rigidbody, CarTimeData> carDataMap = new Dictionary<Rigidbody, CarTimeData>();

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
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        foreach (CarTimeData data in carDataMap.Values)
        {
            if (data.rb == null)
            {
                continue;
            }

            data.currentLapTime += dt;
            UpdateCourseState(data, dt);
        }
    }

    public void OnCarPassCheckpoint(Rigidbody rb, CheckpointSensor checkpoint)
    {
        if (rb == null || checkpoint == null)
        {
            return;
        }

        CarTimeData data = GetOrCreateCarData(rb);
        int checkpointIndex = checkpoint.CheckpointIndex;
        int checkpointCount = GetCheckpointCount();

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
        data.nextCheckpointIndex = Mathf.Min(checkpointIndex + 1, checkpointCount);
        SetRespawnPoint(data, checkpoint.RespawnPoint);

        Debug.Log($"{data.carName} checkpoint {checkpointIndex} passed");
    }

    public void OnCarPassGoal(Rigidbody rb)
    {
        OnCarPassGoal(rb, null);
    }

    public void OnCarPassGoal(Rigidbody rb, Transform goalTransform)
    {
        if (rb == null)
        {
            return;
        }

        CarTimeData data = GetOrCreateCarData(rb);

        if (!data.hasCrossedGoal)
        {
            data.hasCrossedGoal = true;
            data.currentLapTime = 0f;
            ResetCheckpointProgress(data);
            SetRespawnPoint(data, goalTransform);
            Debug.Log($"{data.carName} joined race");
            return;
        }

        if (!CanCompleteLap(data))
        {
            Debug.Log($"{data.carName} goal ignored. Checkpoint {data.nextCheckpointIndex}/{GetCheckpointCount()}");
            return;
        }

        data.lapCount++;
        if (data.currentLapTime < data.bestLapTime)
        {
            data.bestLapTime = data.currentLapTime;
        }

        Debug.Log($"{data.carName} : Lap {data.lapCount} | Time: {data.currentLapTime:F2}s | Best: {data.bestLapTime:F2}s");

        data.currentLapTime = 0f;
        ResetCheckpointProgress(data);
        SetRespawnPoint(data, goalTransform);
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

    private bool CanCompleteLap(CarTimeData data)
    {
        int checkpointCount = GetCheckpointCount();
        if (checkpointCount == 0)
        {
            return allowLapWithoutCheckpoints;
        }

        return data.nextCheckpointIndex >= checkpointCount;
    }

    private int GetCheckpointCount()
    {
        return checkpoints == null ? 0 : checkpoints.Length;
    }

    private void ResetCheckpointProgress(CarTimeData data)
    {
        data.nextCheckpointIndex = 0;
        data.lastCheckpointIndex = -1;
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

        targetPosition.y += respawnHeightOffset;
        data.rb.position = targetPosition;
        data.rb.rotation = targetRotation;

        if (resetVelocityOnRespawn)
        {
            data.rb.linearVelocity = Vector3.zero;
            data.rb.angularVelocity = Vector3.zero;
        }

        data.isOffCourse = false;
        data.offCourseTimer = 0f;
        data.hasValidRacePosition = true;
        data.lastValidPosition = targetPosition;
        data.lastValidRotation = targetRotation;

        Debug.Log($"{data.carName} respawned");
    }
}
