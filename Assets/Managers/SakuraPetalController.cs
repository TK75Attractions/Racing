using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>ステージ全体に舞い散る桜吹雪。花びらはワールド空間で漂い、
/// カメラごとに専用のパーティクルを生成して2人プレイでも二重に見えないようにします。</summary>
[DisallowMultipleComponent]
public sealed class SakuraPetalController : MonoBehaviour
{
    [Header("桜吹雪（再生中も変更できます）")]
    [SerializeField] private bool petalsEnabled = true;

    [Header("シーン参照")]
    [Tooltip("空の場合は MainCamera という名前またはタグのカメラを自動検出します（2人プレイ対応）。")]
    [SerializeField] private Camera[] targetCameras = new Camera[0];

    [Header("量と広がり")]
    [SerializeField, Range(0f, 1500f)] private float petalRate = 240f;
    [Tooltip("カメラの周囲に花びらを湧かせる範囲の広さ。")]
    [SerializeField, Range(10f, 150f)] private float areaSize = 60f;
    [Tooltip("花びらを湧かせる高さ。この範囲の全高で同時に湧くので、走り出してすぐ画面に入ります。")]
    [SerializeField, Range(3f, 40f)] private float areaHeight = 18f;
    [Tooltip("停止しているときに、カメラの前方どれだけ先を中心に湧かせるか。")]
    [SerializeField, Range(0f, 60f)] private float forwardLead = 14f;
    [SerializeField, Range(1f, 20f)] private float petalLifetime = 7f;

    [Header("走行中の追従")]
    [Tooltip("進行方向の何秒先まで花びらを撒くか。速いほど遠くに湧かせて、走り込む先を先に埋めます。")]
    [SerializeField, Range(0f, 8f)] private float lookAheadSeconds = 2.5f;
    [Tooltip("速度に応じて発生数を増やし、画面内の花びらの密度を保ちます。0で補正なし、1で密度がほぼ一定。")]
    [SerializeField, Range(0f, 2f)] private float speedCompensation = 1f;
    [Tooltip("同時に存在できる花びらの上限。処理負荷の上限でもあります。")]
    [SerializeField, Range(100, 20000)] private int maxPetals = 6000;

    [Header("動き")]
    [SerializeField, Range(0.2f, 8f)] private float fallSpeed = 1.4f;
    [SerializeField, Range(0f, 20f)] private float windSpeed = 3.5f;
    [SerializeField, Range(0f, 360f)] private float windDirectionDegrees = 35f;
    [Tooltip("風の乱れ。大きいほど不規則にひらひら舞います。")]
    [SerializeField, Range(0f, 3f)] private float turbulence = 0.9f;
    [Tooltip("花びらが回転する速さ（度/秒）。")]
    [SerializeField, Range(0f, 720f)] private float tumbleSpeed = 180f;

    [Header("見た目")]
    [SerializeField] private Color petalColor = new Color(1f, 0.78f, 0.88f);
    [Tooltip("1を超えると花びら自体が発光し、Bloomがのって暗いシーンでも見えるようになります。")]
    [SerializeField, Range(0f, 8f)] private float glowIntensity = 1.6f;
    [SerializeField, Range(0.02f, 1f)] private float petalSize = 0.17f;
    [SerializeField, Range(0f, 0.5f)] private float petalSizeVariation = 0.06f;

    [Header("Runtime Monitor")]
    [SerializeField] private float measuredSpeed;
    [SerializeField] private float currentRate;
    [SerializeField] private float currentDepth;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    /// <summary>カメラ1台分の発生源。走行速度と進行方向を追い続けます。</summary>
    private sealed class PetalEmitter
    {
        public ParticleSystem system;
        public Vector3 lastCameraPosition;
        public Vector3 direction = Vector3.forward;
        public float speed;
        public bool hasPreviousPosition;
    }

    private readonly Dictionary<Camera, PetalEmitter> emitters = new Dictionary<Camera, PetalEmitter>();
    private readonly List<Camera> staleCameras = new List<Camera>();
    private Material petalMaterial;
    private Mesh petalMesh;
    private float nextCameraScan;
    private bool dirty = true;

    public bool PetalsEnabled => petalsEnabled;

    public void SetPetalsEnabled(bool value)
    {
        petalsEnabled = value;
        dirty = true;
    }

    private void OnEnable()
    {
        dirty = true;
        RenderPipelineManager.beginCameraRendering += BeforeCameraRendering;
    }

