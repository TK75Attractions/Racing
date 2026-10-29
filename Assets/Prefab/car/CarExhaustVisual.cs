using UnityEngine;

/// <summary>Small, low-count exhaust puffs whose tint follows the selected vehicle.</summary>
[DisallowMultipleComponent]
public sealed class CarExhaustVisual : MonoBehaviour
{
    private static readonly Color[] Colors =
    {
        new Color(.35f, .82f, 1f, .42f),
        new Color(1f, .56f, .28f, .42f),
        new Color(1f, .32f, .68f, .42f)
    };

    private ParticleSystem exhaust;
    private Material material;
    private DebugMover mover;
    private float nextPuff;

    public void Configure(int profile)
    {
        if (exhaust == null && !CreateExhaust()) return;
        Color color = Colors[Mathf.Clamp(profile, 0, Colors.Length - 1)];
        var main = exhaust.main;
        main.startColor = color;
        if (material != null)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
        }
    }

    private void Awake() => mover = GetComponent<DebugMover>();

    private void Update()
    {
        if (exhaust == null || mover == null || mover.PedalInput < .12f || Time.time < nextPuff) return;
        nextPuff = Time.time + .09f;
        exhaust.Emit(1);
    }

    private bool CreateExhaust()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null || !shader.isSupported) return false;
        Bounds bounds = new Bounds();
        bool found = false;
        foreach (Renderer part in GetComponentsInChildren<Renderer>(true))
        {
            Bounds world = part.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = transform.InverseTransformPoint(new Vector3(
                    (corner & 1) == 0 ? world.min.x : world.max.x,
                    (corner & 2) == 0 ? world.min.y : world.max.y,
                    (corner & 4) == 0 ? world.min.z : world.max.z));
                if (found) bounds.Encapsulate(point);
                else { bounds = new Bounds(point, Vector3.zero); found = true; }
            }
        }
        GameObject node = new GameObject("Vehicle Exhaust Tint") { layer = gameObject.layer };
        node.transform.SetParent(transform, false);
        Vector3 rear = found ? new Vector3(bounds.center.x, bounds.center.y, bounds.min.z) : new Vector3(0f, .35f, -1.5f);
        node.transform.localPosition = rear;
        exhaust = node.AddComponent<ParticleSystem>();
        var main = exhaust.main;
        main.loop = true; main.playOnAwake = false; main.maxParticles = 12;
        main.startLifetime = .42f; main.startSpeed = .55f; main.startSize = .09f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = exhaust.emission; emission.enabled = false;
        var shape = exhaust.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 14f; shape.radius = .025f;
        material = new Material(shader) { name = "Vehicle Exhaust Tint", hideFlags = HideFlags.HideAndDontSave };
        var renderer = node.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        mover = GetComponent<DebugMover>();
        exhaust.Play();
        return true;
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}
