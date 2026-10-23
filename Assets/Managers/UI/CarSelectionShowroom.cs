using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>A mesh-only garage, isolated by layer and distance, rendered only while the sheet is open.</summary>
public sealed class CarSelectionShowroom : MonoBehaviour
{
    private const int PreviewLayer = 31;
    private readonly List<Material> materials = new List<Material>();
    private GameObject world, model;
    private Transform pivot;
    private Camera previewCamera;
    private PlayerCarPaint paint;
    private RenderTexture target;
    private bool dirty;
    private float lastRenderTime;
    public const float RotationDegreesPerSecond = 18f;
    public RenderTexture Texture => target;
    public GameObject Model => model;
    public Camera PreviewCamera => previewCamera;

    public void Initialize(int playerIndex)
    {
        world = new GameObject("Car Showroom P" + (playerIndex + 1));
        world.transform.position = new Vector3(20000f + playerIndex * 200f, 4000f, 0f);
        SceneManager.MoveGameObjectToScene(world, gameObject.scene);
        var palette=ArcadeUI.For(playerIndex);
        Material floor=Material("Arcade garage floor",palette.Ink,true);
        Cube("Floor",new Vector3(0,-.20f,0),new Vector3(40,.2f,40),floor);
        Light("Softbox",LightType.Point,Color.white,60f,Vector3.zero,new Vector3(-3,4,4));
        Light("Player rim",LightType.Point,palette.Soft,15f,Vector3.zero,new Vector3(3,3,-3));
        Light("Soft fill",LightType.Point,Color.Lerp(palette.Primary,Color.white,.65f),8f,Vector3.zero,new Vector3(3,2,-2));
        pivot = new GameObject("Vehicle rotation").transform;
        pivot.SetParent(world.transform, false);
        GameObject cameraObject = new GameObject("Showroom Camera");
        cameraObject.transform.SetParent(world.transform, false);
        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.scene = gameObject.scene;
        previewCamera.enabled = false;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = palette.Ink;
        previewCamera.cullingMask = 1 << PreviewLayer;
        previewCamera.nearClipPlane = .1f;
        previewCamera.farClipPlane = 60f;
        previewCamera.allowHDR = false;
        previewCamera.fieldOfView = 42f;
        previewCamera.transform.localPosition = new Vector3(-6.4f, 2.8f, 7.8f);
        Frame(false);
        target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { name = "Car Selection Preview", antiAliasing = 4 };
        target.Create();
        previewCamera.targetTexture = target;
        SetLayer(world.transform);
    }

    public void Show(CarSelectionCatalog.Vehicle vehicle, int colorIndex)
    {
        if (model != null) { model.SetActive(false); DestroyOwned(model); }
        pivot.localRotation = Quaternion.identity;
        model = CarSelectionModel.Create(vehicle.model, pivot);
        Bounds bounds = CarSelectionModel.Bounds(model);
        model.transform.localScale = Vector3.one * (4.8f / Mathf.Max(.01f, bounds.size.z));
        bounds = CarSelectionModel.Bounds(model);
        model.transform.position += new Vector3(pivot.position.x - bounds.center.x, pivot.position.y - bounds.min.y, pivot.position.z - bounds.center.z);
        SetLayer(model.transform);
        paint = model.AddComponent<PlayerCarPaint>();
        SetPaint(colorIndex);
        dirty = true;
    }

    public void SetPaint(int index)
    {
        paint?.SetColor(CarSelectionCatalog.Paints[Mathf.Clamp(index, 0, CarSelectionCatalog.Paints.Length - 1)]);
        dirty = true;
    }

    public void SetVisible(bool visible) { if (world != null) world.SetActive(visible); dirty = visible; }

    public void Rotate(float degrees) { pivot.Rotate(Vector3.up, degrees, Space.Self); dirty = true; }
    public void ResetView() { pivot.localRotation = Quaternion.identity; dirty = true; }
    public void TickRotation(float dt) { if (world != null && world.activeSelf && model != null) Rotate(RotationDegreesPerSecond * Mathf.Max(0f, dt)); }

    public RenderTexture Thumbnail(CarSelectionCatalog.Vehicle vehicle, int color)
    {
        RenderTexture thumbnail = new RenderTexture(400, 225, 24) { name = vehicle.id + " thumbnail", antiAliasing = 4 };
        thumbnail.Create();
        RenderThumbnail(vehicle,color,thumbnail);
        return thumbnail;
    }

