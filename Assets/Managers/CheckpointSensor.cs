using UnityEngine;

[RequireComponent(typeof(Collider))]
public class CheckpointSensor : MonoBehaviour
{
    private const string TargetTag = "Player";
    private const string TargetLayerName = "CarBody";

    [SerializeField] private LapManager lapManager;
    [SerializeField] private int checkpointIndex;

    private int targetLayer = -1;

    public int CheckpointIndex => checkpointIndex;
    public Transform RespawnPoint => transform;

    private void Reset()
    {
        Collider checkpointCollider = GetComponent<Collider>();
        if (checkpointCollider != null)
        {
            checkpointCollider.isTrigger = true;
        }

        checkpointIndex = transform.GetSiblingIndex();
    }

    private void Awake()
    {
        targetLayer = LayerMask.NameToLayer(TargetLayerName);

        if (lapManager == null)
        {
            lapManager = FindObjectOfType<LapManager>();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (lapManager == null || !TryGetTargetRigidbody(other, out Rigidbody rb))
        {
            return;
        }

        lapManager.OnCarPassCheckpoint(rb, this);
        Debug.Log($"Car passed checkpoint {checkpointIndex}");
    }

    private bool TryGetTargetRigidbody(Collider other, out Rigidbody rb)
    {
        rb = other.attachedRigidbody;
        if (rb == null)
        {
            return false;
        }

        GameObject target = rb.gameObject;
        return target.CompareTag(TargetTag) && target.layer == targetLayer;
    }
}
