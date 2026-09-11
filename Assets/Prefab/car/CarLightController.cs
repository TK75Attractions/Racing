using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>車両のヘッドライトとテールランプ。点灯はレースのシチュエーション、
/// ブレーキランプはペダル入力に連動します。ランプは実行時に自動生成されます。</summary>
[DisallowMultipleComponent]
public sealed class CarLightController : MonoBehaviour
{
    public enum LampMode
    {
        [InspectorName("自動（シチュエーション連動）")] Auto,
        [InspectorName("常時点灯")] AlwaysOn,
        [InspectorName("常時消灯")] AlwaysOff
    }

    public enum AutoCondition
    {
        [InspectorName("夜のみ")] NightOnly,
        [InspectorName("夜と雨")] NightAndRain
    }

    public enum LensSource
    {
        [InspectorName("自動（モデルのランプがあればそれを使う）")] Auto,
        [InspectorName("モデルのマテリアルを発光")] ModelMaterials,
        [InspectorName("生成した板を発光")] GeneratedQuads,
        [InspectorName("両方")] Both
    }

    [Header("点灯条件")]
    [SerializeField] private LampMode mode = LampMode.Auto;
    [SerializeField] private AutoCondition autoCondition = AutoCondition.NightAndRain;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.15f;

    [Header("光り方")]
    [SerializeField] private LensSource lensSource = LensSource.Auto;
    [Tooltip("ヘッドライトとして発光させるマテリアル名。部分一致・大文字小文字は無視します。")]
    [SerializeField] private string[] headlightMaterialNames = { "frontlight", "headlight" };
    [Tooltip("テールランプとして発光させるマテリアル名。部分一致・大文字小文字は無視します。")]
    [SerializeField] private string[] taillightMaterialNames = { "rearlight", "back_light", "taillight" };
    [Tooltip("モデルのマテリアルを発光させるときの明るさ倍率。")]
    [SerializeField, Min(0f)] private float emissionScale = 1.2f;

    [Header("ヘッドライト")]
    [SerializeField] private Color headlightColor = new Color(1f, 0.96f, 0.86f);
    [SerializeField, Min(0f)] private float headlightIntensity = 12f;
    [SerializeField, Min(1f)] private float headlightRange = 45f;
    [SerializeField, Range(10f, 120f)] private float headlightSpotAngle = 65f;
    [Tooltip("光軸を水平から何度下げるか。")]
    [SerializeField, Range(0f, 20f)] private float headlightDownAngle = 6f;
    [SerializeField, Range(0f, 6f)] private float headlightGlow = 2.4f;
    [Tooltip("影を落とすと見栄えは上がりますが、URPの追加ライト影のコストがかかります。")]
    [SerializeField] private bool headlightShadows;

    [Header("テールランプ")]
    [SerializeField] private Color taillightColor = new Color(1f, 0.07f, 0.04f);
    [SerializeField, Range(0f, 4f)] private float taillightGlow = 0.9f;
    [SerializeField, Range(0f, 8f)] private float brakeGlow = 3.4f;
    [Tooltip("ブレーキと判定するペダル入力（負方向）のしきい値。")]
    [SerializeField, Range(0.01f, 1f)] private float brakePedalThreshold = 0.15f;
    [Tooltip("テールランプを実光源としても点灯します。1オブジェクトが受けられる追加ライト数の上限に注意してください。")]
    [SerializeField] private bool taillightIllumination;
    [SerializeField, Min(0f)] private float taillightLightIntensity = 3f;
    [SerializeField, Min(0.5f)] private float taillightLightRange = 8f;

