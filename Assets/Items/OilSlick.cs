using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// アイテム「オイル」で路面に置かれる油だまりです。車が踏むと滑らせて消えます。
/// 置いた本人は置いた直後だけ踏んでも反応しません。
/// </summary>
[DisallowMultipleComponent]
public sealed class OilSlick : MonoBehaviour
{
    private static readonly List<OilSlick> ActiveSlicks = new List<OilSlick>();

    private const float Radius = 2.2f;
    private const float TriggerHeight = 1.2f;
    private const float OwnerGraceSeconds = 1.5f;
    private const float LifetimeSeconds = 40f;
    private const float AppearSeconds = 0.25f;

    private CarItemEffects owner;
    private float age;
    private bool consumed;
    private Mesh mesh;
    private Material material;
    private Transform visual;

    /// <summary>路面の位置と法線に油だまりを置きます。</summary>
    public static OilSlick Spawn(Vector3 position, Vector3 normal, Vector3 forward, CarItemEffects owner)
    {
        GameObject slickObject = new GameObject("OilSlick");
        Vector3 up = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
        Vector3 look = Vector3.ProjectOnPlane(forward, up);
        if (look.sqrMagnitude < 0.0001f) look = Vector3.ProjectOnPlane(Vector3.forward, up);
        slickObject.transform.SetPositionAndRotation(position + up * 0.02f, Quaternion.LookRotation(look.normalized, up));
        OilSlick slick = slickObject.AddComponent<OilSlick>();
        slick.owner = owner;
        return slick;
    }

    /// <summary>レースのやり直し時などに、置かれた油だまりをすべて消します。</summary>
    public static void ClearAll()
    {
        for (int i = ActiveSlicks.Count - 1; i >= 0; i--)
        {
            if (ActiveSlicks[i] != null) Destroy(ActiveSlicks[i].gameObject);
        }
        ActiveSlicks.Clear();
    }

    private void Awake()
    {
        ActiveSlicks.Add(this);

        BoxCollider trigger = gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(Radius * 1.8f, TriggerHeight, Radius * 1.8f);
        trigger.center = Vector3.up * TriggerHeight * 0.5f;
        // Trigger の検出には片方の Rigidbody が必要です。車の Rigidbody に頼らず、自分側を静的な kinematic にします。
        Rigidbody body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        GameObject visualObject = new GameObject("OilSlickVisual");
        visualObject.transform.SetParent(transform, false);
        visual = visualObject.transform;
        mesh = ItemVisualFactory.CreateBlob(Radius, Random.Range(0, 100000));
        material = ItemVisualFactory.CreateEmissiveMaterial("OilSlickMaterial", new Color(0.03f, 0.025f, 0.02f, 1f), 0f, 0.95f);
        if (material != null && material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.35f);
        visualObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = visualObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        visual.localScale = Vector3.zero;
    }

    private void Update()
    {
        age += Time.deltaTime;
        // 置いた瞬間に広がるように見せます。
        float appear = Mathf.Clamp01(age / AppearSeconds);
        visual.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, appear);
        if (age >= LifetimeSeconds) Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (Gmanager.Control == null || !Gmanager.Control.IsDrivingEnabled) return;
        Rigidbody body = other.attachedRigidbody;
        CarItemEffects effects = body != null ? body.GetComponent<CarItemEffects>() : null;
        if (effects == null) return;
        if (consumed || (effects == owner && age < OwnerGraceSeconds)) return;

        // 車体の複数の Collider が同じ物理フレームに触れても、効果は1回だけにします。
        if (!effects.ApplyOil()) return;
        consumed = true;
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        ActiveSlicks.Remove(this);
        ItemVisualFactory.DestroyObject(mesh);
        ItemVisualFactory.DestroyObject(material);
    }
}
