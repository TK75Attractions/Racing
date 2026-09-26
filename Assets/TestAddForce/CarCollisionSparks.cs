using System;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

/// <summary>対戦車両同士の接触点と、壁をこすった点に、両画面で見える火花を発生させます。
/// 衝突の瞬間には強さを <see cref="Impact"/> で通知し、カメラの揺れに使います。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class CarCollisionSparks : MonoBehaviour
{
    [SerializeField, Min(0f)] private float minimumSpeed = 0.5f;
    [SerializeField, Min(0.02f)] private float emissionInterval = 0.08f;

    [Header("壁")]
    [Tooltip("建物やバリアなど、動かない物体との接触でも火花を出します。")]
    [SerializeField] private bool wallSparksEnabled = true;
    [Tooltip("この値以下の接触法線Yを壁として扱います。地面との接触では火花を出しません。")]
    [SerializeField, Range(0f, 1f)] private float maximumWallNormalY = 0.65f;
    [Tooltip("壁をこする速さ（m/s）がこの値未満では火花を出しません。")]
    [SerializeField, Min(0f)] private float minimumWallSpeed = 3f;

    [Header("衝突の揺れ")]
    [Tooltip("この速さ（m/s）でぶつかったときに揺れが最大になります。")]
    [SerializeField, Min(0.1f)] private float impactFullSpeed = 15f;
    [Tooltip("この速さ（m/s）未満の接触では揺らしません。")]
    [SerializeField, Min(0f)] private float impactMinimumSpeed = 2f;

    private static readonly Color CarSparkColor = new Color(1f, 0.85f, 0.35f, 1f);
    private static readonly Color WallSparkColor = new Color(1f, 0.78f, 0.3f, 1f);

    private Rigidbody body;
    private ParticleSystem sparks;
    private Material sparkMaterial;
    private float nextEmissionTime;
    private float nextWallEmissionTime;

    /// <summary>衝突の瞬間に、強さ（0〜1）を通知します。車同士の衝突では両方の車に届きます。</summary>
    public event Action<float> Impact;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        NotifyImpact(collision);
        EmitContactSparks(collision);
    }

    private void OnCollisionStay(Collision collision) => EmitContactSparks(collision);

    private void NotifyImpact(Collision collision)
    {
        if (Impact == null || !IsGameplayActive() || collision.contactCount == 0) return;

        float impactSpeed;
        if (IsDynamicBody(collision.rigidbody))
        {
            if (collision.rigidbody.GetComponent<CarCollisionSparks>() == null) return;
            impactSpeed = collision.relativeVelocity.magnitude;
        }
        else
        {
            if (!TryGetWallNormal(collision, out Vector3 wallNormal)) return;
            // 正面から当たるほど強く、壁をかすめるだけなら弱く揺らします。
            impactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, wallNormal));
        }

        float strength = EvaluateImpactStrength(impactSpeed);
        if (strength > 0f) Impact.Invoke(strength);
    }

    /// <summary>衝突の速さ（m/s）を揺れの強さ（0〜1）に変換します。</summary>
    public float EvaluateImpactStrength(float impactSpeed)
    {
        if (impactSpeed < impactMinimumSpeed) return 0f;
        return Mathf.Clamp01(impactSpeed / Mathf.Max(0.1f, impactFullSpeed));
    }

    /// <summary>接触法線が壁とみなせる向きかを返します。</summary>
    public bool IsWallNormal(Vector3 normal) => Mathf.Abs(normal.y) <= maximumWallNormalY;

    private void EmitContactSparks(Collision collision)
    {
        if (!IsGameplayActive() || collision.contactCount == 0) return;

        if (IsDynamicBody(collision.rigidbody))
        {
            EmitCarSparks(collision);
        }
        else if (wallSparksEnabled)
        {
            EmitWallSparks(collision);
        }
    }

    private void EmitCarSparks(Collision collision)
    {
        if (Time.time < nextEmissionTime) return;

        CarCollisionSparks other = collision.rigidbody.GetComponent<CarCollisionSparks>();
        // 衝突通知は双方に届くため、一方だけが描画を担当します。
        if (other == null || !other.isActiveAndEnabled || GetInstanceID() > other.GetInstanceID()) return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed < minimumSpeed) return;
        if (sparks == null && !CreateSparks()) return;

        nextEmissionTime = Time.time + emissionInterval;
        float strength = Mathf.Clamp01(speed / 15f);
        int contactCount = Mathf.Min(collision.contactCount, 4);
        int count = Mathf.Max(2, Mathf.RoundToInt(Mathf.Lerp(10f, 32f, strength) / contactCount));
        for (int contactIndex = 0; contactIndex < contactCount; contactIndex++)
        {
            ContactPoint contact = collision.GetContact(contactIndex);
            Vector3 inheritedVelocity = (body.GetPointVelocity(contact.point) +
                collision.rigidbody.GetPointVelocity(contact.point)) * 0.15f;
            EmitAt(contact.point, contact.normal, count, strength, inheritedVelocity, CarSparkColor);
        }
    }

    private void EmitWallSparks(Collision collision)
    {
        if (Time.time < nextWallEmissionTime) return;

        // 壁との接触は相手が動かないため、自分の速さがそのままこする速さになります。
        float speed = collision.relativeVelocity.magnitude;
        if (speed < minimumWallSpeed) return;

        int contactCount = Mathf.Min(collision.contactCount, 4);
        int wallContacts = 0;
        for (int contactIndex = 0; contactIndex < contactCount; contactIndex++)
        {
            if (IsWallNormal(collision.GetContact(contactIndex).normal)) wallContacts++;
        }
        if (wallContacts == 0) return;
        if (sparks == null && !CreateSparks()) return;

        nextWallEmissionTime = Time.time + emissionInterval;
        float strength = Mathf.Clamp01(speed / 20f);
        int count = Mathf.Max(2, Mathf.RoundToInt(Mathf.Lerp(8f, 28f, strength) / wallContacts));
        for (int contactIndex = 0; contactIndex < contactCount; contactIndex++)
        {
            ContactPoint contact = collision.GetContact(contactIndex);
            if (!IsWallNormal(contact.normal)) continue;
            // 火花は進行方向へ流れるように、車体の速度を強めに引き継ぎます。
            Vector3 inheritedVelocity = body.GetPointVelocity(contact.point) * 0.35f;
            EmitAt(contact.point, contact.normal, count, strength, inheritedVelocity, WallSparkColor);
        }
    }

    private void EmitAt(Vector3 point, Vector3 normal, int count, float strength, Vector3 inheritedVelocity, Color color)
    {
        for (int index = 0; index < count; index++)
        {
            Vector3 direction = Vector3.ProjectOnPlane(Random.onUnitSphere, normal) +
                normal * Random.Range(-0.35f, 0.35f) + Vector3.up * 0.6f;
            ParticleSystem.EmitParams particle = new ParticleSystem.EmitParams
            {
                position = point + Vector3.up * 0.04f,
                velocity = direction.normalized * Random.Range(2f, Mathf.Lerp(4f, 9f, strength)) + inheritedVelocity,
                startLifetime = Random.Range(0.15f, 0.4f),
                startSize = Random.Range(0.035f, 0.075f),
                startColor = color
            };
            sparks.Emit(particle, 1);
        }
    }

    private bool TryGetWallNormal(Collision collision, out Vector3 wallNormal)
    {
        wallNormal = Vector3.zero;
        for (int index = 0; index < collision.contactCount; index++)
        {
            Vector3 normal = collision.GetContact(index).normal;
            if (IsWallNormal(normal)) wallNormal += normal;
        }

        if (wallNormal.sqrMagnitude < 0.0001f) return false;
        wallNormal.Normalize();
        return true;
    }

    private bool IsGameplayActive() =>
        isActiveAndEnabled && Gmanager.Control != null && Gmanager.Control.IsDrivingEnabled;

    private static bool IsDynamicBody(Rigidbody other) => other != null && !other.isKinematic;

    private bool CreateSparks()
    {
        // Resources参照により、ビルド時にも専用シェーダーが含まれます。
        Shader shader = Resources.Load<Shader>("CarCollisionSparks");
        if (shader == null)
        {
            Debug.LogError("CarCollisionSparks shader was not found.", this);
            enabled = false;
            return false;
        }

        GameObject effect = new GameObject("Car Collision Sparks");
        effect.layer = gameObject.layer;
        effect.transform.SetParent(transform, false);
        sparks = effect.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.maxParticles = 384;
        main.gravityModifier = 0.8f;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = sparks.emission;
        emission.enabled = false;
        var shape = sparks.shape;
        shape.enabled = false;
        var color = sparks.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.25f, 0.02f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var size = sparks.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        sparkMaterial = new Material(shader);
        var renderer = sparks.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = sparkMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.035f;
        renderer.lengthScale = 2f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        sparks.Play();
        return true;
    }

    private void OnDisable()
    {
        if (sparks != null) sparks.Clear();
        nextEmissionTime = 0f;
        nextWallEmissionTime = 0f;
    }

    private void OnDestroy()
    {
        Impact = null;
        if (sparkMaterial != null) Destroy(sparkMaterial);
    }
}