    [Header("配置（未指定なら車体サイズから自動計算）")]
    [Tooltip("左右2つのヘッドライト位置。前方が +Z になる向きで配置してください。")]
    [SerializeField] private Transform[] headlightAnchors = new Transform[0];
    [Tooltip("左右2つのテールランプ位置。前方が +Z になる向きで配置してください。")]
    [SerializeField] private Transform[] taillightAnchors = new Transform[0];
    [SerializeField, Range(0f, 1f)] private float lampSideRatio = 0.66f;
    [SerializeField, Range(0f, 1f)] private float headlightHeightRatio = 0.45f;
    [SerializeField, Range(0f, 1f)] private float taillightHeightRatio = 0.5f;
    [SerializeField, Min(0.01f)] private float lampSize = 0.34f;
    [Tooltip("車体表面へのめり込みを避けるための前後方向の押し出し量。")]
    [SerializeField, Min(0f)] private float lampSurfaceOffset = 0.03f;

    [Header("Runtime Monitor")]
    [SerializeField] private bool lampsOn;
    [SerializeField] private bool braking;
    [SerializeField] private float lampBlend;

    private const string EmissionKeyword = "_EMISSION";
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private readonly List<Light> headlights = new List<Light>();
    private readonly List<Light> taillights = new List<Light>();
    private readonly List<Material> headlightEmissives = new List<Material>();
    private readonly List<Material> taillightEmissives = new List<Material>();
    private readonly List<Material> ownedMaterials = new List<Material>();
    private DebugMover mover;
    private Material headlightMaterial, taillightMaterial;
    private Mesh lampMesh;
    private Transform lampRoot;
    private bool built;

    public bool LampsOn => lampsOn;
    public bool IsBraking => braking;

    private void Awake()
    {
        mover = GetComponent<DebugMover>();
    }

    private void OnEnable()
    {
        if (!built) built = Build();
    }

