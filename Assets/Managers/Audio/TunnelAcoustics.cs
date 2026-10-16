using System.Collections.Generic;
using UnityEngine;

/// <summary>Tests actual tube walls rather than world bounds (which include the land above the tube).</summary>
public static class TunnelAcoustics
{
    private static readonly HashSet<MeshCollider> Surfaces = new HashSet<MeshCollider>();
    private static readonly Vector3[] Directions = { Vector3.up, Vector3.down, Vector3.left,
        Vector3.right, Vector3.forward, Vector3.back };
    public static void Register(MeshCollider surface) { if (surface != null) Surfaces.Add(surface); }
    public static void Unregister(MeshCollider surface) { if (surface != null) Surfaces.Remove(surface); }

    public static float EnclosureAt(Vector3 position)
    {
        foreach (MeshCollider surface in Surfaces)
        {
            if (surface == null || !surface.enabled || !surface.gameObject.activeInHierarchy || !surface.bounds.Contains(position)) continue;
            int hits = 0, mask = 0;
            for (int i = 0; i < Directions.Length; i++)
                if (surface.Raycast(new Ray(position, Directions[i]), out RaycastHit _, 35f))
                { hits++; mask |= 1 << i; }
            bool opposite = (mask & 3) == 3 || (mask & 12) == 12 || (mask & 48) == 48;
            if (hits >= 3 && opposite) return Mathf.Clamp01(.65f + (hits - 3) * .12f);
        }
        return 0f;
    }
}
