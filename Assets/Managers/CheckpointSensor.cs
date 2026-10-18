using UnityEngine;

/// <summary>Legacy grid anchor. Checkpoints no longer participate in lap counting or recovery.</summary>
public class CheckpointSensor : MonoBehaviour
{
    [SerializeField] private int checkpointIndex;
    public int CheckpointIndex => checkpointIndex;
    public Transform RespawnPoint => transform;

    private void Awake()
    {
        // Retain the transform for existing starting grids, but remove the checkpoint trigger.
        Collider sensor = GetComponent<Collider>();
        if (sensor != null && sensor.isTrigger) sensor.enabled = false;
    }
}
