using UnityEngine;

/// <summary>
/// 車両が踏むと、盤面の前方へ一定時間加速させる地面設置用の加速度盤です。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
[AddComponentMenu("Racing/Acceleration Pad")]
public sealed class AccelerationPad : MonoBehaviour
{
    [Header("Acceleration")]
    [Tooltip("盤面の前方へ加える加速度（m/s²）。")]
    [SerializeField, Min(0f)] private float acceleration = 12f;
    [Tooltip("踏んだ後に加速を続ける時間（秒）。")]
    [SerializeField, Min(0f)] private float boostDuration = 1f;

    [Header("Pad Size")]
    [Tooltip("盤面のローカルサイズ。Yは盤面の厚みです。")]
    [SerializeField] private Vector3 padSize = new Vector3(4f, 0.2f, 8f);
    [SerializeField, Min(0f)] private float triggerMargin = 0.15f;

    [Header("Visual")]
    [SerializeField] private Material visualMaterial;
    [SerializeField] private Color padColor = new Color(0.05f, 0.8f, 1f, 1f);
    [SerializeField] private bool generateVisual = true;

    private const string GeneratedVisualName = "AccelerationPadVisual";
    private BoxCollider trigger;
    private Material runtimeMaterial;
    private Mesh runtimeMesh;

    public Vector3 PadSize => padSize;
    public float Acceleration => acceleration;
    public float BoostDuration => boostDuration;

    private void OnEnable()
    {
        Refresh();
    }

    private void OnDisable()
    {
        CleanupVisual();
    }

    private void OnDestroy()
    {
        CleanupVisual();
        if (runtimeMaterial != null)
        {
            if (Application.isPlaying) Destroy(runtimeMaterial);
            else DestroyImmediate(runtimeMaterial);
            runtimeMaterial = null;
        }
    }

    private void OnValidate()
    {
        padSize.x = Mathf.Max(0.1f, padSize.x);
        padSize.y = Mathf.Max(0.02f, padSize.y);
        padSize.z = Mathf.Max(0.1f, padSize.z);
        acceleration = Mathf.Max(0f, acceleration);
        boostDuration = Mathf.Max(0f, boostDuration);
        triggerMargin = Mathf.Max(0f, triggerMargin);

        if (trigger == null) trigger = GetComponent<BoxCollider>();
        ConfigureTrigger();

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += DelayedRefresh;
        }
#endif
    }

#if UNITY_EDITOR
    private void DelayedRefresh()
    {
        if (this == null || Application.isPlaying || !isActiveAndEnabled) return;
        Refresh();
    }
