using UnityEngine;

public class GoalSensor : MonoBehaviour
{
    private const string TargetTag = "Player";
    private const string TargetLayerName = "CarBody";

    [SerializeField] private LapManager lapManager;

    public int lapCount = 0;

    private int targetLayer = -1;

    private void Awake()
    {
        targetLayer = LayerMask.NameToLayer(TargetLayerName);

        if (lapManager == null)
        {
            lapManager = GetComponent<LapManager>();
        }

        if (lapManager == null)
        {
            lapManager = FindObjectOfType<LapManager>();
        }
    }

    public void OnTriggerEnter(Collider other)
    {
        if (lapManager == null || !TryGetTargetRigidbody(other, out Rigidbody rb))
        {
            return;
        }

        if (lapManager.OnCarPassGoal(rb, transform))
        {
            lapCount++;
            Debug.Log($"Lap {lapCount} passed");
        }
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
