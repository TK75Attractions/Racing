using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns two physically separated practice areas, using the production car and drive input.</summary>
public sealed class DrivingTutorialController : MonoBehaviour
{
    private sealed class Practice
    {
        public int playerIndex;
        public GameObject world, car;
        public Rigidbody body;
        public DebugMover mover;
        public IDriveInputSource input;
        public PlayerDriveInputGate driveGate;
        public RaceDirectionCameraController camera;
        public DrivingTutorialUI ui;
        public DrivingTutorialProgress lesson;
        public readonly TutorialGimmickBriefing briefing = new TutorialGimmickBriefing();
        public float startYaw;
        public float recoveryTimer, offCourseSeconds, headingSeconds, stuckSeconds, recoveryNoticeSeconds;
        public Vector3 progressPosition;
        public bool trackingProgress;
        public int recoveryCount;
        public TireForce[] tires;
        public bool resetHeld;
        public float itemProgress;
        public float missedItemSeconds;
        public DrivingTutorialProgress.Stage announcedStage;
        public readonly List<TutorialItemPickup> pickups = new List<TutorialItemPickup>();
    }

    private readonly List<Practice> players = new List<Practice>();
    private readonly List<Material> materials = new List<Material>();
    private float readyTimer;
    public bool AllComplete
    {
        get
        {
            if (players.Count != 2) return false;
            foreach (Practice player in players) if (!player.lesson.IsFinished || !player.briefing.IsComplete) return false;
            return readyTimer >= 1f;
        }
    }

    public void Begin(GameObject carPrefab, InputManager input, PlayerDisplayRig[] displays, System.Action<GameObject, int> customizeCar = null)
    {
        End();
        for (int i = 0; i < displays.Length; i++) BeginPlayer(carPrefab, input, displays, i, customizeCar);
    }

    public DrivingTutorialProgress GetPlayerProgress(int index) => players.Find(p => p.playerIndex == index)?.lesson;
    public TutorialGimmickBriefing GetPlayerBriefing(int index) => players.Find(p => p.playerIndex == index)?.briefing;
    public bool HasAnyStarted => players.Count > 0;
    public bool HasStarted(int index) => players.Exists(p => p.playerIndex == index);
    public GameObject GetPlayerCar(int index) => players.Find(p => p.playerIndex == index)?.car;

    public void BeginPlayer(GameObject carPrefab, InputManager input, PlayerDisplayRig[] displays, int i,
        System.Action<GameObject, int> customizeCar = null,
        float timeLimitSeconds = DrivingTutorialProgress.DefaultTimeLimitSeconds)
    {
        if (i < 0 || i >= displays.Length || HasStarted(i)) return;
        Material asphalt = Resources.Load<Material>("RaceCourse/M_RoadAsphalt");
        if (asphalt == null) asphalt = Material("Practice asphalt", new Color(.12f, .12f, .12f), false);
        Material ground = Material("Practice surroundings", new Color(.012f, .022f, .04f), false);
        Material cyan = Material("Practice cyan", RacingUITheme.Cyan, true);
        Material pink = Material("Practice pink", new Color(1f, .08f, .3f), true);
        Material white = Material("Practice lane markings", new Color(.85f, .9f, 1f), true);
        Practice player = new Practice { playerIndex = i, lesson = new DrivingTutorialProgress(timeLimitSeconds) };
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
        if (customizeCar != null) customizeCar(player.car, i);
        else paint.SetPlayerIndex(i);
        player.body = player.car.GetComponent<Rigidbody>();
        player.mover = player.car.GetComponent<DebugMover>();
        player.tires = player.car.GetComponentsInChildren<TireForce>();
        player.input = input.GetPlayerInputSource(i);
        player.driveGate = new PlayerDriveInputGate(player.input);
        player.mover.SetInputSource(player.driveGate);
        // Lesson recovery provides a safe spawn for the current step instead of the race start.
        foreach (CarResetter resetter in player.car.GetComponentsInChildren<CarResetter>()) resetter.enabled = false;
        CarItemEffects effects = player.car.GetComponent<CarItemEffects>() ?? player.car.AddComponent<CarItemEffects>();
        effects.Configure(i); effects.enabled = true;
        player.camera = player.world.AddComponent<RaceDirectionCameraController>();
        player.camera.SetCamera(displays[i].RaceCamera);
        player.camera.SetCar(player.car.transform);
        player.camera.SetLookHeight(.6f);
        displays[i].RaceCamera.Follow = player.camera.CameraTarget;
        displays[i].RaceCamera.LookAt = player.camera.LookTarget;
        Vector3 cameraPosition = player.camera.CameraTarget.position;
        displays[i].RaceCamera.ForceCameraPosition(cameraPosition,
            Quaternion.LookRotation(player.camera.LookTarget.position - cameraPosition));
        player.ui = new DrivingTutorialUI(displays[i].CanvasRoot.transform, i);
        player.ui.Update(player.lesson, player.input.CurrentState, 0f, player.input.IsConnected, DriverInputNotice.Connection(player.input));
        Physics.SyncTransforms();
    }

