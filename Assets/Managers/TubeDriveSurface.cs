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

    public static void Ensure(MeshCollider collider)
    {
        if (collider != null && !collider.TryGetComponent<TubeDriveSurface>(out _))
            collider.gameObject.AddComponent<TubeDriveSurface>().hideFlags = HideFlags.DontSave;
        if (collider != null && Contains(collider)) TunnelAcoustics.Register(collider);
    }
}
