using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>稲妻1本の見た目です。</summary>
[System.Serializable]
public struct LightningStyle
{
    [Tooltip("光の縁の色。芯は常に白く光ります。")]
    public Color color;
    [Tooltip("幹の太さ（m）。枝はこれより細くなります。")]
    public float width;
    [Tooltip("区間の長さに対する折れ曲がりの大きさ。0.2〜0.4 程度が稲妻らしく見えます。")]
    public float jaggedness;
    [Tooltip("中点分割の回数（1〜7）。2のこの回数乗の区間に折れます。")]
    public int subdivisions;
    [Tooltip("幹から分かれる枝の数。")]
    public int branchCount;
    [Tooltip("表示時間（秒）。")]
    public float duration;
    [Tooltip("形を作り直してちらつかせる間隔（秒）。0以下で作り直しません。")]
    public float regenerateInterval;
    [Tooltip("周囲を照らす点光源の強さ。0で点光源を使いません。")]
    public float lightIntensity;
    [Tooltip("点光源の届く距離（m）。")]
    public float lightRange;
    [Tooltip("点光源を置く位置。0が始点、1が終点です。")]
    public float lightPlacement;
}

/// <summary>稲妻の端点です。Transform を指定すると、その物体に追従します。</summary>
public readonly struct LightningAnchor
{
    private readonly Transform target;
    private readonly Vector3 point;
    private readonly bool followsTarget;

    public LightningAnchor(Vector3 worldPoint)
    {
        target = null;
        point = worldPoint;
        followsTarget = false;
    }

    public LightningAnchor(Transform target, Vector3 localPoint)
    {
        this.target = target;
        point = localPoint;
        followsTarget = target != null;
    }

    /// <summary>追従先が破棄され、位置を決められなくなったかどうかです。</summary>
    public bool IsLost => followsTarget && target == null;
    public Vector3 Position => followsTarget && target != null ? target.TransformPoint(point) : point;
}

/// <summary>
/// 稲妻の描画、周囲を照らす点光源、雷鳴をまとめて扱います。
/// 稲妻はワールド空間に描くため、両プレイヤーの画面に映ります。
/// </summary>
[DisallowMultipleComponent]
public sealed class LightningEffects : MonoBehaviour
{
    private const int MaxSubdivisions = 7;
    private const int MaxPathPoints = (1 << MaxSubdivisions) + 1;
    private const int MaxActiveBolts = 48;
    private const int AudioSampleRate = 44100;

    [Header("Thunder")]
    [Tooltip("雷鳴全体の音量。")]
    [SerializeField, Range(0f, 1f)] private float thunderVolume = 0.8f;

    private sealed class Strand
    {
        public LineRenderer line;
        // x が幹方向の距離（m）、y と z が幹に垂直な2方向のずれ（m）です。
        public readonly Vector3[] frame = new Vector3[MaxPathPoints];
        public Vector3[] world;
        public int count;
        public float widthScale;
    }

    private sealed class Bolt
    {
        public LightningAnchor start;
        public LightningAnchor end;
        public LightningStyle style;
        public float generatedLength;
        public float age;
        public float nextRegenerateAge;
        public float flicker = 1f;
        public readonly List<Strand> strands = new List<Strand>(4);
        public Light light;
    }

    private static LightningEffects instance;
    private static bool isQuitting;
    private static AudioClip crackClip;
    private static AudioClip rumbleClip;

    private readonly List<Bolt> activeBolts = new List<Bolt>();
    private readonly Stack<Strand> strandPool = new Stack<Strand>();
    private readonly Stack<Light> lightPool = new Stack<Light>();
    private Material boltMaterial;
    private AnimationCurve trunkWidth;
    private AnimationCurve branchWidth;
    private AudioSource crackSource;
    private AudioSource rumbleSource;

    /// <summary>再生中のみ、必要になった時点で生成します。</summary>
    public static LightningEffects Instance
    {
        get
        {
            if (instance != null) return instance;
            if (isQuitting || !Application.isPlaying) return null;
            instance = new GameObject("Lightning Effects").AddComponent<LightningEffects>();
            return instance;
        }
    }

