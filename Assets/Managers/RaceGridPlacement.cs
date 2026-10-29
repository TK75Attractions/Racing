using UnityEngine;

/// <summary>Seat the starting grid on solid road before a countdown freezes the cars.</summary>
public static class RaceGridPlacement
{
    public static bool Ground(GameObject car)
    {
        Rigidbody body = car.GetComponent<Rigidbody>();
        if (body == null) return false;
        Physics.SyncTransforms();
        float correction = float.NegativeInfinity;
        int supported = 0;
        foreach (TireForce tire in car.GetComponentsInChildren<TireForce>())
        {
            SphereCollider wheel = tire.GetComponent<SphereCollider>();
            if (wheel == null || !wheel.enabled || wheel.isTrigger) continue;
            Vector3 center = wheel.transform.TransformPoint(wheel.center);
            Vector3 scale = wheel.transform.lossyScale;
            float radius = wheel.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            float distance = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(center + Vector3.up * 5f, Vector3.down, 15f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // Another parked car is never the grid's supporting road.
                if (hit.rigidbody != null || hit.normal.y < .5f) continue;
                distance = Mathf.Min(distance, hit.distance);
            }
            if (float.IsPositiveInfinity(distance)) continue;
            correction = Mathf.Max(correction, 5f - distance + radius + .02f);
            supported++;
        }
        if (supported < 3) return false;
        Vector3 position = body.position + Vector3.up * correction;
        body.position = position;
        car.transform.position = position;
        Physics.SyncTransforms();
        return true;
    }
}
