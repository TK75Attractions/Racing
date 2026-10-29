using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class OperatorChangesValidation
{
    private const string Key = "Racing.OperatorChangesQA";
    private const string Folder = "Artifacts/OperatorChanges";
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static int stage, received;
    private static double began;
    private static RenderTexture target;
    private static string failure;
    static OperatorChangesValidation() { began = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; }

    [MenuItem("Racing/Validation/Audio and Starting Grid %#j")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Directory.CreateDirectory(Folder);
        File.Delete(Folder + "/failure.txt");
        PlayerAudioValidation.Run();
        AudioBufferingValidation.Run();
        MaintenanceMusicValidation.Run();
        ValidateGridFixture();
        SessionState.SetBool(Key, true);
        stage = received = 0; began = EditorApplication.timeSinceStartup; failure = null;
        EditorApplication.isPlaying = true;
    }

    public static void RunUpdaterPreflight()
    {
        Directory.CreateDirectory(Folder);
        var root = EditorUtility.CreateGameObjectWithHideFlags("Update preflight QA", HideFlags.HideAndDontSave); root.SetActive(false);
        string dirty = Path.GetFullPath("operator-preflight-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(dirty, "Intentional untracked file for updater guard validation.");
        try
        {
            var race = root.AddComponent<Gmanager>(); race.state = Gmanager.State.Title;
            var updater = root.AddComponent<RaceOperatorUpdate>(); updater.Configure(race);
            Require(updater.Request(), "Unity could not launch the external updater: " + updater.Status);
            string folder = (string)typeof(RaceOperatorUpdate).GetField("requestFolder", Private).GetValue(updater);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (updater.Busy && watch.Elapsed.TotalSeconds < 15)
            { System.Threading.Thread.Sleep(50); updater.Poll(); }
            Require(!updater.Busy && File.Exists(Path.Combine(folder, "failed")), "Dirty-repo preflight must stop and report failure.");
            Require(!File.Exists(Path.Combine(folder, "ready")), "Dirty-repo preflight must never close Unity.");
            File.WriteAllText(Folder + "/updater-preflight.txt", "PASS: real Unity runtime launches external updater with quoted paths, dirty-repo preflight fails, no shutdown marker, Unity remains open.\n");
            Debug.Log("OPERATOR_UPDATER_PREFLIGHT_PASS");
        }
        finally { File.Delete(dirty); UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void ValidateGridFixture()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var car = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/car/car2.prefab"));
        try
        {
            ground.transform.position = new Vector3(-5000, 4999.5f, -5000);
            ground.transform.localScale = new Vector3(100, 1, 100);
            car.transform.position = new Vector3(-5000, 5003, -5000);
            car.transform.rotation = Quaternion.identity;
            Require(RaceGridPlacement.Ground(car), "Grid placement could not find solid road.");
            foreach (var wheel in car.GetComponentsInChildren<TireForce>())
                Require(wheel.GetComponent<GroundCheck>().CheckNow(), "Grid wheel must touch road before simulation.");
            float height = car.transform.position.y;
            Require(RaceGridPlacement.Ground(car) && Mathf.Abs(car.transform.position.y - height) < .001f, "Ground placement must be stable when repeated.");
            ground.SetActive(false);
            Require(!RaceGridPlacement.Ground(car), "Missing road must leave the spawn untouched.");
            Require(Mathf.Abs(car.transform.position.y - height) < .001f, "Missing road changed the car height.");
        }
        finally { UnityEngine.Object.DestroyImmediate(car); UnityEngine.Object.DestroyImmediate(ground); }
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) return;
        try
        {
            if (EditorApplication.timeSinceStartup - began > 100) throw new Exception("Starting grid QA timed out at stage " + stage);
            if (failure != null) throw new Exception(failure);
            if (!EditorApplication.isPlaying || Gmanager.Control == null) return;
            var manager = Gmanager.Control;
            foreach (var publisher in UnityEngine.Object.FindObjectsByType<RaceRemoteFirebasePublisher>(FindObjectsSortMode.None)) publisher.enabled = false;
            if (stage == 0 && Time.frameCount > 20)
            {
                if (manager.IsMaintenanceActive) manager.SetMaintenance(false);
                manager.Features.SetValidationFeature(RaceFeature.CourseIntro, false);
                manager.StartAutodriveTestRace(true);
                Require(manager.Participants.Count == 4, "Both human cars and both CPUs must spawn.");
                foreach (var entry in manager.Participants)
                foreach (var wheel in entry.Car.GetComponentsInChildren<TireForce>())
                    Require(wheel.GetComponent<GroundCheck>().CheckNow(), entry.Car.name + " is floating before the countdown.");
                var rigs = (PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs", Private).GetValue(manager);
                for (int player = 0; player < 2; player++)
                {
                    Vector3 lens = rigs[player].RaceCamera.transform.position;
                    foreach (var entry in manager.Participants)
                    {
                        if (!entry.IsCpu) continue;
                        foreach (var collider in entry.Car.GetComponentsInChildren<Collider>())
                            if (collider.enabled && !collider.isTrigger)
                                Require(Vector3.Distance(collider.ClosestPoint(lens), lens) > .5f, "Starting chase lens overlaps CPU body.");
                    }
                }
                target = new RenderTexture(1280, 720, 24); target.Create();
                foreach (var camera in new[] { rigs[1].BackCamera, rigs[1].BackImageCamera, rigs[1].MainCamera, rigs[1].FrontCamera, rigs[1].UiCamera })
                    if (camera != null) camera.targetTexture = target;
                stage = 1; began = EditorApplication.timeSinceStartup;
            }
            else if (stage == 1 && EditorApplication.timeSinceStartup - began > 1)
            {
                stage = 2;
                for (int player = 0; player < 2; player++)
                {
                    int index = player;
                    manager.RequestDashboardPlayerFrame(player, bytes => {
                        try
                        {
                            Require(bytes != null && bytes.Length > 1000, "Missing rendered player frame.");
                            File.WriteAllBytes(Folder + "/start-p" + (index + 1) + ".jpg", bytes);
                            var image = new Texture2D(2, 2);
                            try
                            {
                                Require(image.LoadImage(bytes), "Cannot decode captured frame.");
                                int visible = 0, count = 0;
                                for (int y = image.height * 3 / 10; y < image.height * 6 / 10; y += 4)
                                for (int x = image.width * 3 / 10; x < image.width * 7 / 10; x += 4)
                                { if (image.GetPixel(x, y).maxColorComponent > .1f) visible++; count++; }
                                Require(visible > count / 20, "Player world view is black.");
                            }
                            finally { UnityEngine.Object.Destroy(image); }
                            received++;
                        }
                        catch (Exception error) { failure = error.ToString(); }
                    });
                }
            }
            else if (stage == 2 && received == 2)
            {
                File.WriteAllText(Folder + "/verification.txt", "PASS: opening/finish/results routing; shared DSP sample positions and late joins across loops; maintenance regression; static grid placement and missing-road handling; four grounded cars before countdown; both chase lenses clear of CPUs; rendered P1/P2 world views contain visible pixels.\n");
                Finish(null);
            }
        }
        catch (Exception error) { Finish(error.ToString()); }
    }

    private static void Finish(string error)
    {
        SessionState.SetBool(Key, false);
        if (error == null) Debug.Log("OPERATOR_CHANGES_VALIDATION_PASS");
        else { File.WriteAllText(Folder + "/failure.txt", error); Debug.LogError(error); }
        EditorApplication.isPlaying = false;
        if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); target = null; }
        if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