    public void Tick(float dt)
    {
        bool complete = players.Count == 2;
        foreach (Practice player in players)
        {
            DriveInputState input = player.input.CurrentState;
            bool resetPressed = player.input.IsConnected && input.resetPressed && !player.resetHeld;
            player.resetHeld = player.input.IsConnected && input.resetPressed;
            bool connected = player.input.IsConnected;
            Vector3 local = player.body.position - player.world.transform.position;
            float speed = Vector3.ProjectOnPlane(player.body.linearVelocity, Vector3.up).magnitude;
            float progress = DrivingTutorialRoad.NearestProgress(local);
            bool automaticRecovery = UpdateRecovery(player,local,progress,dt);
            bool missedItem = player.lesson.CurrentStage == DrivingTutorialProgress.Stage.CollectItem && player.pickups.Count > 0 && progress > player.itemProgress + 5f;
            player.missedItemSeconds = missedItem && player.input.IsConnected ? player.missedItemSeconds + dt : 0f;
            if (!player.lesson.IsFinished && (automaticRecovery || resetPressed || player.missedItemSeconds >= .5f))
            {
                Recover(player);
                speed = 0f;
            }
            player.lesson.Tick(dt, connected, input.pedal, input.steering, speed,
                Mathf.DeltaAngle(player.startYaw, player.car.transform.eulerAngles.y));
            // Pickup contact can complete a lesson between ticks. Remember the last announced
            // stage so that the final success cue is emitted once, just like the driving steps.
            if (player.announcedStage != player.lesson.CurrentStage)
            {
                player.announcedStage = player.lesson.CurrentStage;
                RaceSoundEffects.PlayForPlayer(player.lesson.IsComplete ? RaceSoundCue.Confirm : RaceSoundCue.Ready, player.input.PlayerIndex);
                if (player.lesson.CurrentStage == DrivingTutorialProgress.Stage.Steer)
                    player.startYaw = player.body.rotation.eulerAngles.y;
                if (player.lesson.CurrentStage == DrivingTutorialProgress.Stage.CollectItem) PlaceItems(player);
            }
            if (player.lesson.IsFinished && !player.body.isKinematic)
            {
                foreach (var pickup in player.pickups) if (pickup != null) pickup.gameObject.SetActive(false);
                player.mover.enabled = false;
                foreach (CarStabilityController stability in player.car.GetComponentsInChildren<CarStabilityController>()) stability.enabled = false;
                foreach (CarSoundController sound in player.car.GetComponentsInChildren<CarSoundController>()) sound.enabled = false;
                foreach (AudioSource audio in player.car.GetComponentsInChildren<AudioSource>()) audio.Stop();
                player.body.linearVelocity = Vector3.zero;
                player.body.angularVelocity = Vector3.zero;
                player.body.isKinematic = true;
            }
            if (player.lesson.IsFinished) speed = 0f;
            if (player.lesson.IsFinished) player.briefing.Tick(dt, player.input.IsConnected, input.pedal);
            player.ui.Update(player.lesson, input, speed, player.input.IsConnected, DriverInputNotice.Connection(player.input), player.briefing);
            complete &= player.lesson.IsFinished && player.briefing.IsComplete;
        }
        readyTimer = complete ? readyTimer + dt : 0f;
    }

