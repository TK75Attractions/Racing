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
        public float totalRaceTime = 0f;
        public int nextCheckpointIndex = 0;
        public int lastCheckpointIndex = -1;
        public bool allCheckpointsPassed = false;
        public float offCourseTimer = 0f;
        public bool isOffCourse = false;

        [System.NonSerialized] public Rigidbody rb;
        [System.NonSerialized] public bool hasCrossedGoal = false;
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
    [SerializeField] private float respawnHeightOffset = 0.5f;
    [SerializeField] private Transform defaultRespawnPoint;
    [SerializeField] private bool resetVelocityOnRespawn = true;

    private readonly Dictionary<Rigidbody, CarTimeData> carDataMap = new Dictionary<Rigidbody, CarTimeData>();
    private bool raceActive;

    public int GoalLap => goalLap;

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
        if (!raceActive)
        {
            return;
        }

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
        if (!raceActive || rb == null || checkpoint == null)
        {
            return;
        }

        CarTimeData data = GetOrCreateCarData(rb);
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
        ResetCheckpointProgress(data);
        SetRespawnPoint(data, startTransform);
        raceActive = true;
    }

    public bool OnCarPassGoal(Rigidbody rb, Transform goalTransform)
    {
        if (!raceActive || rb == null)
        {
            return false;
        }

        CarTimeData data = GetOrCreateCarData(rb);

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
            Gmanager.Control?.ShowResult(CreateResultRecord(data, completedLapTime));
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

    public void PauseRace()
    {
        raceActive = false;
    }

    public void ResumeRace()
    {
        raceActive = carDataMap.Count > 0;
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

    private RaceResultRecord CreateResultRecord(CarTimeData data, float finalLapTime)
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
        RestoreCheckpointProgressFromRespawnPoint(data);

        Debug.Log($"{data.carName} respawned");
    }
}