    private void Update()
    {
        if (!built) return;

        lampsOn = ShouldLampsBeOn();
        lampBlend = Mathf.MoveTowards(lampBlend, lampsOn ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
        braking = IsBrakePedalPressed();

        foreach (Light light in headlights)
        {
            if (light == null) continue;
            light.enabled = lampBlend > 0.001f;
            light.intensity = headlightIntensity * lampBlend;
        }

        float headGlow = headlightGlow * lampBlend;
        // ブレーキランプは昼夜を問わず点灯し、消灯時のテール点灯だけをシチュエーションに従わせます。
        float tailGlow = Mathf.Max(taillightGlow * lampBlend, braking ? brakeGlow : 0f);
        if (headlightMaterial != null) headlightMaterial.SetFloat(IntensityId, headGlow);
        if (taillightMaterial != null) taillightMaterial.SetFloat(IntensityId, tailGlow);
        ApplyEmission(headlightEmissives, headlightColor, headGlow);
        ApplyEmission(taillightEmissives, taillightColor, tailGlow);

        float tailRatio = brakeGlow > 0f ? Mathf.Clamp01(tailGlow / brakeGlow) : 0f;
        foreach (Light light in taillights)
        {
            if (light == null) continue;
            light.enabled = tailRatio > 0.001f;
            light.intensity = taillightLightIntensity * tailRatio;
        }
    }

    private bool ShouldLampsBeOn()
    {
        if (mode == LampMode.AlwaysOn) return true;
        if (mode == LampMode.AlwaysOff) return false;

        RaceEnvironmentController environment = RaceEnvironmentController.Active;
        if (environment == null) return false;
        if (environment.CurrentTimeOfDay == RaceEnvironmentController.TimeOfDay.Night) return true;
        return autoCondition == AutoCondition.NightAndRain &&
            environment.CurrentWeather == RaceEnvironmentController.Weather.Rainy;
    }

    private bool IsBrakePedalPressed()
    {
        if (mover == null || Gmanager.Control == null || !Gmanager.Control.IsDrivingEnabled) return false;
        return mover.PedalInput < -brakePedalThreshold;
    }

    private bool Build()
    {
        bool hasBounds = TryGetLocalBounds(out Bounds bounds);
        if (!hasBounds && (headlightAnchors.Length < 2 || taillightAnchors.Length < 2))
        {
            Debug.LogWarning("CarLightController: ランプを配置できる Renderer もアンカーも見つかりませんでした。", this);
            enabled = false;
            return false;
        }

        if (lensSource != LensSource.GeneratedQuads) CollectEmissiveMaterials();
        bool foundOnModel = headlightEmissives.Count > 0 || taillightEmissives.Count > 0;
        bool useQuads = lensSource == LensSource.GeneratedQuads || lensSource == LensSource.Both ||
            (lensSource == LensSource.Auto && !foundOnModel);

        if (useQuads && !CreateLensMaterials())
        {
            enabled = false;
            return false;
        }

        GameObject root = new GameObject("Car Lights");
        root.layer = gameObject.layer;
        root.transform.SetParent(transform, false);
        lampRoot = root.transform;

        CreateLamps(bounds, true, useQuads);
        CreateLamps(bounds, false, useQuads);
        return true;
    }

    private bool CreateLensMaterials()
    {
        // Resources参照により、ビルド時にも専用シェーダーが含まれます。
        Shader shader = Resources.Load<Shader>("CarLamp");
        if (shader == null)
        {
            Debug.LogError("CarLamp shader was not found.", this);
            return false;
        }

        headlightMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        headlightMaterial.SetColor(ColorId, headlightColor);
        headlightMaterial.SetFloat(IntensityId, 0f);
        taillightMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        taillightMaterial.SetColor(ColorId, taillightColor);
        taillightMaterial.SetFloat(IntensityId, 0f);
        lampMesh = CreateLampMesh();
        return true;
    }

    /// <summary>車体モデルのランプ用マテリアルを、この車専用のインスタンスに差し替えて発光可能にします。</summary>
    private void CollectEmissiveMaterials()
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            Material[] shared = renderer.sharedMaterials;
            bool matched = false;
            for (int slot = 0; slot < shared.Length && !matched; slot++)
                matched = shared[slot] != null &&
                    (MatchesAnyName(shared[slot].name, headlightMaterialNames) ||
                     MatchesAnyName(shared[slot].name, taillightMaterialNames));
            if (!matched) continue;

            // renderer.materials は全スロットを複製するため、共有マテリアル（プロジェクトの資産）は変更されません。
            Material[] instances = renderer.materials;
            for (int slot = 0; slot < instances.Length; slot++)
            {
                if (instances[slot] == null) continue;
                ownedMaterials.Add(instances[slot]);
                if (shared[slot] == null) continue;

                List<Material> target =
                    MatchesAnyName(shared[slot].name, headlightMaterialNames) ? headlightEmissives :
                    MatchesAnyName(shared[slot].name, taillightMaterialNames) ? taillightEmissives : null;
                if (target == null) continue;

                instances[slot].EnableKeyword(EmissionKeyword);
                instances[slot].globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                instances[slot].SetColor(EmissionColorId, Color.black);
                target.Add(instances[slot]);
            }
        }
    }

    private void ApplyEmission(List<Material> materials, Color color, float glow)
    {
        if (materials.Count == 0) return;
        Color emission = color * (glow * emissionScale);
        foreach (Material material in materials)
        {
            if (material != null) material.SetColor(EmissionColorId, emission);
        }
    }

    private static bool MatchesAnyName(string materialName, string[] keys)
    {
        if (string.IsNullOrEmpty(materialName) || keys == null) return false;
        foreach (string key in keys)
        {
            if (!string.IsNullOrEmpty(key) &&
                materialName.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private void CreateLamps(Bounds bounds, bool front, bool useQuads)
    {
        Transform[] anchors = front ? headlightAnchors : taillightAnchors;
        float heightRatio = front ? headlightHeightRatio : taillightHeightRatio;
        float depth = front ? bounds.max.z + lampSurfaceOffset : bounds.min.z - lampSurfaceOffset;
        float height = bounds.min.y + bounds.size.y * heightRatio;
        Quaternion facing = front ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
        string label = front ? "Headlight" : "Taillight";

        for (int index = 0; index < 2; index++)
        {
            Transform anchor = anchors != null && index < anchors.Length ? anchors[index] : null;
            Vector3 position;
            Quaternion rotation;
            if (anchor != null)
            {
                position = transform.InverseTransformPoint(anchor.position);
                rotation = Quaternion.Inverse(transform.rotation) * anchor.rotation;
            }
            else
            {
                float side = index == 0 ? -1f : 1f;
                position = new Vector3(bounds.center.x + bounds.extents.x * lampSideRatio * side, height, depth);
                rotation = facing;
            }

            if (useQuads) CreateLampQuad(label + " Lens " + (index + 1), position, rotation, front);
            if (front) CreateHeadlight(label + " " + (index + 1), position, rotation);
            else if (taillightIllumination) CreateTaillight(label + " " + (index + 1), position, rotation);
        }
    }

    private void CreateLampQuad(string label, Vector3 position, Quaternion rotation, bool front)
    {
        GameObject lens = new GameObject(label);
        lens.layer = gameObject.layer;
        lens.transform.SetParent(lampRoot, false);
        lens.transform.localPosition = position;
        lens.transform.localRotation = rotation;
        lens.transform.localScale = Vector3.one * lampSize;
        lens.AddComponent<MeshFilter>().sharedMesh = lampMesh;
        MeshRenderer renderer = lens.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = front ? headlightMaterial : taillightMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private void CreateHeadlight(string label, Vector3 position, Quaternion rotation)
    {
        Light light = CreateLight(label, position, rotation * Quaternion.Euler(headlightDownAngle, 0f, 0f));
        light.type = LightType.Spot;
        light.color = headlightColor;
        light.range = headlightRange;
        light.spotAngle = headlightSpotAngle;
        light.innerSpotAngle = headlightSpotAngle * 0.45f;
        light.shadows = headlightShadows ? LightShadows.Hard : LightShadows.None;
        headlights.Add(light);
    }

    private void CreateTaillight(string label, Vector3 position, Quaternion rotation)
    {
        Light light = CreateLight(label, position, rotation);
        light.type = LightType.Point;
        light.color = taillightColor;
        light.range = taillightLightRange;
        light.shadows = LightShadows.None;
        taillights.Add(light);
    }

    private Light CreateLight(string label, Vector3 position, Quaternion rotation)
    {
        GameObject holder = new GameObject(label);
        holder.layer = gameObject.layer;
        holder.transform.SetParent(lampRoot, false);
        holder.transform.localPosition = position;
        holder.transform.localRotation = rotation;
        Light light = holder.AddComponent<Light>();
        light.useColorTemperature = false;
        light.intensity = 0f;
        light.enabled = false;
        return light;
    }

    /// <summary>車体の描画範囲を、車の原点を基準としたローカル空間で求めます。</summary>
    private bool TryGetLocalBounds(out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;

            Bounds local = renderer.localBounds;
            Matrix4x4 matrix = worldToLocal * renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z));
                if (found)
                {
                    bounds.Encapsulate(point);
                }
                else
                {
                    bounds = new Bounds(point, Vector3.zero);
                    found = true;
                }
            }
        }

        return found;
    }

    private static Mesh CreateLampMesh()
    {
        Mesh mesh = new Mesh { name = "Car Lamp Lens", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        return mesh;
    }

    private void OnDestroy()
    {
        foreach (Material material in ownedMaterials) Release(material);
        ownedMaterials.Clear();
        headlightEmissives.Clear();
        taillightEmissives.Clear();
        Release(headlightMaterial);
        Release(taillightMaterial);
        Release(lampMesh);
        headlightMaterial = taillightMaterial = null;
        lampMesh = null;
    }

    private static void Release(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
