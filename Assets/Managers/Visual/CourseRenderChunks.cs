using UnityEngine;

/// <summary>Serialized ownership/rollback record for the offline chunk builder. No runtime work.</summary>
[DisallowMultipleComponent]
public sealed class CourseRenderChunks : MonoBehaviour
{
    [HideInInspector] public MeshRenderer source;
    [HideInInspector] public Mesh sourceMesh;
    [HideInInspector] public bool sourceWasEnabled;
}
