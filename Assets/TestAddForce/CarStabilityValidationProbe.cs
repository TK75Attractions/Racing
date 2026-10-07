#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Editor 専用。運転入力を与えながら実物の車両・路面・物理更新を記録します。</summary>
[DefaultExecutionOrder(10000)]
public sealed class CarStabilityValidationProbe : MonoBehaviour
{
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex => 0;
        public string DeviceId => "stability-validation";
        public bool IsConnected => true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }

    [Serializable]
    private sealed class Trial
    {
        public string name;
        public float maxTilt;
        public float maxAirTilt;
        public float maxAngularSpeed;
        public float airSeconds;
        public float finalTilt;
        public float finalSpeed;
        public float maxDriveWhileAirborne;
        public float yawChange;
        public float landingTilt;
        public bool landed;
        public int groundedWheels;
        public Vector3 finalPosition;
    }

    [Serializable] private sealed class Report { public List<Trial> trials = new List<Trial>(); }
    private readonly Report report = new Report();
    private readonly Input input = new Input();
    private Rigidbody body;
    private DebugMover mover;
    private CarStabilityController stability;
    private TireForce[] tires;
    private Trial trial;
    private Vector3 startingForward;
    private bool observedAir;
    private bool recording;
    private string folder;
    private StreamWriter trace;
    private float trialTime;
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    private IEnumerator Start()
    {
        bool recordBaseline = Environment.GetEnvironmentVariable("RACING_STABILITY_BASELINE") == "1";
        folder = Environment.GetEnvironmentVariable("RACING_STABILITY_OUTPUT") ??
            Path.Combine(Path.GetTempPath(), "racing-stability", recordBaseline ? "baseline" : "latest");
        Directory.CreateDirectory(folder);
        trace = new StreamWriter(Path.Combine(folder, "trace.csv"));
        trace.WriteLine("trial,time,x,y,z,vx,vy,vz,tilt,angularSpeed,groundedWheels,driveForce");
        Application.runInBackground = true;
        Time.timeScale = 3f;
        var manager = UnityEngine.Object.FindFirstObjectByType<Gmanager>();
        manager.StartGame();
        while (!manager.IsDrivingEnabled) yield return null;
        var car = GameObject.Find("Player1_Car");
        body = car.GetComponent<Rigidbody>();
        mover = car.GetComponent<DebugMover>();
        stability = car.GetComponent<CarStabilityController>();
        tires = car.GetComponentsInChildren<TireForce>();
        mover.SetInputSource(input);
        // 実機への音声出力は検証には不要です。
        foreach (var capture in UnityEngine.Object.FindObjectsByType<PlayerAudioCapture>(FindObjectsSortMode.None))
            capture.enabled = false;

        // 新コース専用の坂の座標です。main の旧コースでは専用路面の車両テストを実行します。
        bool hasNewCourse = false;
        foreach (var collider in UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
            if (AssetDatabase.GetAssetPath(collider.sharedMesh) == "Assets/Scenes/1006こうしゃ.fbx")
                hasNewCourse = true;
        if (hasNewCourse)
        foreach (float x in new[] { 668f, 278f })
        foreach (float speed in new[] { 6f, 15f, 25f })
        foreach (bool downhill in new[] { false, true })
        {
            string name = (x > 500f ? "east" : "west") + "-" + speed + (downhill ? "-down" : "-up");
            Vector3 direction = downhill ? Vector3.back : Vector3.forward;
            yield return Place(new Vector3(x, downhill ? 0.36f : -14.64f, downhill ? 540f : 490f), Quaternion.LookRotation(direction));
            yield return new WaitForSeconds(0.3f);
            body.linearVelocity = direction * speed;
            body.angularVelocity = Vector3.zero;
            input.CurrentState = new DriveInputState { pedal = 1f };
            Begin(name);
            yield return new WaitForSeconds(5f);
            End();
        }

        // 専用路面ではコース外リスポーンを停止し、姿勢復元がワープで置き換わらないようにします。
        var lapManager = UnityEngine.Object.FindFirstObjectByType<LapManager>();
        if (lapManager != null) lapManager.enabled = false;

        // 平坦な広い路面で飛行と着地を分離して検証します。
        // 実際のコースの障害物やアイテムが着地だけの比較へ混ざらないよう、同じシーンに検証路面を作ります。
        var landingFloor = new GameObject("Stability validation landing surface");
        landingFloor.transform.position = new Vector3(1500f, 49.5f, 1500f);
        landingFloor.AddComponent<BoxCollider>().size = new Vector3(300f, 1f, 300f);
        Physics.SyncTransforms();
        Vector3 floor = new Vector3(1500f, 50.36f, 1500f);
        Set(stability, "enableUprightAssist", false);
        yield return Place(floor + Vector3.up * 0.5f, Quaternion.identity);
        body.useGravity = false;
        input.CurrentState = new DriveInputState { pedal = 1f };
        Begin("hover-no-contact");
        yield return new WaitForSeconds(0.25f);
        End();
        body.useGravity = true;
        Set(stability, "enableUprightAssist", true);

        foreach (Vector3 rotation in new[] { new Vector3(-25f, 0f, 20f), new Vector3(40f, 0f, -35f), new Vector3(0f, 0f, 180f) })
        foreach (float pedal in new[] { 1f, -1f })
        {
            yield return Place(floor + Vector3.up * 8f, Quaternion.Euler(rotation));
            body.linearVelocity = new Vector3(0f, 5f, 12f);
            body.angularVelocity = new Vector3(2f, 0f, 1.5f);
            input.CurrentState = new DriveInputState { pedal = pedal };
            Begin("jump-" + rotation.x + "-" + rotation.z + "-pedal-" + pedal);
            yield return new WaitForSeconds(4f);
            End();
        }

        // 補助を切った完全な裏返し状態では、前進・後退どちらもタイヤが力を出してはいけません。
        Set(stability, "enableUprightAssist", false);
        foreach (float pedal in new[] { 0f, 1f, -1f })
        {
            yield return Place(floor, Quaternion.Euler(0f, 0f, 180f));
            yield return new WaitForSeconds(0.3f);
            input.CurrentState = new DriveInputState { pedal = pedal };
            Begin("inverted-assist-off-" + pedal);
            yield return new WaitForSeconds(1f);
            End();
        }
        Set(stability, "enableUprightAssist", true);
        foreach (float pedal in new[] { 0f, -1f })
        {
            yield return Place(floor, Quaternion.Euler(0f, 0f, 180f));
            input.CurrentState = new DriveInputState { pedal = pedal };
            Begin("inverted-recovery-" + pedal);
            yield return new WaitForSeconds(4f);
            End();
        }

        // 横滑り・駆動が傾いた車体へ転倒トルクを生まないことと、操舵が残ること。
        foreach (float pedal in new[] { 1f, -1f })
        {
            yield return Place(floor, Quaternion.identity);
            yield return new WaitForSeconds(0.3f);
            input.CurrentState = new DriveInputState { pedal = pedal, steering = 12f };
            Begin("flat-steering-" + pedal);
            yield return new WaitForSeconds(2f);
            End();
        }

        // yaw の角速度だけは補正で消さない（地面から十分離した状態）。
        yield return Place(floor + Vector3.up * 80f, Quaternion.identity);
        body.angularVelocity = Vector3.up;
        Begin("air-yaw");
        yield return new WaitForSeconds(0.5f);
        End();

        File.WriteAllText(Path.Combine(folder, "report.json"), JsonUtility.ToJson(report, true));
        trace.Dispose();
        trace = null;
        bool baseline = recordBaseline;
        var failures = new List<string>();
        foreach (var result in report.trials)
        {
            if (result.name.StartsWith("jump-") && (!result.landed || result.finalTilt > 12f || result.landingTilt > 40f))
                failures.Add(result.name + ": unstable landing");
            if (result.name.StartsWith("inverted-assist-off-") && (result.groundedWheels != 0 || result.maxDriveWhileAirborne > 0.001f || result.finalTilt < 175f))
                failures.Add(result.name + ": inverted wheels applied force");
            if (result.name.StartsWith("inverted-recovery-") && result.finalTilt > 12f)
                failures.Add(result.name + ": failed recovery");
            if (result.name.StartsWith("flat-steering-") && (result.maxTilt > 15f || result.yawChange < 5f || result.finalSpeed < 1f))
                failures.Add(result.name + ": steering or stability regression");
            if (result.name == "air-yaw" && (result.yawChange < 20f || result.maxTilt > 1f))
                failures.Add("air-yaw: yaw was damped or tilt was introduced");
            if (result.name == "hover-no-contact" && (result.groundedWheels != 0 || result.finalSpeed > 0.001f || result.maxDriveWhileAirborne > 0.001f))
                failures.Add("hover-no-contact: driving force without contact");
            if ((result.name.StartsWith("east-") || result.name.StartsWith("west-")) &&
                (result.maxTilt > 75f || result.finalTilt > 12f || !result.landed || result.yawChange > 30f))
                failures.Add(result.name + ": unstable ramp orientation");
        }
        string message = "CAR_STABILITY_PLAYMODE_" + (failures.Count == 0 ? "PASS" : "FAIL") +
            " (" + report.trials.Count + " trials): " + string.Join("; ", failures) + " report=" + folder;
        // Baseline は失敗数も保存して比較しますが、正常終了させます。
        File.WriteAllText(Path.Combine(folder, "result.txt"), message);
        Finish(baseline || failures.Count == 0, message);
    }

    private IEnumerator Place(Vector3 position, Quaternion rotation)
    {
        recording = false;
        input.CurrentState = DriveInputState.Neutral;
        mover.SetInputSource(input); // ドリフト／加速の残りを破棄。
        body.GetComponent<CarItemEffects>()?.ClearAll();
        body.isKinematic = true;
        body.position = position;
        body.rotation = rotation;
        body.transform.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    private void Begin(string name)
    {
        trial = new Trial { name = name };
        report.trials.Add(trial);
        startingForward = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, Vector3.up).normalized;
        observedAir = false;
        trialTime = 0f;
        recording = true;
    }

    private void End()
    {
        Sample();
        recording = false;
        trace.Flush();
    }

    private void FixedUpdate()
    {
        if (recording) Sample();
    }

    private void Sample()
    {
        float tilt = Vector3.Angle(body.rotation * Vector3.up, Vector3.up);
        int grounded = 0;
        float drive = 0f;
        foreach (var tire in tires)
        {
            if (tire.GetComponent<GroundCheck>().CheckNow()) grounded++;
            drive += Mathf.Abs((float)typeof(TireForce).GetField("appliedDriveForce", Fields).GetValue(tire));
        }
        trialTime += Time.fixedDeltaTime;
        trial.maxTilt = Mathf.Max(trial.maxTilt, tilt);
        trial.maxAngularSpeed = Mathf.Max(trial.maxAngularSpeed, body.angularVelocity.magnitude);
        if (grounded == 0)
        {
            trial.airSeconds += Time.fixedDeltaTime;
            trial.maxAirTilt = Mathf.Max(trial.maxAirTilt, tilt);
            trial.maxDriveWhileAirborne = Mathf.Max(trial.maxDriveWhileAirborne, drive);
            observedAir = true;
        }
        else if (observedAir && !trial.landed)
        {
            trial.landed = true;
            trial.landingTilt = tilt;
        }
        trial.finalTilt = tilt;
        trial.finalSpeed = body.linearVelocity.magnitude;
        trial.finalPosition = body.position;
        trial.groundedWheels = grounded;
        Vector3 forward = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, Vector3.up).normalized;
        trial.yawChange = Vector3.Angle(startingForward, forward);
        var p = body.position;
        var v = body.linearVelocity;
        trace.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0},{1:F3},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7:F3},{8:F3},{9:F3},{10},{11:F3}",
            trial.name, trialTime, p.x, p.y, p.z, v.x, v.y, v.z, tilt, body.angularVelocity.magnitude, grounded, drive));
    }

    private static void Set(object instance, string field, object value) =>
        instance.GetType().GetField(field, Fields).SetValue(instance, value);

    // Assembly-CSharp は Editor assembly を参照できないため通知だけを公開メソッドへ渡します。
    private static void Finish(bool passed, string message)
    {
        var editorType = Type.GetType("CarStabilityValidation, Assembly-CSharp-Editor");
        editorType.GetMethod("Finish").Invoke(null, new object[] { passed, message });
    }

    private void OnDestroy()
    {
        trace?.Dispose();
    }
}
#endif
