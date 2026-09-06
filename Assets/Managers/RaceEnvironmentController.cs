using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>レース全体の天気と時間帯。設定は再生開始時と再生中の変更時に反映します。</summary>
[DisallowMultipleComponent]
public sealed class RaceEnvironmentController : MonoBehaviour
{
    public enum Weather { [InspectorName("晴れ")] Sunny, [InspectorName("雨")] Rainy, [InspectorName("曇り")] Cloudy }
    public enum TimeOfDay { [InspectorName("昼")] Day, [InspectorName("夜")] Night }

    [Header("シチュエーション（再生中も変更できます）")]
    [SerializeField] private Weather weather = Weather.Sunny;
    [SerializeField] private TimeOfDay timeOfDay = TimeOfDay.Day;
    [Header("シーン参照")]
    [Tooltip("景観を照らす方向ライトをすべて登録してください。")]
    [SerializeField] private Light[] directionalLights = new Light[0];
    [Tooltip("空の場合は MainCamera という名前またはタグのカメラを自動検出します（2人プレイ対応）。")]
    [SerializeField] private Camera[] rainCameras = new Camera[0];
    [SerializeField] private Shader skyShader;
    [SerializeField] private Shader rainShader;
    [Header("明るさ")]
    [SerializeField, Min(0f)] private float dayLightIntensity = 2f;
    [SerializeField, Min(0f)] private float nightLightIntensity = 0.18f;
    [SerializeField, Range(0f, 1f)] private float nightAmbientBrightness = 0.16f;
    [Header("霧")]
    [SerializeField] private bool enableFog = true;
    [SerializeField, Range(0f, 0.05f)] private float sunnyFogDensity = 0.0005f;
    [SerializeField, Range(0f, 0.05f)] private float cloudyFogDensity = 0.002f;
    [SerializeField, Range(0f, 0.05f)] private float rainyFogDensity = 0.005f;
    [Header("雨（各カメラの周囲に生成）")]
    [SerializeField, Range(0f, 3000f)] private float rainRate = 1000f;
    [SerializeField, Range(10f, 60f)] private float rainAreaSize = 40f;
    [SerializeField, Range(10f, 40f)] private float rainFallSpeed = 25f;

    private readonly Dictionary<Camera, ParticleSystem> rainSystems = new Dictionary<Camera, ParticleSystem>();
    private readonly List<Camera> staleCameras = new List<Camera>();
    private readonly List<LightState> lightStates = new List<LightState>();
    private Material skyMaterial, rainMaterial, originalSky;
    private Color originalAmbient, originalFogColor;
    private AmbientMode originalAmbientMode;
    private FogMode originalFogMode;
    private bool originalFog, captured, dirty = true;
    private float originalFogDensity, originalReflection, nextCameraScan;

    private struct LightState
    {
        public Light light;
        public Color color;
        public float intensity, shadowStrength;
        public bool temperature;
        public Quaternion rotation;
    }

    public Weather CurrentWeather => weather;
    public TimeOfDay CurrentTimeOfDay => timeOfDay;

    public void SetSituation(Weather newWeather, TimeOfDay newTimeOfDay)
    {
        weather = newWeather;
        timeOfDay = newTimeOfDay;
        if (isActiveAndEnabled) ApplyEnvironment();
    }

    private void OnEnable()
    {
        dirty = true;
        RenderPipelineManager.beginCameraRendering += BeforeCameraRendering;
    }

