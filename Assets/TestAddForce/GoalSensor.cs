using System.Collections.Generic;
using UnityEngine;

public class GoalSensor : MonoBehaviour
{
    private const string TargetTag = "Player";
    private const string TargetLayerName = "CarBody";

    [SerializeField] private LapManager lapManager;

    public int lapCount = 0;

    private int targetLayer = -1;
    private readonly Dictionary<Rigidbody, HashSet<Collider>> overlaps = new Dictionary<Rigidbody, HashSet<Collider>>();
    private readonly HashSet<Rigidbody> respawningInside = new HashSet<Rigidbody>();

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

        if (!overlaps.TryGetValue(rb, out HashSet<Collider> colliders))
            overlaps[rb] = colliders = new HashSet<Collider>();
        colliders.RemoveWhere(collider => collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy);
        bool entering = colliders.Count == 0;
        if (!colliders.Add(other) || !entering) return;
        if (respawningInside.Contains(rb)) return;

        if (lapManager.OnCarPassGoal(rb, transform))
        {
            lapCount++;
            Debug.Log($"Lap {lapCount} passed");
        }
    }

    public void ResetCounter()
    {
        lapCount = 0;
        overlaps.Clear();
        respawningInside.Clear();
    }

    public void IgnoreRespawnInsideGoal(Rigidbody body)
    {
        Collider sensor = GetComponent<Collider>();
        if (sensor == null || body == null) return;
        foreach (Collider collider in body.GetComponentsInChildren<Collider>())
            if (collider.enabled && !collider.isTrigger && collider.attachedRigidbody == body &&
                Physics.ComputePenetration(sensor, sensor.transform.position, sensor.transform.rotation,
                    collider, collider.transform.position, collider.transform.rotation, out _, out _))
            { respawningInside.Add(body); return; }
    }

    public void OnTriggerExit(Collider other)
    {
        Rigidbody body = other.attachedRigidbody;
        if (body == null || !overlaps.TryGetValue(body, out HashSet<Collider> colliders)) return;
        colliders.Remove(other);
        if (colliders.Count == 0) { overlaps.Remove(body); respawningInside.Remove(body); }
    }

    private void OnDisable() { overlaps.Clear(); respawningInside.Clear(); }

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
