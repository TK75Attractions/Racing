using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns two physically separated practice areas, using the production car and drive input.</summary>
public sealed class DrivingTutorialController : MonoBehaviour
{
    private sealed class Practice
    {
        public GameObject world, car;
        public Rigidbody body;
        public DebugMover mover;
        public IDriveInputSource input;
        public RaceDirectionCameraController camera;
        public DrivingTutorialUI ui;
        public readonly DrivingTutorialProgress lesson = new DrivingTutorialProgress();
        public float startYaw;
        public float recoveryTimer;
    }

    private readonly List<Practice> players = new List<Practice>();
    private readonly List<Material> materials = new List<Material>();
    private float readyTimer;
    public bool AllComplete
    {
        get
        {
            if (players.Count != 2) return false;
            foreach (Practice player in players) if (!player.lesson.IsComplete) return false;
            return readyTimer >= 1f;
        }
    }

    public void Begin(GameObject carPrefab, InputManager input, PlayerDisplayRig[] displays)
    {
        End();
        Material asphalt = Material("Practice asphalt", new Color(.035f, .045f, .075f), false);
        Material ground = Material("Practice surroundings", new Color(.012f, .022f, .04f), false);
        Material cyan = Material("Practice cyan", RacingUITheme.Cyan, true);
        Material pink = Material("Practice pink", new Color(1f, .08f, .3f), true);
        Material white = Material("Practice lane markings", new Color(.85f, .9f, 1f), true);
        for (int i = 0; i < displays.Length; i++)
        {
            Practice player = new Practice();
            players.Add(player);
            player.world = new GameObject($"Player{i + 1}_PrivatePracticeCourse");
            // More than a camera far plane apart; solid boundaries and recovery prevent escape.
            // Kept well above the race scene, so its sensors/items cannot be triggered by practice cars.
            player.world.transform.position = new Vector3(i * 5000f, 3000f, 0f);
            BuildCourse(player.world.transform, asphalt, ground, cyan, pink, white);
            Vector3 spawn = player.world.transform.position + new Vector3(0f, 1.3f, -48f);
            player.car = Instantiate(carPrefab, spawn, Quaternion.identity, player.world.transform);
            player.car.name = $"Player{i + 1}_TutorialCar";
            PlayerCarPaint paint = player.car.GetComponent<PlayerCarPaint>() ?? player.car.AddComponent<PlayerCarPaint>();
            paint.SetPlayerIndex(i);
            player.body = player.car.GetComponent<Rigidbody>();
            player.mover = player.car.GetComponent<DebugMover>();
            player.input = input.GetPlayerInputSource(i);
            player.mover.SetInputSource(player.input);
            // Lesson recovery provides a safe spawn for the current step instead of the race start.
            foreach (CarResetter resetter in player.car.GetComponentsInChildren<CarResetter>()) resetter.enabled = false;
            foreach (CarItemEffects effects in player.car.GetComponentsInChildren<CarItemEffects>()) effects.enabled = false;
            player.camera = player.world.AddComponent<RaceDirectionCameraController>();
            player.camera.SetCamera(displays[i].RaceCamera);
            player.camera.SetCar(player.car.transform);
            displays[i].RaceCamera.Follow = player.camera.CameraTarget;
            displays[i].RaceCamera.LookAt = player.camera.LookTarget;
            Vector3 cameraPosition = player.camera.CameraTarget.position;
            displays[i].RaceCamera.ForceCameraPosition(cameraPosition,
                Quaternion.LookRotation(player.camera.LookTarget.position - cameraPosition));
            player.ui = new DrivingTutorialUI(displays[i].CanvasRoot.transform, i);
            player.ui.Update(player.lesson, player.input.CurrentState, 0f, player.input.IsConnected);
        }
        Physics.SyncTransforms();
    }

    public void Tick(float dt)
    {
        bool complete = players.Count == 2;
        foreach (Practice player in players)
        {
            DriveInputState input = player.input.CurrentState;
            Vector3 local = player.car.transform.position - player.world.transform.position;
            float speed = Vector3.ProjectOnPlane(player.body.linearVelocity, Vector3.up).magnitude;
            bool escaped = local.y < -2f || Mathf.Abs(local.x - 38f) > 95f || Mathf.Abs(local.z) > 92f;
            bool overturned = Vector3.Dot(player.car.transform.up, Vector3.up) < .3f;
            player.recoveryTimer = overturned ? player.recoveryTimer + dt : 0f;
            if (!player.lesson.IsComplete && (escaped || player.recoveryTimer > 1.5f || input.resetPressed))
            {
                Recover(player);
                speed = 0f;
            }
            DrivingTutorialProgress.Stage previous = player.lesson.CurrentStage;
            player.lesson.Tick(dt, player.input.IsConnected, input.pedal, input.steering, speed,
                Mathf.DeltaAngle(player.startYaw, player.car.transform.eulerAngles.y));
            if (previous != player.lesson.CurrentStage)
            {
                if (player.lesson.CurrentStage == DrivingTutorialProgress.Stage.Steer)
                    Recover(player); // Start the corner exercise with enough road in front of the car.
                if (player.lesson.IsComplete)
                {
                    player.mover.enabled = false;
                    foreach (CarStabilityController stability in player.car.GetComponentsInChildren<CarStabilityController>()) stability.enabled = false;
                    foreach (CarSoundController sound in player.car.GetComponentsInChildren<CarSoundController>()) sound.enabled = false;
                    foreach (AudioSource audio in player.car.GetComponentsInChildren<AudioSource>()) audio.Stop();
                    player.body.linearVelocity = Vector3.zero;
                    player.body.angularVelocity = Vector3.zero;
                    player.body.isKinematic = true;
                }
            }
            player.ui.Update(player.lesson, input, speed, player.input.IsConnected);
            complete &= player.lesson.IsComplete;
        }
        readyTimer = complete ? readyTimer + dt : 0f;
    }