#endif

    /// <summary>Inspectorや配置ツールから、Colliderと表示を現在の設定へ同期します。</summary>
    public void Refresh()
    {
        if (trigger == null) trigger = GetComponent<BoxCollider>();
        ConfigureTrigger();
        if (generateVisual) BuildVisual();
        else CleanupVisual();
    }

    private void ConfigureTrigger()
    {
        if (trigger == null) return;
        trigger.isTrigger = true;
        trigger.center = Vector3.up * (padSize.y * 0.5f);
        trigger.size = new Vector3(padSize.x, padSize.y + triggerMargin, padSize.z);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!Application.isPlaying) return;
        if (Gmanager.Control == null || !Gmanager.Control.IsDrivingEnabled) return;

        DebugMover mover = other.attachedRigidbody != null
            ? other.attachedRigidbody.GetComponent<DebugMover>()
            : null;
        if (mover == null) mover = other.GetComponentInParent<DebugMover>();
        if (mover == null) return;

        Vector3 direction = Vector3.ProjectOnPlane(transform.forward, transform.up).normalized;
        mover.StartAccelerationPadBoost(acceleration, boostDuration, direction);
    }

    /// <summary>盤面の中心位置から下方向へレイを飛ばし、地面に接地させます。</summary>
    public bool PlaceOnGround(float rayDistance = 1000f)
    {
        Vector3 origin = transform.position + Vector3.up * Mathf.Max(10f, rayDistance * 0.5f);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayDistance + 10f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(transform.up, hit.normal).normalized;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.Cross(hit.normal, Vector3.right).normalized;

        transform.SetPositionAndRotation(
            hit.point + hit.normal * (padSize.y * 0.5f),
            Quaternion.LookRotation(forward, hit.normal));
        Refresh();
        return true;
    }

    private void BuildVisual()
    {
        CleanupVisual();

        GameObject visual = new GameObject(GeneratedVisualName);
        visual.hideFlags = HideFlags.DontSave;
        visual.transform.SetParent(transform, false);

        MeshFilter filter = visual.AddComponent<MeshFilter>();
        MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
        runtimeMesh = CreatePadMesh();
        filter.sharedMesh = runtimeMesh;
        renderer.sharedMaterial = GetVisualMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private Mesh CreatePadMesh()
    {
        float width = padSize.x;
        float length = padSize.z;
        float y = padSize.y + 0.006f;
        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();

        AddQuad(vertices, triangles,
            new Vector3(-width * 0.5f, y, -length * 0.5f),
            new Vector3(width * 0.5f, y, -length * 0.5f),
            new Vector3(width * 0.5f, y, length * 0.5f),
            new Vector3(-width * 0.5f, y, length * 0.5f));

        // 盤面の向きを視認できる矢印を3つ配置します。
        float arrowWidth = width * 0.48f;
        float arrowLength = Mathf.Min(length * 0.18f, 1.3f);
        for (int i = 0; i < 3; i++)
        {
            float z = Mathf.Lerp(-length * 0.3f, length * 0.28f, i / 2f);
            float head = arrowLength * 0.42f;
            AddQuad(vertices, triangles,
                new Vector3(-arrowWidth * 0.5f, y + 0.002f, z - arrowLength * 0.5f),
                new Vector3(arrowWidth * 0.5f, y + 0.002f, z - arrowLength * 0.5f),
                new Vector3(arrowWidth * 0.5f, y + 0.002f, z + arrowLength * 0.5f - head),
                new Vector3(-arrowWidth * 0.5f, y + 0.002f, z + arrowLength * 0.5f - head));
            AddQuad(vertices, triangles,
                new Vector3(-arrowWidth * 0.5f, y + 0.002f, z + arrowLength * 0.5f - head),
                new Vector3(arrowWidth * 0.5f, y + 0.002f, z + arrowLength * 0.5f - head),
                new Vector3(0f, y + 0.002f, z + arrowLength * 0.5f),
                new Vector3(0f, y + 0.002f, z + arrowLength * 0.5f));
        }

        Mesh mesh = new Mesh { name = "AccelerationPadMesh", hideFlags = HideFlags.DontSave };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Material GetVisualMaterial()
    {
        if (visualMaterial != null) return visualMaterial;
        if (runtimeMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null)
            {
                runtimeMaterial = new Material(shader) { name = "AccelerationPadRuntimeMaterial", hideFlags = HideFlags.DontSave };
                runtimeMaterial.EnableKeyword("_EMISSION");
            }
        }

        if (runtimeMaterial != null)
        {
            if (runtimeMaterial.HasProperty("_BaseColor")) runtimeMaterial.SetColor("_BaseColor", padColor);
            if (runtimeMaterial.HasProperty("_Color")) runtimeMaterial.SetColor("_Color", padColor);
            if (runtimeMaterial.HasProperty("_EmissionColor")) runtimeMaterial.SetColor("_EmissionColor", padColor * 1.5f);
        }
        return runtimeMaterial;
    }

    private void CleanupVisual()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name != GeneratedVisualName) continue;
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

        if (runtimeMesh != null)
        {
            if (Application.isPlaying) Destroy(runtimeMesh);
            else DestroyImmediate(runtimeMesh);
            runtimeMesh = null;
        }
    }

    private static void AddQuad(System.Collections.Generic.List<Vector3> vertices,
        System.Collections.Generic.List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int start = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        // 上面を向くように反時計回りで登録します。
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
        triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.8f);
        Matrix4x4 matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        Gizmos.matrix = matrix;
        Gizmos.DrawWireCube(Vector3.up * padSize.y * 0.5f, padSize);
        Gizmos.DrawRay(Vector3.up * (padSize.y + 0.02f), Vector3.forward * Mathf.Min(2f, padSize.z * 0.3f));
        Gizmos.matrix = Matrix4x4.identity;
    }
}
