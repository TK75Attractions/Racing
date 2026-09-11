using UnityEngine;
using UnityEngine.Rendering;

/// <summary>対戦車両同士の接触点に、両画面で見える火花を発生させます。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class CarCollisionSparks : MonoBehaviour
{
    [SerializeField, Min(0f)] private float minimumSpeed = 0.5f;
    [SerializeField, Min(0.02f)] private float emissionInterval = 0.08f;

    private Rigidbody body;
    private ParticleSystem sparks;
    private Material sparkMaterial;
    private float nextEmissionTime;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void OnCollisionEnter(Collision collision) => EmitContactSparks(collision);
    private void OnCollisionStay(Collision collision) => EmitContactSparks(collision);

    private void EmitContactSparks(Collision collision)
    {
        if (!isActiveAndEnabled || Gmanager.Control == null || !Gmanager.Control.IsDrivingEnabled ||
            Time.time < nextEmissionTime || collision.rigidbody == null || collision.contactCount == 0)
            return;

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
            for (int index = 0; index < count; index++)
            {
                Vector3 direction = Vector3.ProjectOnPlane(Random.onUnitSphere, contact.normal) +
                    contact.normal * Random.Range(-0.35f, 0.35f) + Vector3.up * 0.6f;
                ParticleSystem.EmitParams particle = new ParticleSystem.EmitParams
                {
                    position = contact.point + Vector3.up * 0.04f,
                    velocity = direction.normalized * Random.Range(2f, Mathf.Lerp(4f, 9f, strength)) + inheritedVelocity,
                    startLifetime = Random.Range(0.15f, 0.4f),
                    startSize = Random.Range(0.035f, 0.075f),
                    startColor = new Color(1f, 0.85f, 0.35f, 1f)
                };
                sparks.Emit(particle, 1);
            }
        }
    }

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
        main.maxParticles = 256;
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
    }

    private void OnDestroy()
    {
        if (sparkMaterial != null) Destroy(sparkMaterial);
    }
}
