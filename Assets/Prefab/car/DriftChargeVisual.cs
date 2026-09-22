using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// ドリフトのチャージ量を後輪の火花で示します。段階が上がるほど色が変わり、量も増えます。
/// 車体に付くため両プレイヤーの画面に映り、相手の溜め具合も読み取れます。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DebugMover))]
public sealed class DriftChargeVisual : MonoBehaviour
{
    [Header("Tier Colors")]
    [Tooltip("チャージ段階ごとの火花の色。先頭が段階0（溜め始め）です。画面端の色にも同じ値を使います。")]
    [SerializeField] private Color[] tierColors =
    {
        new Color(1f, 1f, 1f, 1f),
        new Color(0.25f, 0.65f, 1f, 1f),
        new Color(1f, 0.6f, 0.12f, 1f),
        new Color(0.85f, 0.35f, 1f, 1f)
    };

    [Header("Emission")]
    [Tooltip("後輪1本あたり、1秒間に出す火花の数。段階が上がるほど増えます。")]
    [SerializeField, Min(0f)] private float sparksPerSecondPerWheel = 26f;

    [Tooltip("段階1つあたりの発生量の増加割合。")]
    [SerializeField, Min(0f)] private float tierEmissionBonus = 0.6f;

    [Tooltip("段階が上がった瞬間に、後輪1本あたりから弾けさせる数。")]
    [SerializeField, Min(0)] private int tierUpBurst = 18;

    [Tooltip("解放した瞬間に、後輪1本あたりから弾けさせる数。段階が高いほど増えます。")]
    [SerializeField, Min(0)] private int releaseBurst = 26;

    [Tooltip("この速度（m/s）未満では火花を出しません。")]
    [SerializeField, Min(0f)] private float minimumSpeed = 2f;

    [Tooltip("満チャージ時に火花の量を脈動させる速さ（Hz）。0で脈動しません。")]
    [SerializeField, Min(0f)] private float fullChargePulseHz = 3f;

    private DebugMover mover;
    private Rigidbody body;
    private Transform[] rearWheels;
    private ParticleSystem sparks;
    private Material sparkMaterial;
    private float emissionAccumulator;
    private int previousTier;
    private bool wasDrifting;

    /// <summary>現在の段階の色です。画面演出にも同じ色を渡して見た目を揃えます。</summary>
    public Color CurrentTierColor => GetTierColor(mover != null ? mover.DriftChargeTier : 0);

    private void Awake()
    {
        mover = GetComponent<DebugMover>();
        body = GetComponent<Rigidbody>();
        CacheRearWheels();
    }

    private void Update()
    {
        if (mover == null || Gmanager.Control == null || !Gmanager.Control.IsDrivingEnabled)
        {
            ResetState();
            return;
        }

        int tier = mover.DriftChargeTier;
        bool isDrifting = mover.IsDrifting;

        if (isDrifting && tier > previousTier)
        {
            EmitBurst(tierUpBurst, GetTierColor(tier), 1.35f);
        }
        else if (wasDrifting && !isDrifting && previousTier > 0)
        {
            // 解放の瞬間は、直前まで溜めていた段階の色で大きく弾けさせる。
            EmitBurst(releaseBurst * previousTier, GetTierColor(previousTier), 1.8f);
        }

        if (isDrifting)
        {
            EmitTrail(tier, Time.deltaTime);
        }
        else
        {
            emissionAccumulator = 0f;
        }

        previousTier = isDrifting ? tier : 0;
        wasDrifting = isDrifting;
    }

    private void EmitTrail(int tier, float deltaTime)
    {
        if (body != null && body.linearVelocity.magnitude < minimumSpeed)
        {
            emissionAccumulator = 0f;
            return;
        }

        float rate = sparksPerSecondPerWheel * (1f + tier * Mathf.Max(0f, tierEmissionBonus));
        if (mover.IsDriftChargeFull && fullChargePulseHz > 0f)
        {
            // 満チャージは量の脈動で「解放できる」ことを伝える。
            rate *= 1f + 0.45f * Mathf.Sin(Time.time * fullChargePulseHz * Mathf.PI * 2f);
        }

        emissionAccumulator += Mathf.Max(0f, rate) * Mathf.Max(0f, deltaTime);
        int count = Mathf.FloorToInt(emissionAccumulator);
        if (count <= 0)
        {
            return;
        }

        emissionAccumulator -= count;
        EmitAtWheels(count, GetTierColor(tier), 1f);
    }

