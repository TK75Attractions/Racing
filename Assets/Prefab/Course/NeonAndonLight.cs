using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>コースの道を照らすネオン行灯。天板・台・角柱・桟という行灯の骨格は残しつつ、
/// 断面を角丸（スーパー楕円）にして障子紙をネオンのグラデーションに置き換えたモダンな解釈です。
/// メッシュ・マテリアル・ライトはすべて実行時に生成するので、置くだけで使えます。
/// 原点は台の底で、宙に浮かせる高さはそのまま Transform の Y になります。</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class NeonAndonLight : MonoBehaviour
{
    public enum LampMode
    {
        [InspectorName("自動（夜と雨で路面を照らす）")] Auto,
        [InspectorName("常時点灯")] AlwaysOn,
        [InspectorName("常時消灯")] AlwaysOff
    }

    public enum LampShape
    {
        [InspectorName("スポット（真下を照らす）")] Spot,
        [InspectorName("ポイント（周囲を照らす）")] Point
    }

    [Header("大きさ")]
    [SerializeField, Min(0.1f)] private float height = 1.75f;
    [SerializeField, Min(0.1f)] private float width = 1.05f;

    [Header("形")]
    [Tooltip("断面の分割数。大きいほど角丸が滑らかになります。")]
    [SerializeField, Range(12, 96)] private int sideSegments = 48;
    [Tooltip("断面の角ばり具合。2で円、4で角丸の四角、大きいほど四角に近づきます。")]
    [SerializeField, Range(2f, 8f)] private float cornerSharpness = 4f;
    [Tooltip("四隅の柱。行灯の枠らしさが出ます。")]
    [SerializeField] private bool cornerPosts = true;
    [Tooltip("下寄りに入る横桟。行灯らしさを1本だけ残しています。")]
    [SerializeField] private bool midRail = true;

    [Header("配色（ネオン）")]
    [SerializeField, ColorUsage(false, true)] private Color paperBottomColor = new Color(1f, 0.18f, 0.55f);
    [SerializeField, ColorUsage(false, true)] private Color paperTopColor = new Color(0.12f, 0.85f, 1f);
    [SerializeField, ColorUsage(false, true)] private Color neonTubeColor = new Color(0.42f, 1f, 1f);
    [SerializeField] private Color frameColor = new Color(0.07f, 0.075f, 0.095f);
    [Tooltip("1を超えると Bloom がのって、暗いシーンでネオンらしく滲みます。")]
    [SerializeField, Range(0f, 8f)] private float paperGlow = 1.7f;
    [SerializeField, Range(0f, 8f)] private float neonGlow = 3.2f;
    [Tooltip("紙の内側を光がゆっくり上る演出の強さ。0で止まります。")]
    [SerializeField, Range(0f, 1f)] private float lightFlow = 0.15f;
    [Tooltip("昼のときの明るさ倍率。消えはせず、控えめに光り続けます。")]
    [SerializeField, Range(0f, 1f)] private float daytimeGlowScale = 0.55f;

    [Header("浮遊感")]
    [Tooltip("台の下に浮かぶ輪。宙に浮いていることが一目で分かるようになります。")]
    [SerializeField] private bool hoverRing = true;
    [Tooltip("上下にゆっくり漂う幅（メートル）。再生中のみ動きます。")]
    [SerializeField, Range(0f, 0.5f)] private float bobHeight = 0.07f;
    [SerializeField, Range(0.5f, 20f)] private float bobSeconds = 5f;
    [SerializeField, Range(-90f, 90f)] private float spinSpeed = 5f;
    [SerializeField, Range(-180f, 180f)] private float hoverRingSpinSpeed = -30f;

    [Header("路面を照らすライト")]
    [SerializeField] private LampMode mode = LampMode.Auto;
    [SerializeField] private LampShape lampShape = LampShape.Spot;
    [SerializeField, Min(0f)] private float lightIntensity = 14f;
    [SerializeField, Min(0.5f)] private float lightRange = 22f;
    [SerializeField, Range(20f, 170f)] private float spotAngle = 110f;
    [Tooltip("空にすると2色の中間色を使います。路面の色を決めたいときだけ指定してください。")]
    [SerializeField] private bool overrideLightColor;
    [SerializeField] private Color lightColor = new Color(1f, 0.72f, 0.86f);
    [Tooltip("影を落とすと見栄えは上がりますが、URPの追加ライト影のコストがかかります。")]
    [SerializeField] private bool castShadows;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.4f;

    [Header("Runtime Monitor")]
    [SerializeField] private float lampBlend = 1f;

    // 形状は「高さ = 全高の割合」「半径 = 幅の半分に対する割合」で持ち、大きさを変えても比率が崩れないようにします。
    private const float BasePlateBottom = 0.040f;
    private const float BasePlateTop = 0.118f;
    private const float PaperBottom = 0.115f;
    private const float PaperTop = 0.895f;
    private const float PaperBottomRadius = 0.74f;
    private const float PaperTopRadius = 0.615f;
    private const float PaperBulge = 0.022f;
    private const float UnderPanelY = 0.030f;

    private static readonly int ColorBottomId = Shader.PropertyToID("_ColorBottom");
    private static readonly int ColorTopId = Shader.PropertyToID("_ColorTop");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int FlowId = Shader.PropertyToID("_Flow");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int RimBoostId = Shader.PropertyToID("_RimBoost");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

    private const string VisualRootName = "Neon Andon (generated)";

    private readonly List<Object> owned = new List<Object>();
    private Transform visualRoot;
    private Transform hoverRingRoot;
    private Material paperMaterial;
    private Material neonMaterial;
    private Material frameMaterial;
    private Light roadLight;
    private float appliedBlend = -1f;
    private bool dirty;
    private bool built;

    public bool LampOn => lampBlend > 0.001f;

    private void OnEnable()
    {
        if (!built) built = Build();
    }

    private void OnDisable()
    {
        Release();
    }

    // OnValidate はメインスレッド外でも走るため、生成し直しは Update に任せます。
    private void OnValidate()
    {
        sideSegments = Mathf.Max(12, sideSegments - sideSegments % 4);
        dirty = true;
    }

    [ContextMenu("行灯を作り直す")]
    private void Rebuild()
    {
        Release();
        built = Build();
    }

    private void Update()
    {
        if (dirty)
        {
            dirty = false;
            Rebuild();
        }
        if (!built) return;

        float target = ShouldIlluminate() ? 1f : 0f;
        lampBlend = Application.isPlaying
            ? Mathf.MoveTowards(lampBlend, target, Time.deltaTime / Mathf.Max(0.01f, fadeSeconds))
            : target;

        if (!Mathf.Approximately(appliedBlend, lampBlend))
        {
            ApplyGlow();
            appliedBlend = lampBlend;
        }

        // 漂う動きは再生中だけにして、編集中にシーンが更新され続けないようにします。
        if (!Application.isPlaying || visualRoot == null) return;

        float time = Time.time;
        float bob = bobHeight * Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(0.01f, bobSeconds));
        visualRoot.localPosition = new Vector3(0f, bob, 0f);
        visualRoot.localRotation = Quaternion.Euler(0f, time * spinSpeed, 0f);
        if (hoverRingRoot != null)
            hoverRingRoot.localRotation = Quaternion.Euler(0f, time * hoverRingSpinSpeed, 0f);
    }

    /// <summary>路面を照らすかどうか。環境コントローラーが無いシーンでは常に点灯します。</summary>
    private bool ShouldIlluminate()
    {
        if (mode == LampMode.AlwaysOn) return true;
        if (mode == LampMode.AlwaysOff) return false;

        RaceEnvironmentController environment = RaceEnvironmentController.Active;
        if (environment == null) return true;
        return environment.CurrentTimeOfDay == RaceEnvironmentController.TimeOfDay.Night ||
            environment.CurrentWeather == RaceEnvironmentController.Weather.Rainy;
    }

    private bool Build()
    {
        // ドメインリロードなどで参照が切れた生成物が残っていたら片付けます。
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name == VisualRootName) DestroyGenerated(child.gameObject);
        }

        Shader neonShader = Resources.Load<Shader>("NeonAndon");
        if (neonShader == null)
        {
            Debug.LogError("NeonAndon shader was not found.", this);
            enabled = false;
            return false;
        }

        GameObject root = new GameObject(VisualRootName) { hideFlags = HideFlags.DontSave };
        root.layer = gameObject.layer;
        root.transform.SetParent(transform, false);
        visualRoot = root.transform;

        paperMaterial = Track(new Material(neonShader) { name = "Andon Paper", hideFlags = HideFlags.HideAndDontSave });
        neonMaterial = Track(new Material(neonShader) { name = "Andon Neon", hideFlags = HideFlags.HideAndDontSave });
        frameMaterial = Track(CreateFrameMaterial(neonShader));
        ApplyColors();
        ApplyGlow();

        AddRenderer("Frame", BuildFrameMesh(), frameMaterial, true);
        AddRenderer("Paper", BuildPaperMesh(), paperMaterial, false);
        AddRenderer("Neon", BuildNeonMesh(), neonMaterial, false);

        if (hoverRing)
        {
            // 輪だけ独立して回せるように、専用の親を挟みます。
            GameObject ring = new GameObject("Hover Ring") { hideFlags = HideFlags.DontSave };
            ring.layer = gameObject.layer;
            ring.transform.SetParent(visualRoot, false);
            hoverRingRoot = ring.transform;
            AddRenderer("Ring", BuildHoverRingMesh(), neonMaterial, false, hoverRingRoot);
        }

        CreateLight();
        appliedBlend = -1f;
        return true;
    }

    private Material CreateFrameMaterial(Shader fallback)
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogWarning("NeonAndonLight: URP/Lit が見つからないため、枠を単色で描画します。", this);
            Material flat = new Material(fallback) { name = "Andon Frame", hideFlags = HideFlags.HideAndDontSave };
            flat.SetColor(ColorBottomId, frameColor);
            flat.SetColor(ColorTopId, frameColor);
            flat.SetFloat(IntensityId, 1f);
            flat.SetFloat(AlphaId, 1f);
            flat.SetFloat(RimBoostId, 0.4f);
            return flat;
        }

        Material material = new Material(lit) { name = "Andon Frame", hideFlags = HideFlags.HideAndDontSave };
        material.SetColor(BaseColorId, frameColor);
        material.SetFloat(MetallicId, 0.85f);
        material.SetFloat(SmoothnessId, 0.68f);
        return material;
    }

    private void ApplyColors()
    {
        if (paperMaterial != null)
        {
            paperMaterial.SetColor(ColorBottomId, paperBottomColor);
            paperMaterial.SetColor(ColorTopId, paperTopColor);
            paperMaterial.SetFloat(FlowId, lightFlow);
            paperMaterial.SetFloat(AlphaId, 0.93f);
        }
        if (neonMaterial != null)
        {
            // ネオン管は UV が管の周りを回るため、2色を同じにして単色にします。
            neonMaterial.SetColor(ColorBottomId, neonTubeColor);
            neonMaterial.SetColor(ColorTopId, neonTubeColor);
            neonMaterial.SetFloat(FlowId, 0f);
            neonMaterial.SetFloat(AlphaId, 1f);
            neonMaterial.SetFloat(RimBoostId, 0.6f);
        }
    }

    private void ApplyGlow()
    {
        float scale = Mathf.Lerp(daytimeGlowScale, 1f, lampBlend);
        if (paperMaterial != null) paperMaterial.SetFloat(IntensityId, paperGlow * scale);
        if (neonMaterial != null) neonMaterial.SetFloat(IntensityId, neonGlow * scale);
        if (roadLight != null)
        {
            roadLight.enabled = lampBlend > 0.001f;
            roadLight.intensity = lightIntensity * lampBlend;
        }
    }

    private void CreateLight()
    {
        GameObject holder = new GameObject("Road Light") { hideFlags = HideFlags.DontSave };
        holder.transform.SetParent(visualRoot, false);
        // 台の真下から照らします。スポットは真下を向けます。
        holder.transform.localPosition = new Vector3(0f, UnderPanelY * height, 0f);
        holder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        roadLight = holder.AddComponent<Light>();
        roadLight.type = lampShape == LampShape.Spot ? LightType.Spot : LightType.Point;
        roadLight.color = overrideLightColor ? lightColor : Color.Lerp(paperBottomColor, paperTopColor, 0.35f);
        roadLight.range = lightRange;
        roadLight.spotAngle = spotAngle;
        roadLight.innerSpotAngle = spotAngle * 0.55f;
        roadLight.intensity = lightIntensity * lampBlend;
        roadLight.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
        roadLight.renderMode = LightRenderMode.Auto;
    }

    private void AddRenderer(string name, Mesh mesh, Material material, bool shadows, Transform parent = null)
    {
        GameObject part = new GameObject(name) { hideFlags = HideFlags.DontSave };
        part.layer = gameObject.layer;
        part.transform.SetParent(parent != null ? parent : visualRoot, false);
        part.AddComponent<MeshFilter>().sharedMesh = Track(mesh);

        MeshRenderer renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.receiveShadows = shadows;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.lightProbeUsage = shadows ? LightProbeUsage.BlendProbes : LightProbeUsage.Off;
    }

    // ---------------------------------------------------------------- 形状

    /// <summary>断面のひとまわり。角丸の四角（スーパー楕円）で行灯の四角さを残します。</summary>
    private Vector3 Section(float angle, float radius, float y)
    {
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);
        float exponent = 2f / cornerSharpness;
        float half = width * 0.5f * radius;
        return new Vector3(
            Mathf.Sign(cos) * Mathf.Pow(Mathf.Abs(cos), exponent) * half,
            y * height,
            Mathf.Sign(sin) * Mathf.Pow(Mathf.Abs(sin), exponent) * half);
    }

    /// <summary>天板・台・桟・角柱。ひとつのメッシュにまとめて1ドローコールに収めます。</summary>
    private Mesh BuildFrameMesh()
    {
        MeshBuilder builder = new MeshBuilder();

        // 台（下の板）。面ごとに帯を分けることで、稜線がはっきり立ちます。
        AddBand(builder, new[] { new Ring(BasePlateBottom, 0.78f), new Ring(0.062f, 0.92f) }, true, false);
        AddBand(builder, new[] { new Ring(0.062f, 0.92f), new Ring(0.100f, 0.92f) }, false, false);
        AddBand(builder, new[] { new Ring(0.100f, 0.92f), new Ring(BasePlateTop, 0.80f) }, false, true);

        // 天板。薄く張り出させて、笠というより一枚板に見せます。
        AddBand(builder, new[] { new Ring(0.885f, 0.72f), new Ring(0.912f, 0.88f) }, true, false);
        AddBand(builder, new[] { new Ring(0.912f, 0.88f), new Ring(0.952f, 0.88f) }, false, false);
        AddBand(builder, new[] { new Ring(0.952f, 0.88f), new Ring(0.985f, 0.74f) }, false, false);
        AddBand(builder, new[] { new Ring(0.985f, 0.74f), new Ring(1.000f, 0.58f) }, false, true);

        if (midRail)
        {
            AddBand(builder, new[] { new Ring(0.330f, 0.735f), new Ring(0.352f, 0.775f) }, false, false);
            AddBand(builder, new[] { new Ring(0.352f, 0.775f), new Ring(0.368f, 0.775f) }, false, false);
            AddBand(builder, new[] { new Ring(0.368f, 0.775f), new Ring(0.390f, 0.730f) }, false, false);
        }

        if (cornerPosts)
            for (int i = 0; i < 4; i++)
                AddPost(builder, 45f + i * 90f, 0.105f, 0.905f, 0.76f, 0.645f, 0.045f);

        return builder.ToMesh("Andon Frame");
    }

    /// <summary>障子紙にあたる面。ゆるやかに絞りながら、わずかに膨らませます。</summary>
    private Mesh BuildPaperMesh()
    {
        MeshBuilder builder = new MeshBuilder();
        const int steps = 14;
        Ring[] rings = new Ring[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float v = (float)i / steps;
            float y = Mathf.Lerp(PaperBottom, PaperTop, v);
            float r = Mathf.Lerp(PaperBottomRadius, PaperTopRadius, v) + PaperBulge * Mathf.Sin(Mathf.PI * v);
            rings[i] = new Ring(y, r);
        }
        AddBand(builder, rings, false, false);
        // 台の下に向いた発光板。走っている車から見上げたときの光源になります。
        AddCap(builder, new Ring(UnderPanelY, 0.70f), -1f);
        return builder.ToMesh("Andon Paper");
    }

    /// <summary>上下のネオン管。</summary>
    private Mesh BuildNeonMesh()
    {
        MeshBuilder builder = new MeshBuilder();
        AddTube(builder, 0.132f, 0.845f, 0.032f);
        AddTube(builder, 0.872f, 0.715f, 0.030f);
        return builder.ToMesh("Andon Neon");
    }

    private Mesh BuildHoverRingMesh()
    {
        MeshBuilder builder = new MeshBuilder();
        AddTube(builder, -0.042f, 0.98f, 0.020f);
        return builder.ToMesh("Andon Hover Ring");
    }

    /// <summary>複数のリングを縦につないだ帯。ひと続きの帯の中だけ法線をならすので、
    /// 帯を分けた境目がそのまま稜線になります。</summary>
    private void AddBand(MeshBuilder builder, Ring[] rings, bool capBottom, bool capTop)
    {
        int segments = sideSegments;
        int start = builder.Positions.Count;

        for (int i = 0; i < rings.Length; i++)
        {
            for (int j = 0; j < segments; j++)
            {
                float angle = (float)j / segments * Mathf.PI * 2f;
                builder.Positions.Add(Section(angle, rings[i].R, rings[i].Y));
                builder.Normals.Add(Vector3.zero);
                builder.Uvs.Add(new Vector2((float)j / segments, rings.Length > 1 ? (float)i / (rings.Length - 1) : 0f));
            }
        }

        for (int i = 0; i < rings.Length - 1; i++)
        {
            for (int j = 0; j < segments; j++)
            {
                int a = start + i * segments + j;
                int b = start + i * segments + (j + 1) % segments;
                int c = start + (i + 1) * segments + j;
                int d = start + (i + 1) * segments + (j + 1) % segments;
                builder.Triangles.Add(a); builder.Triangles.Add(c); builder.Triangles.Add(b);
                builder.Triangles.Add(b); builder.Triangles.Add(c); builder.Triangles.Add(d);
            }
        }

        // 頂点を複製せずに一周させているため、法線は前後の頂点から直接求めて継ぎ目をなくします。
        for (int i = 0; i < rings.Length; i++)
        {
            int upper = Mathf.Min(i + 1, rings.Length - 1);
            int lower = Mathf.Max(i - 1, 0);
            for (int j = 0; j < segments; j++)
            {
                int index = start + i * segments + j;
                Vector3 along = builder.Positions[start + i * segments + (j + 1) % segments] -
                    builder.Positions[start + i * segments + (j - 1 + segments) % segments];
                Vector3 up = upper == lower
                    ? Vector3.up
                    : builder.Positions[start + upper * segments + j] - builder.Positions[start + lower * segments + j];

                Vector3 normal = Vector3.Cross(up, along);
                if (normal.sqrMagnitude < 1e-10f) normal = new Vector3(builder.Positions[index].x, 0f, builder.Positions[index].z);
                normal.Normalize();

                Vector3 outward = new Vector3(builder.Positions[index].x, 0f, builder.Positions[index].z);
                if (Vector3.Dot(normal, outward) < 0f) normal = -normal;
                builder.Normals[index] = normal;
            }
        }

        if (capBottom) AddCap(builder, rings[0], -1f);
        if (capTop) AddCap(builder, rings[rings.Length - 1], 1f);
    }

    /// <summary>リングの中心へ張る平らな面。上下の板のふたと、底面の発光板に使います。</summary>
    private void AddCap(MeshBuilder builder, Ring ring, float direction)
    {
        int segments = sideSegments;
        int center = builder.Positions.Count;

        builder.Positions.Add(new Vector3(0f, ring.Y * height, 0f));
        builder.Normals.Add(new Vector3(0f, direction, 0f));
        builder.Uvs.Add(new Vector2(0.5f, 0f));

        for (int j = 0; j < segments; j++)
        {
            float angle = (float)j / segments * Mathf.PI * 2f;
            builder.Positions.Add(Section(angle, ring.R, ring.Y));
            builder.Normals.Add(new Vector3(0f, direction, 0f));
            builder.Uvs.Add(new Vector2((float)j / segments, 0f));
        }

        for (int j = 0; j < segments; j++)
        {
            int a = center + 1 + j;
            int b = center + 1 + (j + 1) % segments;
            builder.Triangles.Add(center);
            builder.Triangles.Add(direction > 0f ? b : a);
            builder.Triangles.Add(direction > 0f ? a : b);
        }
    }

    /// <summary>四隅に立てる細い柱。紙の絞りに合わせて内側へ寄せていきます。</summary>
    private void AddPost(MeshBuilder builder, float degrees, float y0, float y1, float r0, float r1, float thickness)
    {
        const int steps = 6;
        const int sides = 8;
        float angle = degrees * Mathf.Deg2Rad;
        float radius = width * 0.5f * thickness;
        int start = builder.Positions.Count;

        for (int i = 0; i <= steps; i++)
        {
            float v = (float)i / steps;
            Vector3 center = Section(angle, Mathf.Lerp(r0, r1, v), Mathf.Lerp(y0, y1, v));
            for (int j = 0; j < sides; j++)
            {
                float a = (float)j / sides * Mathf.PI * 2f;
                Vector3 normal = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                builder.Positions.Add(center + normal * radius);
                builder.Normals.Add(normal);
                builder.Uvs.Add(new Vector2((float)j / sides, v));
            }
        }

        for (int i = 0; i < steps; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                int a = start + i * sides + j;
                int b = start + i * sides + (j + 1) % sides;
                int c = start + (i + 1) * sides + j;
                int d = start + (i + 1) * sides + (j + 1) % sides;
                builder.Triangles.Add(a); builder.Triangles.Add(c); builder.Triangles.Add(b);
                builder.Triangles.Add(b); builder.Triangles.Add(c); builder.Triangles.Add(d);
            }
        }
    }

    /// <summary>断面の輪郭をなぞるネオン管。角丸の四角い輪になります。</summary>
    private void AddTube(MeshBuilder builder, float y, float radius, float thickness)
    {
        int path = sideSegments;
        const int tubeSides = 10;
        float tube = width * 0.5f * thickness;
        int start = builder.Positions.Count;

        for (int i = 0; i < path; i++)
        {
            float angle = (float)i / path * Mathf.PI * 2f;
            Vector3 center = Section(angle, radius, y);
            Vector3 ahead = Section(angle + 0.01f, radius, y);
            Vector3 behind = Section(angle - 0.01f, radius, y);

            Vector3 outward = new Vector3(-(ahead.z - behind.z), 0f, ahead.x - behind.x).normalized;
            if (Vector3.Dot(outward, new Vector3(center.x, 0f, center.z)) < 0f) outward = -outward;

            for (int k = 0; k < tubeSides; k++)
            {
                float a = (float)k / tubeSides * Mathf.PI * 2f;
                Vector3 normal = outward * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a);
                builder.Positions.Add(center + normal * tube);
                builder.Normals.Add(normal);
                builder.Uvs.Add(new Vector2((float)i / path, (float)k / tubeSides));
            }
        }

        for (int i = 0; i < path; i++)
        {
            int next = (i + 1) % path;
            for (int k = 0; k < tubeSides; k++)
            {
                int k2 = (k + 1) % tubeSides;
                int a = start + i * tubeSides + k;
                int b = start + i * tubeSides + k2;
                int c = start + next * tubeSides + k;
                int d = start + next * tubeSides + k2;
                builder.Triangles.Add(a); builder.Triangles.Add(b); builder.Triangles.Add(c);
                builder.Triangles.Add(b); builder.Triangles.Add(d); builder.Triangles.Add(c);
            }
        }
    }

    private readonly struct Ring
    {
        public readonly float Y;
        public readonly float R;
        public Ring(float y, float r) { Y = y; R = r; }
    }

    private sealed class MeshBuilder
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();

        public Mesh ToMesh(string name)
        {
            Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(Positions);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // ---------------------------------------------------------------- 後始末

    private T Track<T>(T value) where T : Object
    {
        owned.Add(value);
        return value;
    }

    private void Release()
    {
        if (visualRoot != null) DestroyGenerated(visualRoot.gameObject);
        foreach (Object value in owned) DestroyGenerated(value);
        owned.Clear();
        visualRoot = null;
        hoverRingRoot = null;
        paperMaterial = null;
        neonMaterial = null;
        frameMaterial = null;
        roadLight = null;
        appliedBlend = -1f;
        built = false;
    }

    private static void DestroyGenerated(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
    }
}
