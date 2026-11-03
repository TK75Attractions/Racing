using UnityEngine;

/// <summary>Query-only school geometry must never supply ground or recovery obstacles.</summary>
public static class CoursePhysicsLayers
{
    public const int OcclusionLayer = 14;
    public const int OcclusionMask = 1 << OcclusionLayer;
    public const int PhysicalMask = ~OcclusionMask;
    public const int GroundMask = Physics.DefaultRaycastLayers & PhysicalMask;
    public const int SightMask = Physics.DefaultRaycastLayers | OcclusionMask;
}