    // UI is built before URP is ready on startup; refresh the retained textures on opening.
    public void RenderThumbnail(CarSelectionCatalog.Vehicle vehicle,int color,RenderTexture thumbnail)
    {
        Show(vehicle,color);Frame(true);RenderTo(thumbnail);Frame(false);
    }

    public void RenderNow() { RenderTo(target); dirty = false; }
    private void Update() => TickRotation(Time.unscaledDeltaTime);
    private void LateUpdate()
    {
        if (dirty && model != null && Time.unscaledTime - lastRenderTime >= 1f / 30f)
        { RenderNow(); lastRenderTime = Time.unscaledTime; }
    }

    private void RenderTo(RenderTexture destination)
    {
        if (GraphicsSettings.currentRenderPipeline != null)
        {
            var request = new RenderPipeline.StandardRequest { destination = destination };
            if (RenderPipeline.SupportsRenderRequest(previewCamera, request))
                RenderPipeline.SubmitRenderRequest(previewCamera, request);
        }
        else
        {
            RenderTexture previous = previewCamera.targetTexture;
            previewCamera.targetTexture = destination;
            previewCamera.Render();
            previewCamera.targetTexture = previous;
        }
    }

    private void Frame(bool thumbnail)
    {
        previewCamera.aspect = 16f / 9f;
        previewCamera.transform.LookAt(world.transform.position + new Vector3(0, .7f, 0));
        if (!thumbnail)
        {
            // Offset the focal point to leave the right side free for the performance panel.
            Vector3 focal = world.transform.position + new Vector3(0, .05f, 0) + previewCamera.transform.right * 1.2f;
            previewCamera.transform.LookAt(focal);
        }
        previewCamera.fieldOfView = thumbnail ? 26f : 34f;
    }

    private Material Material(string name, Color color, bool neon)
    {
        Shader shader = neon ? Resources.Load<Shader>("CarSelectionNeon") : Shader.Find(
            GraphicsSettings.currentRenderPipeline == null ? "Standard" : "Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        Material material = new Material(shader) { name = name, color = color };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .72f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", .35f);
        materials.Add(material);
        return material;
    }

    private GameObject Primitive(string name, PrimitiveType primitive, Material material)
    {
        GameObject obj = GameObject.CreatePrimitive(primitive);
        obj.name = name; obj.transform.SetParent(world.transform, false);
        Collider collider = obj.GetComponent<Collider>(); collider.enabled = false; DestroyOwned(collider);
        obj.GetComponent<Renderer>().sharedMaterial = material;
        return obj;
    }
    private void Cube(string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject obj = Primitive(name, PrimitiveType.Cube, material);
        obj.transform.localPosition = position; obj.transform.localScale = scale;
    }
    private void Arc(string name, float radius, float y, float from, float to, Material material, float width)
    {
        GameObject obj = new GameObject(name); obj.transform.SetParent(world.transform, false);
        LineRenderer line = obj.AddComponent<LineRenderer>();
        line.useWorldSpace = false; line.positionCount = 100; line.widthMultiplier = width;
        line.sharedMaterial = material; line.shadowCastingMode = ShadowCastingMode.Off;
        for (int i = 0; i < 100; i++)
        {
            float angle = Mathf.Lerp(from, to, i / 99f) * Mathf.Deg2Rad;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius));
        }
    }
    private void Light(string name, LightType type, Color color, float intensity, Vector3 rotation, Vector3 position)
    {
        GameObject obj = new GameObject(name); obj.transform.SetParent(world.transform, false);
        obj.transform.localEulerAngles = rotation; obj.transform.localPosition = position;
        Light light = obj.AddComponent<Light>(); light.type = type; light.color = color; light.intensity = intensity;
        light.range = 12f; light.cullingMask = 1 << PreviewLayer; light.shadows = LightShadows.None;
    }
    private static void SetLayer(Transform parent)
    { foreach (Transform child in parent.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = PreviewLayer; }
    private static void DestroyOwned(Object obj)
    { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    private void OnEnable() { if (world != null) { world.SetActive(true); dirty = true; } }
    private void OnDisable() { if (world != null) world.SetActive(false); }
    private void OnDestroy()
    {
        if (world != null) DestroyOwned(world);
        if (target != null) { target.Release(); DestroyOwned(target); }
        foreach (Material material in materials) if (material != null) DestroyOwned(material);
    }
}