    private void BeforeCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // 各プレイヤーの雨はそのカメラだけに描画し、接近時の二重表示やUIへの描画を防ぎます。
        foreach (var pair in rainSystems)
            pair.Value.GetComponent<ParticleSystemRenderer>().enabled = pair.Key == camera;
    }
    // OnValidate may run outside the main thread. Unity objects are updated in LateUpdate.
    private void OnValidate() { dirty = true; }

    private void LateUpdate()
    {
        if (dirty) ApplyEnvironment();
        if (Time.unscaledTime >= nextCameraScan)
        {
            RefreshRainCameras();
            nextCameraScan = Time.unscaledTime + 0.5f;
        }
        foreach (var pair in rainSystems)
        {
            if (pair.Key == null) continue;
            pair.Value.transform.position = pair.Key.transform.position + Vector3.up * 12f;
            bool visible = weather == Weather.Rainy && pair.Key.isActiveAndEnabled;
            if (visible && !pair.Value.isPlaying) pair.Value.Play();
            else if (!visible && pair.Value.isPlaying) pair.Value.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    public void ApplyEnvironment()
    {
        CaptureOriginalState();
        bool night = timeOfDay == TimeOfDay.Night;
        float cloud = weather == Weather.Sunny ? 0.12f : weather == Weather.Cloudy ? 0.8f : 1f;
        float lightFactor = weather == Weather.Sunny ? 1f : weather == Weather.Cloudy ? 0.6f : 0.4f;
        Color horizon = night ? new Color(0.035f, 0.05f, 0.09f) : Color.Lerp(new Color(0.65f, 0.8f, 0.95f), new Color(0.38f, 0.43f, 0.49f), cloud);
        foreach (LightState state in lightStates)
        {
            if (state.light == null) continue;
            state.light.intensity = (night ? nightLightIntensity : dayLightIntensity) * lightFactor;
            state.light.color = night ? new Color(0.55f, 0.68f, 1f) : new Color(1f, 0.96f, 0.88f);
            state.light.useColorTemperature = false;
            state.light.shadowStrength = Mathf.Lerp(state.shadowStrength, 0.25f, cloud);
            state.light.transform.rotation = night ? Quaternion.Euler(35f, -145f, 0f) : state.rotation;
        }
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = night ? new Color(0.5f, 0.65f, 1f) * nightAmbientBrightness : Color.Lerp(new Color(0.65f, 0.7f, 0.8f), new Color(0.38f, 0.42f, 0.48f), cloud);
        RenderSettings.reflectionIntensity = night ? 0.12f : Mathf.Lerp(1f, 0.45f, cloud);
        RenderSettings.fog = enableFog;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = horizon;
        RenderSettings.fogDensity = weather == Weather.Sunny ? sunnyFogDensity : weather == Weather.Cloudy ? cloudyFogDensity : rainyFogDensity;
        if (skyMaterial == null && skyShader != null) skyMaterial = new Material(skyShader) { hideFlags = HideFlags.HideAndDontSave };
        if (skyMaterial != null)
        {
            skyMaterial.SetColor("_TopColor", night ? new Color(0.004f, 0.008f, 0.025f) : new Color(0.08f, 0.36f, 0.75f));
            skyMaterial.SetColor("_HorizonColor", horizon);
            skyMaterial.SetFloat("_CloudCover", cloud);
            skyMaterial.SetFloat("_Night", night ? 1f : 0f);
            RenderSettings.skybox = skyMaterial;
        }
        RefreshRainCameras();
        foreach (ParticleSystem system in rainSystems.Values) ConfigureRain(system);
        dirty = false;
    }

    private void CaptureOriginalState()
    {
        if (captured) return;
        originalSky = RenderSettings.skybox;
        originalAmbientMode = RenderSettings.ambientMode;
        originalAmbient = RenderSettings.ambientLight;
        originalFog = RenderSettings.fog;
        originalFogMode = RenderSettings.fogMode;
        originalFogColor = RenderSettings.fogColor;
        originalFogDensity = RenderSettings.fogDensity;
        originalReflection = RenderSettings.reflectionIntensity;
        foreach (Light light in directionalLights)
        {
            if (light == null || lightStates.Exists(s => s.light == light)) continue;
            lightStates.Add(new LightState { light = light, color = light.color, intensity = light.intensity,
                shadowStrength = light.shadowStrength, temperature = light.useColorTemperature, rotation = light.transform.rotation });
        }
        captured = true;
    }

    private void RefreshRainCameras()
    {
        staleCameras.Clear();
        foreach (var pair in rainSystems)
            if (pair.Key == null || (rainCameras.Length > 0 && System.Array.IndexOf(rainCameras, pair.Key) < 0)) staleCameras.Add(pair.Key);
        foreach (Camera camera in staleCameras)
        {
            Release(rainSystems[camera].gameObject);
            rainSystems.Remove(camera);
        }
        if (weather != Weather.Rainy || rainShader == null) return;
        if (rainCameras.Length > 0)
        {
            foreach (Camera camera in rainCameras) EnsureRain(camera);
        }
        else
        {
            foreach (Camera camera in Camera.allCameras)
                if (camera.gameObject.scene == gameObject.scene &&
                    (camera.name == "MainCamera" || camera.CompareTag("MainCamera"))) EnsureRain(camera);
        }
    }

    private void EnsureRain(Camera camera)
    {
        if (camera == null || rainSystems.ContainsKey(camera)) return;
        if (rainMaterial == null) rainMaterial = new Material(rainShader) { hideFlags = HideFlags.HideAndDontSave };
        GameObject root = new GameObject("Rain - " + camera.name) { hideFlags = HideFlags.DontSave };
        root.transform.SetParent(transform, false);
        root.transform.position = camera.transform.position + Vector3.up * 12f;
        ParticleSystem system = root.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startSpeed = 0f;
        main.startSize = 0.035f;
        main.startColor = new Color(0.65f, 0.78f, 0.9f, 0.55f);
        main.maxParticles = 10000;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        var velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = rainMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.04f;
        renderer.lengthScale = 3f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        rainSystems.Add(camera, system);
        ConfigureRain(system);
    }

    private void ConfigureRain(ParticleSystem system)
    {
        var main = system.main;
        main.startLifetime = 24f / Mathf.Max(10f, rainFallSpeed);
        var emission = system.emission;
        emission.rateOverTime = Mathf.Clamp(rainRate, 0f, 3000f);
        var shape = system.shape;
        shape.scale = new Vector3(rainAreaSize, 1f, rainAreaSize);
        var velocity = system.velocityOverLifetime;
        velocity.y = -Mathf.Max(10f, rainFallSpeed);
        if (weather == Weather.Rainy) system.Play();
        else system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCameraRendering;
        if (captured)
        {
            RenderSettings.skybox = originalSky;
            RenderSettings.ambientMode = originalAmbientMode;
            RenderSettings.ambientLight = originalAmbient;
            RenderSettings.fog = originalFog;
            RenderSettings.fogMode = originalFogMode;
            RenderSettings.fogColor = originalFogColor;
            RenderSettings.fogDensity = originalFogDensity;
            RenderSettings.reflectionIntensity = originalReflection;
            foreach (LightState state in lightStates)
            {
                if (state.light == null) continue;
                state.light.color = state.color;
                state.light.intensity = state.intensity;
                state.light.shadowStrength = state.shadowStrength;
                state.light.useColorTemperature = state.temperature;
                state.light.transform.rotation = state.rotation;
            }
        }
        foreach (ParticleSystem system in rainSystems.Values) if (system != null) Release(system.gameObject);
        rainSystems.Clear();
        lightStates.Clear();
        Release(skyMaterial);
        Release(rainMaterial);
        skyMaterial = rainMaterial = null;
        captured = false;
    }

    private static void Release(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