    private void BeforeCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // 花びらはワールド空間にあるため、各プレイヤーの分がそのカメラだけに描画されるようにします。
        foreach (var pair in emitters)
            pair.Value.system.GetComponent<ParticleSystemRenderer>().enabled = pair.Key == camera;
    }

    // OnValidate may run outside the main thread. Unity objects are updated in LateUpdate.
    private void OnValidate() { dirty = true; }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextCameraScan)
        {
            RefreshPetalCameras();
            nextCameraScan = Time.unscaledTime + 0.5f;
        }
        if (dirty)
        {
            ApplyPetalMaterial();
            foreach (PetalEmitter emitter in emitters.Values) ConfigurePetals(emitter.system);
            dirty = false;
        }

        foreach (var pair in emitters)
        {
            if (pair.Key == null) continue;
            TrackCamera(pair.Value, pair.Key);
            UpdateEmitterVolume(pair.Value, pair.Key);

            bool visible = petalsEnabled && pair.Key.isActiveAndEnabled;
            if (visible && !pair.Value.system.isPlaying) pair.Value.system.Play();
            else if (!visible && pair.Value.system.isPlaying)
                pair.Value.system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    /// <summary>カメラの実移動から走行速度と進行方向を求めます。車の参照を持たずに済みます。</summary>
    private void TrackCamera(PetalEmitter emitter, Camera camera)
    {
        Vector3 position = camera.transform.position;
        Vector3 planarForward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
        if (planarForward.sqrMagnitude <= Mathf.Epsilon) planarForward = Vector3.forward;
        planarForward.Normalize();

        Vector3 travel = Vector3.ProjectOnPlane(position - emitter.lastCameraPosition, Vector3.up);
        emitter.lastCameraPosition = position;

        if (!emitter.hasPreviousPosition || Time.deltaTime <= 0f)
        {
            emitter.hasPreviousPosition = true;
            emitter.direction = planarForward;
            emitter.speed = 0f;
            return;
        }

        // リスポーンやカメラ切り替えの瞬間移動を速度と誤認しないようにします。
        if (travel.magnitude > areaSize)
        {
            emitter.direction = planarForward;
            emitter.speed = 0f;
            emitter.system.Clear(true);
            return;
        }

        float instantSpeed = travel.magnitude / Time.deltaTime;
        emitter.speed = Mathf.Lerp(emitter.speed, instantSpeed, 1f - Mathf.Exp(-Time.deltaTime * 3f));

        Vector3 target = instantSpeed > 1f ? travel.normalized : planarForward;
        emitter.direction = Vector3.Slerp(emitter.direction, target, 1f - Mathf.Exp(-Time.deltaTime * 5f)).normalized;
    }

    /// <summary>発生源を進行方向に伸ばし、走り込む先にも花びらを湧かせます。</summary>
    private void UpdateEmitterVolume(PetalEmitter emitter, Camera camera)
    {
        float lifetime = Mathf.Max(1f, petalLifetime);
        // 速いほど前方に長い箱にして、これから走る空間を先に埋めておきます。
        float depth = areaSize + emitter.speed * lookAheadSeconds;
        Vector3 offset = emitter.direction * (forwardLead + (depth - areaSize) * 0.5f);

        emitter.system.transform.SetPositionAndRotation(
            camera.transform.position + offset + Vector3.up * (areaHeight * 0.35f),
            Quaternion.LookRotation(emitter.direction, Vector3.up));

        var shape = emitter.system.shape;
        shape.scale = new Vector3(areaSize, Mathf.Max(1f, areaHeight), depth);

        // カメラが1秒間に通り抜ける体積の分だけ発生数を上乗せし、見える密度を保ちます。
        float rate = petalRate * (1f + emitter.speed * speedCompensation * lifetime / Mathf.Max(1f, areaSize));
        var emission = emitter.system.emission;
        emission.rateOverTime = rate;

        measuredSpeed = emitter.speed;
        currentRate = rate;
        currentDepth = depth;
    }

    private void RefreshPetalCameras()
    {
        staleCameras.Clear();
        foreach (var pair in emitters)
            if (pair.Key == null || (targetCameras.Length > 0 && System.Array.IndexOf(targetCameras, pair.Key) < 0))
                staleCameras.Add(pair.Key);
        foreach (Camera camera in staleCameras)
        {
            Release(emitters[camera].system.gameObject);
            emitters.Remove(camera);
        }

        if (!petalsEnabled) return;
        if (targetCameras.Length > 0)
        {
            foreach (Camera camera in targetCameras) EnsurePetals(camera);
        }
        else
        {
            foreach (Camera camera in Camera.allCameras)
                if (camera.gameObject.scene == gameObject.scene &&
                    (camera.name == "MainCamera" || camera.CompareTag("MainCamera"))) EnsurePetals(camera);
        }
    }

    private void EnsurePetals(Camera camera)
    {
        if (camera == null || emitters.ContainsKey(camera)) return;
        if (petalMaterial == null && !CreateSharedAssets()) return;

        GameObject root = new GameObject("Sakura - " + camera.name) { hideFlags = HideFlags.DontSave };
        root.transform.SetParent(transform, false);

        ParticleSystem system = root.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        // ワールド空間なので、車が花びらの中を追い越していく見え方になります。
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        var velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        // 発生源は進行方向に回転させるため、風はワールド空間で与えて向きを固定します。
        velocity.space = ParticleSystemSimulationSpace.World;
        var rotation = system.rotationOverLifetime;
        rotation.enabled = true;
        rotation.separateAxes = true;
        var noise = system.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.35f;
        noise.damping = true;
        // 寿命の前後で大きさを絞り、花びらが唐突に現れたり消えたりしないようにします。
        // 色を頂点ストリームに載せないため、フェードは透明度ではなく大きさで行います。
        var size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.1f, 1f),
            new Keyframe(0.85f, 1f), new Keyframe(1f, 0f)));

        var renderer = system.GetComponent<ParticleSystemRenderer>();
        // メッシュ描画にすると花びらが立体的に回転し、板が裏返る動きが出ます。
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = petalMesh;
        renderer.sharedMaterial = petalMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        PetalEmitter emitter = new PetalEmitter { system = system, lastCameraPosition = camera.transform.position };
        emitters.Add(camera, emitter);
        ConfigurePetals(system);
        TrackCamera(emitter, camera);
        UpdateEmitterVolume(emitter, camera);
    }

    /// <summary>速度で変化しない設定をまとめて適用します。量と広がりは毎フレーム更新します。</summary>
    private void ConfigurePetals(ParticleSystem system)
    {
        var main = system.main;
        main.startLifetime = Mathf.Max(1f, petalLifetime);
        float variation = Mathf.Min(petalSizeVariation, petalSize * 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(petalSize - variation, petalSize + variation);
        main.maxParticles = maxPetals;

        float windRadians = windDirectionDegrees * Mathf.Deg2Rad;
        var velocity = system.velocityOverLifetime;
        velocity.x = Mathf.Sin(windRadians) * windSpeed;
        velocity.z = Mathf.Cos(windRadians) * windSpeed;
        velocity.y = -Mathf.Max(0.2f, fallSpeed);

        // パーティクルの回転はスクリプトからはラジアン指定です。
        float tumble = tumbleSpeed * Mathf.Deg2Rad;
        var rotation = system.rotationOverLifetime;
        rotation.x = new ParticleSystem.MinMaxCurve(-tumble, tumble);
        rotation.y = new ParticleSystem.MinMaxCurve(-tumble, tumble);
        rotation.z = new ParticleSystem.MinMaxCurve(-tumble * 0.5f, tumble * 0.5f);

        var noise = system.noise;
        noise.strength = turbulence;

        if (petalsEnabled) system.Play();
        else system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    /// <summary>色と発光の強さはマテリアルから渡します。頂点カラー経由よりも環境差の影響を受けません。</summary>
    private void ApplyPetalMaterial()
    {
        if (petalMaterial == null) return;
        petalMaterial.SetColor(ColorId, petalColor);
        petalMaterial.SetFloat(IntensityId, glowIntensity);
    }

    private bool CreateSharedAssets()
    {
        // Resources参照により、ビルド時にも専用シェーダーが含まれます。
        Shader shader = Resources.Load<Shader>("SakuraPetal");
        if (shader == null)
        {
            Debug.LogError("SakuraPetal shader was not found.", this);
            enabled = false;
            return false;
        }

        petalMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        petalMesh = CreatePetalMesh();
        ApplyPetalMaterial();
        return true;
    }

    private static Mesh CreatePetalMesh()
    {
        Mesh mesh = new Mesh { name = "Sakura Petal", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        return mesh;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCameraRendering;
        foreach (PetalEmitter emitter in emitters.Values)
            if (emitter.system != null) Release(emitter.system.gameObject);
        emitters.Clear();
        Release(petalMaterial);
        Release(petalMesh);
        petalMaterial = null;
        petalMesh = null;
        nextCameraScan = 0f;
    }

    private static void Release(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
