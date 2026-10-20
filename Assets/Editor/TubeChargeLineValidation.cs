using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Actual SampleScene tube geometry with a controlled wheel-contact fixture.</summary>
public static class TubeChargeLineValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex => 0;
        public string DeviceId => "tube-line-validation";
        public bool IsConnected => true;
        public DriveInputState CurrentState => new DriveInputState { pedal = 1 };
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    [MenuItem("Racing/Validation/Tube Charge Lines")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        RaceCourse course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        course.RebuildRoad(); Physics.SyncTransforms();
        Gmanager previous = Gmanager.Control;
        var root = new GameObject("Tube line validation"); root.SetActive(false);
        TubeChargeLineLayout layout = null;
        try
        {
            Gmanager race = root.AddComponent<Gmanager>(); race.course = course; Gmanager.Control = race;
            RaceFeatureController features = root.AddComponent<RaceFeatureController>(); features.Configure(race);
            Set(race, "<Features>k__BackingField", features); race.state = Gmanager.State.Game;
            var car = new GameObject("Line driver"); car.transform.SetParent(root.transform);
            Rigidbody body = car.AddComponent<Rigidbody>(); body.useGravity = false;
            for (int i = 0; i < 4; i++)
            {
                var wheel = new GameObject("Wheel"); wheel.transform.SetParent(car.transform, false);
                wheel.transform.localPosition = new Vector3(i % 2 == 0 ? -.7f : .7f, 0, i < 2 ? 1 : -1);
                wheel.AddComponent<SphereCollider>().radius = .35f;
                TireForce tire = wheel.AddComponent<TireForce>(); Set(tire, "isFrontTire", i < 2);
                Invoke(wheel.GetComponent<GroundCheck>(), "Awake"); Invoke(tire, "Awake");
            }
            DebugMover mover = car.AddComponent<DebugMover>(); Invoke(mover, "Awake"); mover.SetInputSource(new Input());
            var own = new RaceParticipant { Index = 0, Car = car, Body = body };
            ((List<RaceParticipant>)typeof(Gmanager).GetField("participants", Private).GetValue(race)).Add(own);
            TubeChargeLineController controller = car.AddComponent<TubeChargeLineController>(); controller.Configure(race, own);
            layout = controller.Layout; root.SetActive(true); Physics.SyncTransforms();
            Require(layout.Available && layout.VisualsVisible, "The actual scene must contain one safe optional tube-line section.");
            for (int pass = 0; pass < 2; pass++)
                for (int d = 0; d <= 19; d++)
                {
                    course.TryGetSampleAtProgress(layout.EntryProgress + d, out RaceCourse.CourseSample sample);
                    car.transform.SetPositionAndRotation(sample.position + sample.right * layout.LaneOffset + sample.up * .35f,
                        Quaternion.LookRotation(sample.forward, sample.up));
                    body.position = car.transform.position; body.rotation = car.transform.rotation;
                    body.linearVelocity = sample.forward * 10f; Physics.SyncTransforms(); controller.Tick(.05f);
                }
            Require(controller.SuccessfulLines == 1 && Mathf.Abs(mover.NormalizedDriftCharge - .25f) < .001f && controller.Notice.Length > 0,
                "A grounded side-line sequence adds 25% charge once; repeating it in the same lap cannot farm charge.");
            string output = Environment.GetEnvironmentVariable("RACING_CHALLENGE_VALIDATION_OUTPUT") ?? "/private/tmp/racing-challenge-geometry";
            System.IO.Directory.CreateDirectory(output);
            course.TryGetSampleAtProgress(layout.EntryProgress + 9f, out RaceCourse.CourseSample middle);
            var cameraObject = new GameObject("Line preview camera"); cameraObject.transform.SetParent(root.transform);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = middle.position + middle.up * 12f - middle.forward * 14f;
            camera.transform.rotation = Quaternion.LookRotation(middle.position + middle.forward * 4f - camera.transform.position, middle.up);
            camera.nearClipPlane = .1f; camera.farClipPlane = 180; camera.fieldOfView = 65;
            var target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
            camera.Render(); RenderTexture old = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(output, "tube-lines.png"), image.EncodeToPNG());
            RenderTexture.active = old; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
            features.SetFeature(RaceFeature.TubeChargeLines, false);
            Require(!layout.VisualsVisible && !controller.Pending && controller.Notice.Length == 0 && mover.NormalizedDriftCharge > .24f,
                "OFF hides visuals and clears pending notices without deleting already-earned normal charge.");
            features.SetFeature(RaceFeature.TubeChargeLines, true);
            Require(layout.VisualsVisible, "ON restores only the optional visual lines.");
            string result = "TUBE_CHARGE_LINE_VALIDATION_PASS: actual tube/ground geometry, entry=" + layout.EntryProgress.ToString("F2") +
                ", laneOffset=" + layout.LaneOffset.ToString("F2") + ", continuous contact sequence, 25% reward, no repeat farming, live OFF/ON.";
            Debug.Log(result); System.IO.File.WriteAllText(System.IO.Path.Combine(output, "validation.txt"), result + "\n");
            RacingHUDFontBuilder.Build();
            Require(RacingUIFontCatalog.GetHUD(false).HasCharacters("ライン成功！チャージ増加"), "Tube notice glyphs must exist.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            if (layout != null) { UnityEngine.Object.DestroyImmediate(layout.gameObject.transform.Find("Optional tube charge lines")?.gameObject); UnityEngine.Object.DestroyImmediate(layout); }
            Gmanager.Control = previous;
        }
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
