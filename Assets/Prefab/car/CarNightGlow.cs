using UnityEngine;
using UnityEngine.Rendering;

/// <summary>夜・雨の車体を見やすくする、車両ローカルの発光板と人間用の補助光。</summary>
[DisallowMultipleComponent]
public sealed class CarNightGlow : MonoBehaviour
{
    [SerializeField, Min(0f)] private float glowIntensity = 1.8f;
    [SerializeField, Min(0f)] private float bodyLightIntensity = 1.6f;
    [SerializeField, Range(4f, 6f)] private float bodyLightRange = 5f;
    [SerializeField, Range(0f, 1f)] private float airborneGlow = 0.15f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private TireForce[] tires;
    private Transform glowRoot;
    private MeshRenderer glowRenderer;
    private Mesh glowMesh;
    private Material glowMaterial;
    private static Texture2D gradient;
    private Light bodyLight;
    private Bounds carBounds;
    private Color glowColor;
    private float environmentBlend;
    private float groundBlend;
    private bool lampShader;
    private bool configured;

    /// <param name="participantIndex">0 = P1、1 = P2、2以降 = CPU。</param>
    public void Configure(int participantIndex)
    {
        glowColor = participantIndex == 0 ? new Color(0.1f, 0.9f, 1f) :
            participantIndex == 1 ? new Color(1f, 0.25f, 0.65f) : new Color(0.8f, 0.75f, 1f);
        if (!configured)
        {
            tires = GetComponentsInChildren<TireForce>(true);
            carBounds = GetCarBounds();
            CreateGlow();
            configured = true;
        }

        if (participantIndex >= 0 && participantIndex < 2)
        {
            if (bodyLight == null) CreateBodyLight();
        }
        else if (bodyLight != null)
        {
            bodyLight.enabled = false;
            Destroy(bodyLight.gameObject);
            bodyLight = null;
        }
    }

    private void LateUpdate()
    {
        if (!configured) return;
        RaceEnvironmentController environment = RaceEnvironmentController.Active;
        bool active = environment != null &&
            (environment.CurrentTimeOfDay == RaceEnvironmentController.TimeOfDay.Night ||
             environment.CurrentWeather == RaceEnvironmentController.Weather.Rainy);
        environmentBlend = Mathf.MoveTowards(environmentBlend, active ? 1f : 0f, Time.deltaTime * 5f);

        int contacts = 0;
        float groundHeight = 0f;
        foreach (TireForce tire in tires)
        {
            if (tire == null || !tire.IsGrounded) continue;
            groundHeight += transform.InverseTransformPoint(tire.GroundHit.point).y;
            contacts++;
        }
        groundBlend = Mathf.MoveTowards(groundBlend, contacts > 0 ? 1f : airborneGlow, Time.deltaTime * 8f);
        if (glowRoot != null && contacts > 0)
        {
            // ローカルの下方向を維持し、壁面・天井でも車の下に追従させます。
            Vector3 position = glowRoot.localPosition;
            position.y = groundHeight / contacts + 0.035f;
            glowRoot.localPosition = position;
        }

        float strength = environmentBlend * groundBlend * glowIntensity;
        if (glowRenderer != null)
        {
            glowRenderer.enabled = strength > 0.001f;
            if (lampShader)
            {
                glowMaterial.SetColor(ColorId, glowColor);
                glowMaterial.SetFloat(IntensityId, strength);
            }
            else
            {
                Color hdr = glowColor * strength;
                hdr.a = 1f;
                glowMaterial.SetColor(BaseColorId, hdr);
            }
        }
        if (bodyLight != null)
        {
            bodyLight.enabled = environmentBlend > 0.001f;
            bodyLight.intensity = bodyLightIntensity * environmentBlend;
        }
    }