    private void FixedUpdate()
    {
        // Keep the small practice course approachable without replacing the production tire physics.
        foreach (Practice player in players)
        {
            if (player.body == null || player.body.isKinematic) continue;
            Vector3 velocity = player.body.linearVelocity;
            Vector3 planar = Vector3.ProjectOnPlane(velocity, Vector3.up);
            if (planar.magnitude > 10f)
                player.body.linearVelocity = planar.normalized * 10f + Vector3.up * velocity.y;
        }
    }

    private static void Recover(Practice player)
    {
        bool first = player.lesson.CurrentStage == DrivingTutorialProgress.Stage.Accelerate;
        player.body.position = player.world.transform.position + new Vector3(0f, 1.3f, first ? -48f : -12f);
        player.body.rotation = Quaternion.identity;
        player.body.linearVelocity = Vector3.zero;
        player.body.angularVelocity = Vector3.zero;
        player.startYaw = 0f;
        player.recoveryTimer = 0f;
        player.lesson.ResetCurrentAttempt();
        // A stop recovery must still require movement before it can complete the stopping lesson.
        if (player.lesson.CurrentStage == DrivingTutorialProgress.Stage.Stop)
            player.body.linearVelocity = Vector3.forward * 4f;
        player.mover.SuppressInputAfterRespawn();
    }

    public void End()
    {
        foreach (Practice player in players)
        {
            player.ui?.Dispose();
            player.camera?.ClearCar();
            if (player.world != null)
            {
                player.world.SetActive(false);
                Destroy(player.world);
            }
        }
        players.Clear();
        foreach (Material material in materials) if (material != null) Destroy(material);
        materials.Clear();
        readyTimer = 0f;
        TireMarkRenderer.ClearAll();
    }

    private void OnDestroy() => End();

    private Material Material(string name, Color color, bool glowing)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { name = name };
        material.color = color;
        if (glowing)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 2f);
        }
        materials.Add(material);
        return material;
    }

    private static void BuildCourse(Transform root, Material asphalt, Material ground, Material cyan, Material pink, Material white)
    {
        Box(root, "Safety floor", new Vector3(38f, -.6f, 0f), new Vector3(180f, 1f, 172f), ground, true);
        Vector3 previous = new Vector3(0f, 0f, -64f);
        for (int i = 0; i <= 30; i++)
        {
            Vector3 next;
            if (i <= 10) next = new Vector3(0f, 0f, -64f + i * 7f);
            else if (i <= 22)
            {
                float angle = (i - 10) / 12f * Mathf.PI * .5f;
                next = new Vector3(30f - Mathf.Cos(angle) * 30f, 0f, 6f + Mathf.Sin(angle) * 30f);
            }
            else next = new Vector3(30f + (i - 22) * 10f, 0f, 36f);
            if (i > 0)
            {
                Vector3 delta = next - previous;
                Quaternion rotation = Quaternion.LookRotation(delta);
                GameObject road = Box(root, $"Road{i}", (next + previous) * .5f + Vector3.down * .05f,
                    new Vector3(24f, .1f, delta.magnitude + .3f), asphalt, true);
                road.transform.localRotation = rotation;
                for (int side = -1; side <= 1; side += 2)
                {
                    GameObject rail = Box(root, $"NeonRail{i}_{side}", (next + previous) * .5f + rotation * Vector3.right * side * 12f + Vector3.up * .45f,
                        new Vector3(.4f, .9f, delta.magnitude + .5f), side < 0 ? cyan : pink, true);
                    rail.transform.localRotation = rotation;
                }
                GameObject marking = Box(root, $"LaneDash{i}", (next + previous) * .5f + Vector3.up * .012f,
                    new Vector3(.22f, .025f, delta.magnitude * .4f), white, false);
                marking.transform.localRotation = rotation;
            }
            previous = next;
        }
        Box(root, "Back boundary", new Vector3(38f, 2f, -86f), new Vector3(180f, 5f, 1f), cyan, true);
        Box(root, "Front boundary", new Vector3(38f, 2f, 86f), new Vector3(180f, 5f, 1f), pink, true);
        Box(root, "Left boundary", new Vector3(-52f, 2f, 0f), new Vector3(1f, 5f, 172f), cyan, true);
        Box(root, "Right boundary", new Vector3(128f, 2f, 0f), new Vector3(1f, 5f, 172f), pink, true);
        // Simple scenery makes movement and depth visible without external art assets.
        for (int i = 0; i < 12; i++)
        {
            float z = -70f + i * 12f;
            Box(root, $"LightPylon{i}", new Vector3(-24f, 3f, z), new Vector3(.35f, 6f, .35f), cyan, false);
            Box(root, $"CityBlock{i}", new Vector3(110f, 4f + i % 3 * 3f, z),
                new Vector3(8f, 8f + i % 3 * 6f, 7f), asphalt, false);
        }
        GameObject lamp = new GameObject("Practice key light", typeof(Light));
        lamp.transform.SetParent(root, false);
        lamp.transform.localPosition = new Vector3(20f, 24f, 4f);
        Light light = lamp.GetComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(.5f, .72f, 1f);
        light.range = 130f;
        light.intensity = 35f;
    }

    private static GameObject Box(Transform root, string name, Vector3 position, Vector3 size, Material material, bool solid)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(root, false);
        box.transform.localPosition = position;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
        if (!solid) box.GetComponent<Collider>().enabled = false;
        return box;
    }
}