    private void EmitBurst(int countPerWheel, Color color, float sizeScale)
    {
        if (countPerWheel <= 0)
        {
            return;
        }

        EmitAtWheels(countPerWheel, color, sizeScale);
    }

    private void EmitAtWheels(int countPerWheel, Color color, float sizeScale)
    {
        if (rearWheels == null || rearWheels.Length == 0)
        {
            CacheRearWheels();
        }

        if (rearWheels.Length == 0) return;
        if (sparks == null && !CreateSparks()) return;

        Vector3 inheritedVelocity = body != null ? body.linearVelocity * 0.25f : Vector3.zero;
        foreach (Transform wheel in rearWheels)
        {
            if (wheel == null) continue;
            for (int index = 0; index < countPerWheel; index++)
            {
                Vector3 direction = -transform.forward * Random.Range(0.4f, 1.2f) +
                    transform.right * Random.Range(-0.9f, 0.9f) +
                    Vector3.up * Random.Range(0.2f, 1f);
                ParticleSystem.EmitParams particle = new ParticleSystem.EmitParams
                {
                    position = wheel.position + Vector3.up * 0.05f,
                    velocity = direction.normalized * Random.Range(1.5f, 4.5f) + inheritedVelocity,
                    startLifetime = Random.Range(0.18f, 0.42f),
                    startSize = Random.Range(0.03f, 0.07f) * Mathf.Max(0.1f, sizeScale),
                    startColor = color
                };
                sparks.Emit(particle, 1);
            }
        }
    }

    private void CacheRearWheels()
    {
        TireForce[] tires = GetComponentsInChildren<TireForce>(true);
        int rearCount = 0;
        foreach (TireForce tire in tires)
        {
            if (!tire.IsFrontWheel) rearCount++;
        }

        rearWheels = new Transform[rearCount];
        int writeIndex = 0;
        foreach (TireForce tire in tires)
        {
            if (!tire.IsFrontWheel) rearWheels[writeIndex++] = tire.transform;
        }
    }

    private Color GetTierColor(int tier)
    {
        if (tierColors == null || tierColors.Length == 0)
        {
            return Color.white;
        }

        return tierColors[Mathf.Clamp(tier, 0, tierColors.Length - 1)];
    }

    private bool CreateSparks()
    {
        // 衝突火花と同じ加算シェーダーを共有します。Resources参照によりビルドにも含まれます。
        Shader shader = Resources.Load<Shader>("CarCollisionSparks");
        if (shader == null)
        {
            Debug.LogError("DriftChargeVisual requires the CarCollisionSparks shader.", this);
            enabled = false;
            return false;
        }

        GameObject effect = new GameObject("Drift Charge Sparks");
        effect.layer = gameObject.layer;
        effect.transform.SetParent(transform, false);
        sparks = effect.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.maxParticles = 512;
        main.gravityModifier = 0.55f;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = sparks.emission;
        emission.enabled = false;
        var shape = sparks.shape;
        shape.enabled = false;
        var sizeOverLifetime = sparks.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));
        var colorOverLifetime = sparks.colorOverLifetime;
        colorOverLifetime.enabled = true;
        // 色は段階ごとの startColor が決めるため、ここでは消え方だけを与えます。
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;

        sparkMaterial = new Material(shader);
        var renderer = sparks.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = sparkMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        sparks.Play();
        return true;
    }

    private void ResetState()
    {
        emissionAccumulator = 0f;
        previousTier = 0;
        wasDrifting = false;
    }

    private void OnDisable()
    {
        ResetState();
        if (sparks != null) sparks.Clear();
    }

    private void OnDestroy()
    {
        if (sparkMaterial != null) Destroy(sparkMaterial);
    }
}
