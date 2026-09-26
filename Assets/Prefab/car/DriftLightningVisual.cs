using UnityEngine;

/// <summary>
/// ドリフトのチャージが最大段階にある間は車体に放電をまとわせ、解放した瞬間に前方へ稲妻を走らせます。
/// 稲妻は車体に追従するワールド空間の演出なので、相手の画面からも満チャージが読み取れます。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DebugMover))]
public sealed class DriftLightningVisual : MonoBehaviour
{
    [Header("Full Charge Arcs")]
    [Tooltip("満チャージ中に、車体の放電を出す間隔（秒）の範囲。")]
    [SerializeField] private Vector2 arcIntervalRange = new Vector2(0.05f, 0.16f);
    [Tooltip("放電1本の長さ（m）の範囲。")]
    [SerializeField] private Vector2 arcLengthRange = new Vector2(0.5f, 1.4f);
    [SerializeField, Min(0f)] private float arcWidth = 0.06f;
    [SerializeField, Min(0.01f)] private float arcDuration = 0.1f;
    [Tooltip("放電が後輪から出る割合。")]
    [SerializeField, Range(0f, 1f)] private float wheelArcChance = 0.3f;

    [Header("Release")]
    [Tooltip("解放時に車体の両脇を走る稲妻の太さ（m）。")]
    [SerializeField, Min(0f)] private float releaseWidth = 0.16f;
    [Tooltip("解放時の稲妻が車体の前へ伸びる距離（m）。")]
    [SerializeField, Min(0f)] private float releaseReach = 5f;
    [SerializeField, Min(0.01f)] private float releaseDuration = 0.32f;
    [Tooltip("解放時に車体の周りで弾けさせる放電の本数。")]
    [SerializeField, Min(0)] private int releaseArcBurst = 6;
    [SerializeField, Min(0f)] private float releaseLightIntensity = 12f;
    [Tooltip("解放時に、このプレイヤーの画面を白く光らせる強さ。")]
    [SerializeField, Range(0f, 1f)] private float releaseScreenFlash = 0.45f;
    [Tooltip("解放時に、このプレイヤーのカメラへ伝える揺れの強さ。")]
    [SerializeField, Range(0f, 1f)] private float releaseCameraImpact = 0.2f;
    [SerializeField, Range(0f, 1f)] private float releaseSoundVolume = 0.55f;

    [Header("Fallback")]
    [Tooltip("DriftChargeVisual が無いときに使う満チャージの色。")]
    [SerializeField] private Color fallbackColor = new Color(0.85f, 0.35f, 1f, 1f);

    private DebugMover mover;
    private DriftChargeVisual chargeVisual;
    private Transform[] rearWheels;
    private Bounds bodyBounds;
    private bool hasBodyBounds;
    private float nextArcTime;
    private bool wasFull;
    private Color fullColor;
    private int playerIndex = -1;
    private VManager volumeManager;
    private RaceSpeedVisualController raceVisuals;

    private void Awake()
    {
        mover = GetComponent<DebugMover>();
        chargeVisual = GetComponent<DriftChargeVisual>();
        fullColor = fallbackColor;
    }

    private void Start()
    {
        // アイテムのシールドなど、後から付く大きな演出物を含めないよう、生成直後に形を測っておきます。
        EnsureGeometry();
    }

    /// <summary>解放時の画面の光と揺れを、この車を操作しているプレイヤーの画面だけに送ります。</summary>
    public void Configure(int index, VManager manager, RaceSpeedVisualController visuals)
    {
        playerIndex = index;
        volumeManager = manager;
        raceVisuals = visuals;
    }

    private void Update()
    {
        bool active = mover != null && mover.isActiveAndEnabled &&
            Gmanager.Control != null && Gmanager.Control.IsDrivingEnabled;
        if (!active)
        {
            wasFull = false;
            return;
        }

        bool isFull = mover.IsDriftChargeFull;
        if (isFull)
        {
            if (!wasFull) nextArcTime = 0f;
            if (chargeVisual != null) fullColor = chargeVisual.CurrentTierColor;
            UpdateArcs();
        }
        else if (wasFull && mover.IsDriftBoosting)
        {
            // 解放で加速が始まったときだけを拾い、リスポーンやスピンでチャージを失った場合は光らせません。
            PlayRelease();
        }

        wasFull = isFull;
    }

    private void UpdateArcs()
    {
        if (Time.time < nextArcTime) return;
        float minimum = Mathf.Max(0.01f, Mathf.Min(arcIntervalRange.x, arcIntervalRange.y));
        float maximum = Mathf.Max(minimum, arcIntervalRange.y);
        nextArcTime = Time.time + Random.Range(minimum, maximum);
        EmitArc(1f);
    }

