using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 車が触れると効果を発動し、一定時間消えてから再出現する、コース設置型の取得オブジェクトの基底クラスです。
/// 見た目は実行時に生成し、Trigger 用の SphereCollider を自動設定します。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(SphereCollider))]
public abstract class RaceItemPickup : MonoBehaviour
{
    private static readonly List<RaceItemPickup> ActivePickups = new List<RaceItemPickup>();

    [Header("Pickup")]
    [Tooltip("取得後に再出現するまでの時間（秒）。")]
    [SerializeField, Min(0f)] private float respawnSeconds = 6f;
    [Tooltip("取得判定の半径（m）。")]
    [SerializeField, Min(0.1f)] private float triggerRadius = 1.6f;

    [Header("Visual")]
    [Tooltip("地面からの浮き上がり高さ（m）。")]
    [SerializeField, Min(0f)] private float hoverHeight = 1.2f;
    [SerializeField, Min(0f)] private float bobAmplitude = 0.15f;
    [SerializeField, Min(0f)] private float spinDegreesPerSecond = 90f;

    private const string GeneratedVisualName = "ItemPickupVisual";
    private SphereCollider trigger;
    private Transform visualRoot;
    private Mesh runtimeMesh;
    private Material runtimeMaterial;
    private float respawnTimeRemaining;
    private float phaseOffset;

    public bool IsAvailable => respawnTimeRemaining <= 0f;
    public float HoverHeight => hoverHeight;
    protected Material VisualMaterial => runtimeMaterial;

    /// <summary>レース開始時に、取得済みのものをすべて再出現させます。</summary>
    public static void ResetAll()
    {
        foreach (RaceItemPickup pickup in ActivePickups) pickup.SetAvailable();
    }

    protected virtual void OnEnable()
    {
        if (!ActivePickups.Contains(this)) ActivePickups.Add(this);
        phaseOffset = Mathf.Abs(transform.position.GetHashCode() % 1000) * 0.01f;
        Refresh();
    }

    protected virtual void OnDisable()
    {
        ActivePickups.Remove(this);
        CleanupVisual();
    }

    protected virtual void OnValidate()
    {
        triggerRadius = Mathf.Max(0.1f, triggerRadius);
        if (trigger == null) trigger = GetComponent<SphereCollider>();
        ConfigureTrigger();
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorApplication.delayCall += DelayedRefresh;
#endif
    }

#if UNITY_EDITOR
    private void DelayedRefresh()
    {
        if (this == null || Application.isPlaying || !isActiveAndEnabled) return;
        Refresh();
    }
#endif

    /// <summary>Colliderと見た目を現在の設定へ同期します。</summary>
    public void Refresh()
    {
        if (trigger == null) trigger = GetComponent<SphereCollider>();
        ConfigureTrigger();
        BuildVisual();
        ApplyAvailability();
    }

    /// <summary>取得オブジェクトの中心から下へレイを飛ばし、地面から hoverHeight の位置へ配置します。</summary>
    public bool PlaceOnGround(float rayDistance = 1000f)
    {
        Vector3 origin = transform.position + Vector3.up * Mathf.Max(10f, rayDistance * 0.5f);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayDistance + 10f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        transform.position = hit.point;
        Refresh();
        return true;
    }

    private void Update()
    {
        if (!Application.isPlaying) return;

        if (respawnTimeRemaining > 0f)
        {
            respawnTimeRemaining -= Time.deltaTime;
            if (respawnTimeRemaining <= 0f) SetAvailable();
        }

        if (visualRoot != null && IsAvailable)
        {
            float time = Time.time + phaseOffset;
            visualRoot.localPosition = Vector3.up * (hoverHeight + Mathf.Sin(time * 2.2f) * bobAmplitude);
            visualRoot.localRotation = Quaternion.Euler(0f, time * spinDegreesPerSecond, 0f) * GetVisualTilt();
            AnimateVisual(time);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!Application.isPlaying || !IsAvailable) return;
        if (Gmanager.Control == null || !Gmanager.Control.IsDrivingEnabled) return;

        Rigidbody body = other.attachedRigidbody;
        DebugMover mover = body != null ? body.GetComponent<DebugMover>() : other.GetComponentInParent<DebugMover>();
        if (mover == null || !mover.isActiveAndEnabled) return;
        if (body == null) body = mover.GetComponent<Rigidbody>();
        CarItemEffects effects = mover.GetComponent<CarItemEffects>();

        // 車体の複数の Collider が同じフレームに触れても、最初の1回で消えるため二重取得になりません。
        if (!OnCollected(mover, body, effects)) return;
        respawnTimeRemaining = Mathf.Max(0.01f, respawnSeconds);
        ApplyAvailability();
    }

    /// <summary>車が触れたときの効果です。取得扱いにして消す場合は true を返します。</summary>
    protected abstract bool OnCollected(DebugMover mover, Rigidbody body, CarItemEffects effects);
    protected abstract Mesh CreateVisualMesh();
    protected abstract Material CreateVisualMaterial();
    protected virtual Quaternion GetVisualTilt() => Quaternion.identity;
    protected virtual void AnimateVisual(float time) { }

    private void SetAvailable()
    {
        respawnTimeRemaining = 0f;
        ApplyAvailability();
    }

    private void ApplyAvailability()
    {
        bool available = IsAvailable || !Application.isPlaying;
        if (trigger != null) trigger.enabled = available;
        if (visualRoot != null) visualRoot.gameObject.SetActive(available);
    }

    private void ConfigureTrigger()
    {
        if (trigger == null) return;
        trigger.isTrigger = true;
        trigger.radius = triggerRadius;
        trigger.center = Vector3.up * hoverHeight;
    }

    private void BuildVisual()
    {
        CleanupVisual();
        GameObject visual = new GameObject(GeneratedVisualName);
        visual.hideFlags = HideFlags.DontSave;
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = Vector3.up * hoverHeight;
        visual.transform.localRotation = GetVisualTilt();
        visualRoot = visual.transform;

        runtimeMesh = CreateVisualMesh();
        if (runtimeMaterial == null) runtimeMaterial = CreateVisualMaterial();
        visual.AddComponent<MeshFilter>().sharedMesh = runtimeMesh;
        MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = runtimeMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void CleanupVisual()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name == GeneratedVisualName) ItemVisualFactory.DestroyObject(child.gameObject);
        }

        visualRoot = null;
        ItemVisualFactory.DestroyObject(runtimeMesh);
        runtimeMesh = null;
    }

    protected virtual void OnDestroy()
    {
        CleanupVisual();
        ItemVisualFactory.DestroyObject(runtimeMaterial);
        runtimeMaterial = null;
    }

    protected virtual void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * hoverHeight, triggerRadius);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * hoverHeight);
    }
}