    public int ActiveBoltCount => activeBolts.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        isQuitting = false;
    }

    /// <summary>稲妻を1本走らせます。上限に達しているなどで作れなかった場合は false を返します。</summary>
    public static bool Strike(LightningAnchor start, LightningAnchor end, LightningStyle style)
    {
        LightningEffects effects = Instance;
        return effects != null && effects.SpawnBolt(start, end, style);
    }

    /// <summary>近くで放電した「バチッ」という音だけを鳴らします。</summary>
    public static void PlayCrack(float volume) => PlayThunder(volume, 0f, 0f);

    /// <summary>破裂音と、少し遅れて届くゴロゴロという遠雷を鳴らします。</summary>
    public static void PlayThunder(float crackVolume, float rumbleVolume, float rumbleDelaySeconds)
    {
        Instance?.PlayThunderInternal(crackVolume, rumbleVolume, rumbleDelaySeconds);
    }

    /// <summary>表示中の稲妻をすべて消します。レースのやり直し時に使います。</summary>
    public static void ClearAll()
    {
        if (instance != null) instance.ReleaseAllBolts();
    }

    /// <summary>中点変位法で折れ線を作ります。両端は必ず from と to に一致します。戻り値は点の数です。</summary>
    public static int BuildPath(Vector3[] buffer, Vector3 from, Vector3 to, int subdivisions, float jaggedness)
    {
        int segments = 1 << Mathf.Clamp(subdivisions, 0, MaxSubdivisions);
        buffer[0] = from;
        buffer[segments] = to;
        float roughness = Mathf.Max(0f, jaggedness);
        for (int step = segments; step > 1; step >>= 1)
        {
            int half = step >> 1;
            for (int index = 0; index < segments; index += step)
            {
                Vector3 a = buffer[index];
                Vector3 b = buffer[index + step];
                // 区間が短くなるほどずれも小さくなり、大きなうねりと細かなギザギザが両立します。
                Vector2 offset = Random.insideUnitCircle * Vector3.Distance(a, b) * roughness;
                buffer[index + half] = (a + b) * 0.5f + new Vector3(0f, offset.x, offset.y);
            }
        }

        return segments + 1;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        trunkWidth = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.6f));
        branchWidth = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.08f));
    }

    private void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        for (int index = activeBolts.Count - 1; index >= 0; index--)
        {
            Bolt bolt = activeBolts[index];
            bolt.age += deltaTime;
            if (bolt.age >= bolt.style.duration || bolt.start.IsLost || bolt.end.IsLost)
            {
                ReleaseBolt(bolt);
                activeBolts.RemoveAt(index);
                continue;
            }

            if (bolt.age >= bolt.nextRegenerateAge) Regenerate(bolt, false);
            ApplyBolt(bolt);
        }
    }

    private bool SpawnBolt(LightningAnchor start, LightningAnchor end, LightningStyle style)
    {
        if (activeBolts.Count >= MaxActiveBolts || start.IsLost || end.IsLost) return false;
        if (boltMaterial == null && !CreateMaterial()) return false;

        float length = Vector3.Distance(start.Position, end.Position);
        if (length < 0.01f) return false;

        style.duration = Mathf.Max(0.01f, style.duration);
        style.subdivisions = Mathf.Clamp(style.subdivisions, 1, MaxSubdivisions);
        style.branchCount = Mathf.Clamp(style.branchCount, 0, 8);
        Bolt bolt = new Bolt
        {
            start = start,
            end = end,
            style = style,
            generatedLength = length
        };

        for (int index = 0; index <= style.branchCount; index++)
        {
            bolt.strands.Add(RentStrand(index == 0));
        }

        if (style.lightIntensity > 0f) bolt.light = RentLight(style);
        Regenerate(bolt, true);
        ApplyBolt(bolt);
        activeBolts.Add(bolt);
        return true;
    }

    private static void Regenerate(Bolt bolt, bool isFirst)
    {
        LightningStyle style = bolt.style;
        float length = bolt.generatedLength;
        Strand trunk = bolt.strands[0];
        trunk.count = BuildPath(trunk.frame, Vector3.zero, new Vector3(length, 0f, 0f), style.subdivisions, style.jaggedness);
        trunk.widthScale = 1f;

        for (int index = 1; index < bolt.strands.Count; index++)
        {
            Strand branch = bolt.strands[index];
            // 枝は幹の途中から、進行方向へ斜めに伸ばします。
            int firstOrigin = Mathf.Max(1, trunk.count / 5);
            int origin = Random.Range(firstOrigin, Mathf.Max(firstOrigin + 1, trunk.count * 3 / 4));
            Vector3 from = trunk.frame[origin];
            float angle = Random.Range(20f, 55f) * Mathf.Deg2Rad;
            float roll = Random.Range(0f, Mathf.PI * 2f);
            Vector3 direction = new Vector3(
                Mathf.Cos(angle),
                Mathf.Sin(angle) * Mathf.Cos(roll),
                Mathf.Sin(angle) * Mathf.Sin(roll));
            Vector3 to = from + direction * length * Random.Range(0.15f, 0.4f);
            branch.count = BuildPath(branch.frame, from, to, style.subdivisions - 1, style.jaggedness);
            branch.widthScale = Random.Range(0.3f, 0.55f);
        }

        foreach (Strand strand in bolt.strands)
        {
            if (strand.world == null || strand.world.Length != strand.count) strand.world = new Vector3[strand.count];
        }

        // 最初は必ず最大の明るさで光らせ、以降はときどき暗く落として明滅させます。
        bolt.flicker = isFirst ? 1f : Random.value < 0.3f ? Random.Range(0.15f, 0.4f) : Random.Range(0.75f, 1f);
        bolt.nextRegenerateAge = style.regenerateInterval > 0f
            ? bolt.age + style.regenerateInterval * Random.Range(0.7f, 1.3f)
            : float.MaxValue;
    }

    private static void ApplyBolt(Bolt bolt)
    {
        Vector3 start = bolt.start.Position;
        Vector3 axis = bolt.end.Position - start;
        float length = axis.magnitude;
        Vector3 direction = length > 0.0001f ? axis / length : Vector3.forward;
        Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
        Vector3 right = Vector3.Cross(reference, direction).normalized;
        Vector3 up = Vector3.Cross(direction, right);
        // 端点が動いて距離が変わっても、形を幹方向に伸縮させて両端をつなぎ続けます。
        float along = length / Mathf.Max(0.0001f, bolt.generatedLength);

        float life = Mathf.Clamp01(bolt.age / bolt.style.duration);
        float brightness = (1f - life * life) * bolt.flicker;
        Color color = bolt.style.color;
        color.a = brightness;
        float width = Mathf.Max(0f, bolt.style.width) * Mathf.Lerp(0.55f, 1f, brightness);

        foreach (Strand strand in bolt.strands)
        {
            for (int index = 0; index < strand.count; index++)
            {
                Vector3 point = strand.frame[index];
                strand.world[index] = start + direction * (point.x * along) + right * point.y + up * point.z;
            }

            LineRenderer line = strand.line;
            line.positionCount = strand.count;
            line.SetPositions(strand.world);
            line.startColor = color;
            line.endColor = color;
            line.widthMultiplier = width * strand.widthScale;
        }

        if (bolt.light != null)
        {
            Strand trunk = bolt.strands[0];
            int lightIndex = Mathf.RoundToInt(Mathf.Clamp01(bolt.style.lightPlacement) * (trunk.count - 1));
            bolt.light.transform.position = trunk.world[lightIndex];
            bolt.light.intensity = bolt.style.lightIntensity * brightness;
        }
    }

    private Strand RentStrand(bool isTrunk)
    {
        Strand strand = strandPool.Count > 0 ? strandPool.Pop() : CreateStrand();
        strand.line.widthCurve = isTrunk ? trunkWidth : branchWidth;
        strand.line.enabled = true;
        return strand;
    }

    private Strand CreateStrand()
    {
        GameObject lineObject = new GameObject("Lightning Strand");
        lineObject.transform.SetParent(transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCornerVertices = 1;
        line.numCapVertices = 0;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        line.allowOcclusionWhenDynamic = false;
        line.sharedMaterial = boltMaterial;
        line.positionCount = 0;
        return new Strand { line = line };
    }

    private Light RentLight(LightningStyle style)
    {
        Light light = lightPool.Count > 0 ? lightPool.Pop() : CreateLight();
        light.color = Color.Lerp(style.color, Color.white, 0.4f);
        light.range = Mathf.Max(0.1f, style.lightRange);
        light.intensity = 0f;
        light.enabled = true;
        return light;
    }

    private Light CreateLight()
    {
        GameObject lightObject = new GameObject("Lightning Light");
        lightObject.transform.SetParent(transform, false);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.shadows = LightShadows.None;
        return light;
    }

    private void ReleaseBolt(Bolt bolt)
    {
        foreach (Strand strand in bolt.strands)
        {
            if (strand.line == null) continue;
            strand.line.positionCount = 0;
            strand.line.enabled = false;
            strandPool.Push(strand);
        }

        bolt.strands.Clear();
        if (bolt.light != null)
        {
            bolt.light.enabled = false;
            lightPool.Push(bolt.light);
            bolt.light = null;
        }
    }

    private void ReleaseAllBolts()
    {
        foreach (Bolt bolt in activeBolts) ReleaseBolt(bolt);
        activeBolts.Clear();
    }

    private bool CreateMaterial()
    {
        // Resources参照により、ビルド時にも専用シェーダーが含まれます。
        Shader shader = Resources.Load<Shader>("LightningBolt");
        if (shader == null)
        {
            Debug.LogError("LightningEffects requires the LightningBolt shader.", this);
            return false;
        }

        boltMaterial = new Material(shader) { name = "M_LightningBolt_Runtime" };
        return true;
    }

    private void PlayThunderInternal(float crackVolume, float rumbleVolume, float rumbleDelaySeconds)
    {
        EnsureAudio();
        if (crackVolume > 0f)
        {
            crackSource.PlayOneShot(crackClip, Mathf.Clamp01(crackVolume) * thunderVolume);
        }

        if (rumbleVolume > 0f)
        {
            // 光より音が遅れて届くことで、落雷の距離感を出します。
            rumbleSource.Stop();
            rumbleSource.clip = rumbleClip;
            rumbleSource.volume = Mathf.Clamp01(rumbleVolume) * thunderVolume;
            rumbleSource.PlayDelayed(Mathf.Max(0f, rumbleDelaySeconds));
        }
    }

    private void EnsureAudio()
    {
        if (crackClip == null) crackClip = CreateCrackClip();
        if (rumbleClip == null) rumbleClip = CreateRumbleClip();
        if (crackSource == null) crackSource = CreateAudioSource();
        if (rumbleSource == null) rumbleSource = CreateAudioSource();
    }

    private AudioSource CreateAudioSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        // 2画面で1つの音声出力を共有するため、位置による減衰をかけません。
        source.spatialBlend = 0f;
        return source;
    }

    // 高域の鋭い破裂音と、低めの胴鳴りを重ねた「バチッ」という音を合成します。
    private static AudioClip CreateCrackClip()
    {
        int length = Mathf.RoundToInt(AudioSampleRate * 0.6f);
        float[] samples = new float[length];
        System.Random random = new System.Random(1207);
        float previous = 0f;
        float low = 0f;
        for (int index = 0; index < length; index++)
        {
            float time = index / (float)AudioSampleRate;
            float white = (float)(random.NextDouble() * 2.0 - 1.0);
            float high = white - previous;
            previous = white;
            low += (white - low) * 0.08f;
            float snap = Mathf.Exp(-time * 38f);
            float body = Mathf.Exp(-time * 7f);
            // 最初の一瞬に、細かな放電のパチパチを散らします。
            float crackle = time < 0.12f && random.NextDouble() < 0.015
                ? (float)(random.NextDouble() * 2.0 - 1.0) * 2f
                : 0f;
            samples[index] = high * snap * 0.9f + low * body * 2.2f + crackle * body;
        }

        return CreateClip("LightningCrack", samples);
    }

    // 低域に寄せたノイズをゆっくり揺らし、転がるように長く尾を引く遠雷を合成します。
    private static AudioClip CreateRumbleClip()
    {
        int length = Mathf.RoundToInt(AudioSampleRate * 3.2f);
        float[] samples = new float[length];
        System.Random random = new System.Random(4099);
        float brown = 0f;
        float smooth = 0f;
        for (int index = 0; index < length; index++)
        {
            float time = index / (float)AudioSampleRate;
            float white = (float)(random.NextDouble() * 2.0 - 1.0);
            brown = (brown + white * 0.02f) * 0.998f;
            smooth += (brown - smooth) * 0.05f;
            float attack = Mathf.Clamp01(time / 0.12f);
            float decay = Mathf.Exp(-time * 1.1f);
            float roll = 0.6f + 0.4f * Mathf.Sin(time * 9.3f + Mathf.Sin(time * 3.1f) * 2f);
            samples[index] = (smooth * 0.7f + brown * 0.3f) * attack * decay * roll;
        }

        return CreateClip("LightningRumble", samples);
    }

    private static AudioClip CreateClip(string clipName, float[] samples)
    {
        float peak = 0f;
        foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
        float gain = peak > 0.0001f ? 0.9f / peak : 0f;
        for (int index = 0; index < samples.Length; index++) samples[index] *= gain;

        AudioClip clip = AudioClip.Create(clipName, samples.Length, 1, AudioSampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnApplicationQuit()
    {
        isQuitting = true;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (boltMaterial != null) Destroy(boltMaterial);
    }
}