    private void CreateGlow()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        // 実行時専用のUnlit variantがビルドで除去されても、Resourcesの加算シェーダーで表示。
        lampShader = shader == null || !shader.isSupported;
        if (lampShader) shader = Resources.Load<Shader>("CarLamp");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning("CarNightGlow: no supported glow shader was found.", this);
            return;
        }
        PrepareSharedResources();
        glowMaterial = new Material(shader) { name = "Car Night Glow", hideFlags = HideFlags.HideAndDontSave };
        if (lampShader) glowMaterial.SetFloat(IntensityId, 0f);
        else
        {
            glowMaterial.SetTexture("_BaseMap", gradient);
            glowMaterial.SetColor(BaseColorId, Color.clear);
            glowMaterial.SetFloat("_Surface", 1f);
            glowMaterial.SetFloat("_Blend", 2f);
            glowMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            glowMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
            glowMaterial.SetFloat("_SrcBlendAlpha", (float)BlendMode.Zero);
            glowMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            glowMaterial.SetFloat("_ZWrite", 0f);
            glowMaterial.SetFloat("_Cull", (float)CullMode.Off);
            // Unlit's opaque variant also samples texture alpha. Keep it to avoid
            // depending on a runtime-only transparent keyword surviving stripping.
            glowMaterial.SetOverrideTag("RenderType", "Transparent");
            glowMaterial.SetShaderPassEnabled("DepthOnly", false);
            glowMaterial.SetShaderPassEnabled("DepthNormalsOnly", false);
            glowMaterial.SetShaderPassEnabled("ShadowCaster", false);
            glowMaterial.SetShaderPassEnabled("MotionVectors", false);
        }
        glowMaterial.renderQueue = (int)RenderQueue.Transparent;

        GameObject plate = new GameObject("Car Night Underglow");
        plate.layer = gameObject.layer;
        glowRoot = plate.transform;
        glowRoot.SetParent(transform, false);
        glowRoot.localPosition = new Vector3(carBounds.center.x, carBounds.min.y + 0.035f, carBounds.center.z);
        glowRoot.localScale = new Vector3(carBounds.size.x * 1.45f, 1f, carBounds.size.z * 1.15f);
        glowMesh = new Mesh { name = "Car Night Glow Quad", hideFlags = HideFlags.HideAndDontSave };
        glowMesh.vertices = new[] { new Vector3(-.5f, 0f, -.5f), new Vector3(.5f, 0f, -.5f),
            new Vector3(-.5f, 0f, .5f), new Vector3(.5f, 0f, .5f) };
        glowMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        glowMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        glowMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        glowMesh.RecalculateBounds();
        plate.AddComponent<MeshFilter>().sharedMesh = glowMesh;
        glowRenderer = plate.AddComponent<MeshRenderer>();
        glowRenderer.sharedMaterial = glowMaterial;
        glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        glowRenderer.receiveShadows = false;
        glowRenderer.lightProbeUsage = LightProbeUsage.Off;
        glowRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        glowRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        glowRenderer.enabled = false;
    }

    /// <summary>All cars use the same immutable mask, prepared before the title.</summary>
    public static void PrepareSharedResources()
    {
        if (gradient == null) gradient = CreateGradient();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSharedResources()
    {
        // Also handles entering Play Mode with domain reload disabled.
        if (gradient != null) Destroy(gradient);
        gradient = null;
    }

    private static Texture2D CreateGradient()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        { name = "Car Night Glow Gradient", wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 offset = new Vector2(x, y) * (2f / (size - 1)) - Vector2.one;
            float fade = Mathf.Clamp01(1f - offset.sqrMagnitude);
            pixels[y * size + x] = new Color(1f, 1f, 1f, fade * fade);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void CreateBodyLight()
    {
        GameObject lamp = new GameObject("Car Night Body Light");
        lamp.layer = gameObject.layer;
        lamp.transform.SetParent(transform, false);
        lamp.transform.localPosition = new Vector3(carBounds.center.x, carBounds.max.y + 0.6f, carBounds.min.z - 0.65f);
        bodyLight = lamp.AddComponent<Light>();
        bodyLight.type = LightType.Point;
        bodyLight.color = new Color(0.82f, 0.9f, 1f);
        bodyLight.range = bodyLightRange;
        bodyLight.shadows = LightShadows.None;
        bodyLight.bounceIntensity = 0f;
        bodyLight.intensity = 0f;
        bodyLight.enabled = false;
    }

    private Bounds GetCarBounds()
    {
        Bounds bounds = new Bounds();
        bool found = false;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            Bounds local = renderer.localBounds;
            Matrix4x4 matrix = transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = matrix.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z));
                if (found) bounds.Encapsulate(point);
                else { bounds = new Bounds(point, Vector3.zero); found = true; }
            }
        }
        return found ? bounds : new Bounds(new Vector3(0f, 0.6f, 0f), new Vector3(2f, 1.2f, 4f));
    }

    private void OnDisable()
    {
        if (glowRenderer != null) glowRenderer.enabled = false;
        if (bodyLight != null) bodyLight.enabled = false;
    }

    private void OnDestroy()
    {
        if (glowRoot != null) Destroy(glowRoot.gameObject);
        if (bodyLight != null) Destroy(bodyLight.gameObject);
        if (glowMaterial != null) Destroy(glowMaterial);
        if (glowMesh != null) Destroy(glowMesh);
    }
}