    private void EmitArc(float scale)
    {
        EnsureGeometry();
        Vector3 start = rearWheels.Length > 0 && Random.value < wheelArcChance
            ? transform.InverseTransformPoint(rearWheels[Random.Range(0, rearWheels.Length)].position)
            : RandomBodySurfacePoint();
        float length = Random.Range(arcLengthRange.x, Mathf.Max(arcLengthRange.x, arcLengthRange.y)) * scale;
        // 終点も車体の表面へ戻し、放電が車体を這うように見せます。
        Vector3 end = ProjectToBodySurface(start + Random.onUnitSphere * length);

        LightningEffects.Strike(
            new LightningAnchor(transform, start),
            new LightningAnchor(transform, end),
            new LightningStyle
            {
                color = fullColor,
                width = arcWidth * scale,
                jaggedness = 0.35f,
                subdivisions = 3,
                branchCount = Random.value < 0.3f ? 1 : 0,
                duration = arcDuration * Random.Range(0.7f, 1.3f),
                regenerateInterval = 0.03f
            });
    }

    private void PlayRelease()
    {
        EnsureGeometry();
        Vector3 center = bodyBounds.center;
        Vector3 extents = bodyBounds.extents;
        LightningStyle style = new LightningStyle
        {
            color = fullColor,
            width = releaseWidth,
            jaggedness = 0.22f,
            subdivisions = 5,
            branchCount = 2,
            duration = releaseDuration,
            regenerateInterval = 0.05f,
            lightIntensity = releaseLightIntensity,
            lightRange = 12f,
            lightPlacement = 0.35f
        };

        // 車体の後ろから両脇を抜けて前方へ走らせ、車が稲妻に撃ち出されたように見せます。
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 start = new Vector3(
                center.x + side * extents.x * 0.9f,
                center.y + extents.y * 0.3f,
                center.z - extents.z - 1f);
            Vector3 end = new Vector3(
                center.x + side * extents.x * 0.5f,
                center.y,
                center.z + extents.z + releaseReach);
            LightningEffects.Strike(new LightningAnchor(transform, start), new LightningAnchor(transform, end), style);
        }

        for (int index = 0; index < releaseArcBurst; index++) EmitArc(1.4f);

        Color flashTint = Color.Lerp(fullColor, Color.white, 0.6f);
        if (volumeManager != null && playerIndex >= 0)
            volumeManager.FlashLightning(playerIndex, releaseScreenFlash, flashTint);
        if (raceVisuals != null && releaseCameraImpact > 0f) raceVisuals.AddImpact(releaseCameraImpact);
        LightningEffects.PlayCrack(releaseSoundVolume);
    }

    private void EnsureGeometry()
    {
        if (rearWheels == null) CacheRearWheels();
        if (!hasBodyBounds) CacheBodyBounds();
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

    // 車体の見た目を包む箱を、車のローカル座標で求めます。演出用の描画物は含めません。
    private void CacheBodyBounds()
    {
        bool found = false;
        Bounds bounds = new Bounds();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer) continue;
            Bounds local = renderer.localBounds;
            Vector3 min = local.min;
            Vector3 max = local.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 carPoint = transform.InverseTransformPoint(renderer.transform.TransformPoint(point));
                if (!found)
                {
                    bounds = new Bounds(carPoint, Vector3.zero);
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(carPoint);
                }
            }
        }

        if (!found || bounds.size.sqrMagnitude < 0.01f)
        {
            bounds = new Bounds(new Vector3(0f, 0.6f, 0f), new Vector3(1.8f, 1.1f, 4.2f));
        }

        // 塗装にめり込まないよう、少しだけ外側を這わせます。
        bounds.Expand(0.08f);
        bodyBounds = bounds;
        hasBodyBounds = true;
    }

    private Vector3 RandomBodySurfacePoint()
    {
        Vector3 extents = bodyBounds.extents;
        Vector3 point = new Vector3(
            Random.Range(-extents.x, extents.x),
            Random.Range(-extents.y * 0.2f, extents.y),
            Random.Range(-extents.z, extents.z));
        return ProjectToBodySurface(bodyBounds.center + point);
    }

    private Vector3 ProjectToBodySurface(Vector3 point)
    {
        Vector3 extents = bodyBounds.extents;
        Vector3 offset = point - bodyBounds.center;
        offset = new Vector3(
            Mathf.Clamp(offset.x, -extents.x, extents.x),
            Mathf.Clamp(offset.y, -extents.y, extents.y),
            Mathf.Clamp(offset.z, -extents.z, extents.z));

        // 箱の面のうち、最も近い面へ押し出します。
        float x = extents.x > 0f ? Mathf.Abs(offset.x) / extents.x : 0f;
        float y = extents.y > 0f ? Mathf.Abs(offset.y) / extents.y : 0f;
        float z = extents.z > 0f ? Mathf.Abs(offset.z) / extents.z : 0f;
        if (x >= y && x >= z) offset.x = Mathf.Sign(offset.x == 0f ? 1f : offset.x) * extents.x;
        else if (y >= z) offset.y = offset.y < 0f ? -extents.y : extents.y;
        else offset.z = Mathf.Sign(offset.z == 0f ? 1f : offset.z) * extents.z;
        return bodyBounds.center + offset;
    }

    private void OnDisable()
    {
        wasFull = false;
    }
}
