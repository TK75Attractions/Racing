using UnityEngine;

/// <summary>Marks the configured tube interior as drivable, including its steep sides.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshCollider))]
public sealed class TubeDriveSurface : MonoBehaviour
{
    private void OnEnable() => TunnelAcoustics.Register(GetComponent<MeshCollider>());
    private void OnDisable() => TunnelAcoustics.Unregister(GetComponent<MeshCollider>());

    public static bool Contains(Collider collider) => collider != null &&
        collider.TryGetComponent<TubeDriveSurface>(out var surface) && surface.isActiveAndEnabled;

    /// <summary>対向する壁を実際に横切る回数で内部を判定し、曲がったチューブの外側を除きます。</summary>
    public static bool IsPointInside(MeshCollider collider, Vector3 position)
    {
        if (!Contains(collider) || !collider.enabled || collider.isTrigger ||
            !collider.gameObject.activeInHierarchy || !collider.bounds.Contains(position)) return false;
        Bounds bounds = collider.bounds;
        return (HasOddCrossings(collider, position, Vector3.up, bounds.max.y - position.y) &&
                HasOddCrossings(collider, position, Vector3.down, position.y - bounds.min.y)) ||
            (HasOddCrossings(collider, position, Vector3.left, position.x - bounds.min.x) &&
                HasOddCrossings(collider, position, Vector3.right, bounds.max.x - position.x)) ||
            (HasOddCrossings(collider, position, Vector3.forward, bounds.max.z - position.z) &&
                HasOddCrossings(collider, position, Vector3.back, position.z - bounds.min.z));
    }

    private static bool HasOddCrossings(MeshCollider collider, Vector3 position, Vector3 direction, float distance)
    {
        // 元のチューブColliderは両面三角形。微小距離を進め、同じ壁の裏面を重複計数しない。
        const float step = .01f;
        int crossings = 0;
        float remaining = distance + step;
        while (collider.Raycast(new Ray(position, direction), out RaycastHit hit, remaining))
        {
            if (++crossings > 64) return false;
            float advance = hit.distance + step;
            position += direction * advance;
            remaining -= advance;
            if (remaining <= 0f) break;
        }
        return crossings % 2 == 1;
    }

    public static void Ensure(MeshCollider collider)
    {
        if (collider != null && !collider.TryGetComponent<TubeDriveSurface>(out _))
            collider.gameObject.AddComponent<TubeDriveSurface>().hideFlags = HideFlags.DontSave;
        if (collider != null && Contains(collider)) TunnelAcoustics.Register(collider);
    }
}
