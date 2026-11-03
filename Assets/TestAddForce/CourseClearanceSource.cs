using UnityEngine;

/// <summary>Persistent input and owned query collider for a school collision clearance bake.</summary>
[AddComponentMenu("")]
public sealed class CourseClearanceSource : MonoBehaviour
{
    public MeshCollider target;
    public Mesh originalMesh;
    public bool originalEnabled;
    public MeshCollider occlusion;
}