    private static bool UpdateRecovery(Practice player,Vector3 local,float progress,float deltaTime)
    {
        float dt=Mathf.Max(0f,deltaTime);
        player.recoveryNoticeSeconds=Mathf.Max(0f,player.recoveryNoticeSeconds-dt);
        bool available=!player.lesson.IsFinished&&player.input.IsConnected&&!player.driveGate.IsBlocked&&
            !player.driveGate.NeedsPedalRelease&&!player.mover.IsInputSuppressed;
        if(!available)
        {
            ClearRecoveryTimers(player);player.ui.SetRecoveryNotice("");return false;
        }
        DrivingTutorialRoad.Sample(progress,out var point,out var forward);
        bool outside=Vector3.ProjectOnPlane(local-point,Vector3.up).sqrMagnitude>
            Mathf.Pow(DrivingTutorialRoad.Width*.5f+1f,2)||local.y < -2f;
        int grounded=0;foreach(var tire in player.tires)if(tire.IsGrounded)grounded++;
        bool badHeading=grounded>=2&&Vector3.Angle(Vector3.ProjectOnPlane(player.body.rotation*Vector3.forward,Vector3.up),forward)+.0001f>=CarResetter.BadHeadingDegrees;
        bool overturned=Vector3.Dot(player.body.rotation*Vector3.up,Vector3.up)<.3f;
        player.offCourseSeconds=outside?player.offCourseSeconds+dt:0f;
        player.headingSeconds=badHeading?player.headingSeconds+dt:0f;
        player.recoveryTimer=overturned?player.recoveryTimer+dt:0f;
        bool pushing=Mathf.Abs(player.driveGate.CurrentState.pedal)>CarResetter.RecoveryPedalThreshold;
        bool almostStopped=player.body.linearVelocity.sqrMagnitude<=CarResetter.StuckRecoverySpeedMps*CarResetter.StuckRecoverySpeedMps;
        bool shouldTrack=pushing||player.trackingProgress&&almostStopped;
        if(!shouldTrack){player.trackingProgress=false;player.stuckSeconds=0f;}
        else
        {
            if(!player.trackingProgress||!almostStopped&&(player.body.position-player.progressPosition).sqrMagnitude>=4f)
            {player.trackingProgress=true;player.progressPosition=player.body.position;player.stuckSeconds=0f;}
            player.stuckSeconds+=dt;
        }
        float elapsed=Mathf.Max(player.offCourseSeconds,player.headingSeconds,player.recoveryTimer,player.stuckSeconds);
        string reason=outside?"コースアウト":overturned?"車がたおれています":badHeading?"向きを戻してね":"つまり";
        player.ui.SetRecoveryNotice(player.recoveryNoticeSeconds>0f?"コースへ復帰しました":elapsed>=.6f?
            reason+" / "+Mathf.Max(1,Mathf.CeilToInt(CarResetter.AutomaticRecoveryDelay-elapsed))+"秒でコースに戻ります":"");
        // A fallen car needs immediate rescue rather than another three seconds of falling.
        bool escaped=local.y < -8f||Mathf.Abs(local.x-38f)>95f||Mathf.Abs(local.z)>92f;
        return escaped||elapsed+.0001f>=CarResetter.AutomaticRecoveryDelay;
    }
    private static void ClearRecoveryTimers(Practice player)
    {
        player.recoveryTimer=player.offCourseSeconds=player.headingSeconds=player.stuckSeconds=0f;
        player.trackingProgress=false;
    }

    public bool RecoverPlayer(int index)
    {
        Practice player = players.Find(p => p.playerIndex == index);
        if (player == null || player.lesson.IsFinished) return false;
        Recover(player); return true;
    }

    private static void Recover(Practice player)
    {
        bool collecting = player.lesson.CurrentStage == DrivingTutorialProgress.Stage.CollectItem;
        float recoveryProgress = collecting ? Mathf.Max(0f, player.itemProgress - 28f) :
            player.lesson.CurrentStage == DrivingTutorialProgress.Stage.Accelerate ? 16f : 52f;
        DrivingTutorialRoad.Sample(recoveryProgress, out Vector3 point, out Vector3 direction);
        player.body.position = player.world.transform.position + point + Vector3.up * 1.3f;
        player.body.rotation = Quaternion.LookRotation(direction);
        player.body.linearVelocity = Vector3.zero;
        player.body.angularVelocity = Vector3.zero;
        player.startYaw = 0f;
        ClearRecoveryTimers(player);player.missedItemSeconds=0f;
        player.recoveryCount++;player.recoveryNoticeSeconds=1.2f;
        player.ui.SetRecoveryNotice("コースへ復帰しました");
        player.lesson.ResetCurrentAttempt();
        player.mover.SuppressInputAfterRespawn();
        if (collecting) PlaceItems(player, player.itemProgress);
        Physics.SyncTransforms();
    }

    private static void PlaceItems(Practice player, float fixedProgress = -1f)
    {
        foreach (var pickup in player.pickups) if (pickup != null) Destroy(pickup.gameObject);
        player.pickups.Clear();
        Vector3 local = player.body.position - player.world.transform.position;
        float nearest = DrivingTutorialRoad.NearestProgress(local);
        // A fast learner can already reach 90 km/h here. Leave time to see the box
        // and the automatic-use instruction instead of spawning it under the bonnet.
        float speed = Vector3.ProjectOnPlane(player.body.linearVelocity, Vector3.up).magnitude;
        float approach = Mathf.Clamp(speed * 2.5f, 55f, 65f);
        player.itemProgress = fixedProgress >= 0f ? fixedProgress : Mathf.Min(170f, nearest + approach);
        DrivingTutorialRoad.Sample(player.itemProgress, out Vector3 center, out Vector3 tangent);
        for (int lane = -1; lane <= 1; lane++)
        {
            var go = new GameObject("Practice item / drive through a box");go.transform.SetParent(player.world.transform, false);
            go.transform.localPosition = center + Vector3.Cross(Vector3.up, tangent) * (lane * 4f);
            var pickup = go.AddComponent<TutorialItemPickup>();
            pickup.Configure(player.body, () => player.lesson.CollectItem(player.input.IsConnected));
            player.pickups.Add(pickup);
        }
        Physics.SyncTransforms();
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
        // One collider at y=0: the visual ribbon and lane markings never participate in tire contact.
        Box(root, "Safety floor", new Vector3(38f, -.5f, 0f), new Vector3(180f, 1f, 172f), ground, true);
        DrivingTutorialRoad.Build(root, asphalt, cyan, pink, white);
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
                new Vector3(8f, 8f + i % 3 * 6f, 7f), asphalt, true);
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
