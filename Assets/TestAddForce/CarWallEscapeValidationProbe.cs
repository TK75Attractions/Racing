#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(10000)]
public sealed class CarWallEscapeValidationProbe : MonoBehaviour
{
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex => 0;
        public string DeviceId => "wall-escape-validation";
        public bool IsConnected => true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    [Serializable] private sealed class Trial
    {
        public string name;
        public float maxPenetration;
        public float finalPenetration;
        public float maxPenetrationTime;
        public float retreatDistance;
        public float maxSpeed;
        public float maxReboundSpeed;
        public float maxTilt;
        public float maxForwardTravel;
        public Vector3 finalPosition;
    }
    [Serializable] private sealed class Report { public List<Trial> trials = new List<Trial>(); }
    private readonly Report report = new Report();
    private readonly Input input = new Input();
    private readonly List<Collider> walls = new List<Collider>();
    private Rigidbody body;
    private DebugMover mover;
    private BoxCollider envelope;
    private Trial trial;
    private Vector3 startingPosition;
    private Vector3 direction;
    private StreamWriter trace;
    private string folder;
    private bool recording;
    private float elapsed;
    private float penetrationRun;

    private IEnumerator Start()
    {
        folder = Environment.GetEnvironmentVariable("RACING_WALL_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "racing-wall-escape");
        Directory.CreateDirectory(folder);
        trace = new StreamWriter(Path.Combine(folder, "trace.csv"));
        trace.WriteLine("trial,time,x,y,z,speed,tilt,penetration");
        Application.runInBackground = true;
        Time.timeScale = 3f;
        var manager = UnityEngine.Object.FindFirstObjectByType<Gmanager>();
        manager.StartGame();
        while (!manager.IsDrivingEnabled) yield return null;
        var car = GameObject.Find("Player1_Car");
        body = car.GetComponent<Rigidbody>();
        mover = car.GetComponent<DebugMover>();
        mover.SetInputSource(input);
        var laps = UnityEngine.Object.FindFirstObjectByType<LapManager>();
        if (laps != null) laps.enabled = false;
        foreach (var capture in UnityEngine.Object.FindObjectsByType<PlayerAudioCapture>(FindObjectsSortMode.None)) capture.enabled = false;
        DumpGeometry(car);

        // 同じ車両を使い、コース外リスポーンに邪魔されない検証用路面を作成します。
        CreateBox("Validation floor", new Vector3(1500f, 49.5f, 1500f), new Vector3(300f, 1f, 300f));
        var proxy = new GameObject("Independent trigger body envelope for measurements");
        envelope = proxy.AddComponent<BoxCollider>();
        envelope.center = new Vector3(0f, 0.4f, 0.44f);
        envelope.size = new Vector3(2.15f, 1f, 4.72f);
        envelope.isTrigger = true;

        bool focusRecovery = Environment.GetEnvironmentVariable("RACING_WALL_FOCUSED_RECOVERY") == "1";
        if (!focusRecovery)
        foreach (bool mesh in new[] { false, true })
        foreach (float speed in new[] { 6f, 25f, 50f })
        foreach (float angle in new[] { 0f, 35f })
        {
            walls.Clear();
            Collider wall = mesh
                ? CreateMeshWall(new Vector3(1500f, 50f, 1510f))
                : CreateBox("Thin wall", new Vector3(1500f, 55f, 1510f), new Vector3(40f, 10f, 0.2f));
            walls.Add(wall);
            yield return Place(new Vector3(1500f, 50.36f, 1500f), Quaternion.Euler(0f, angle, 0f));
            yield return new WaitForSeconds(0.3f);
            body.linearVelocity = direction * speed;
            input.CurrentState = new DriveInputState { pedal = 1f };
            Begin((mesh ? "mesh" : "box") + "-" + speed + "-angle-" + angle);
            yield return new WaitForSeconds(1.8f);
            Vector3 contactPosition = body.position;
            input.CurrentState = new DriveInputState { pedal = -1f };
            yield return new WaitForSeconds(2f);
            End(contactPosition);
            Destroy(wall.gameObject);
            yield return null;
        }

        // 前後のタイヤが壁の両側に出ている状態から脱出できるか。低い壁でも床下へ落としません。
        foreach (float height in new[] { 10f, 3f })
        {
            walls.Clear();
            var splitWall = CreateBox("Wall between axles", new Vector3(1500f, 50f + height * 0.5f, 1510f),
                new Vector3(40f, height, 0.2f));
            walls.Add(splitWall);
            yield return Place(new Vector3(1500f, 50.36f, 1510f), Quaternion.identity);
            input.CurrentState = new DriveInputState { pedal = -1f };
            Begin("straddled-wall-reverse-" + height);
            Vector3 embeddedStart = body.position;
            yield return new WaitForSeconds(3f);
            End(embeddedStart);
            Destroy(splitWall.gameObject);
            yield return null;
        }

        // タイヤの間を通る細い柱で車体が貫通しないか。
        walls.Clear();
        var post = CreateBox("Post between wheels", new Vector3(1500f, 52f, 1505f), new Vector3(0.5f, 4f, 0.5f));
        walls.Add(post);
        yield return Place(new Vector3(1500f, 50.36f, 1500f), Quaternion.identity);
        input.CurrentState = new DriveInputState { pedal = 1f };
        Begin("post-between-wheels");
        yield return new WaitForSeconds(2f);
        var postContact = body.position;
        input.CurrentState = new DriveInputState { pedal = -1f };
        yield return new WaitForSeconds(2f);
        End(postContact);
        Destroy(post.gameObject);
        yield return null;

        walls.Clear();
        var cornerX = CreateBox("Corner x", new Vector3(1510f, 55f, 1500f), new Vector3(0.2f, 10f, 40f));
        var cornerZ = CreateBox("Corner z", new Vector3(1500f, 55f, 1510f), new Vector3(40f, 10f, 0.2f));
        walls.Add(cornerX); walls.Add(cornerZ);
        yield return Place(new Vector3(1500f, 50.36f, 1500f), Quaternion.Euler(0f, 45f, 0f));
        body.linearVelocity = direction * 25f;
        input.CurrentState = new DriveInputState { pedal = 1f };
        Begin("two-wall-corner");
        yield return new WaitForSeconds(2f);
        var cornerContact = body.position;
        // 衝突で車体が回った場合も、角の外へ向かう入力を選びます。壁越しの走行は求めません。
        float escapePedal = Vector3.Dot(body.rotation * Vector3.forward, direction) >= 0f ? -1f : 1f;
        input.CurrentState = new DriveInputState { pedal = escapePedal };
        yield return new WaitForSeconds(3f);
        End(cornerContact);
        Destroy(cornerX.gameObject); Destroy(cornerZ.gameObject);

        // 坂を下りた先の、実際の FBX の左右の薄い壁でも後退を確認します。
        if (!focusRecovery)
        foreach (float lane in new[] { 668f, 278f })
        foreach (bool right in new[] { false, true })
        foreach (float speed in new[] { 6f, 25f })
        {
            string plane = lane > 500f ? (right ? "Plane.018" : "Plane.017") : (right ? "Plane.023" : "Plane.034");
            var wallObject = GameObject.Find("1006こうしゃ/" + plane);
            if (wallObject == null) continue; // main の旧コースには新 FBX を入れません。
            walls.Clear();
            walls.Add(Array.Find(wallObject.GetComponents<Collider>(), candidate => candidate.enabled && !candidate.isTrigger));
            yield return Place(new Vector3(lane, -14.64f, 486f), Quaternion.LookRotation(right ? Vector3.right : Vector3.left));
            yield return new WaitForSeconds(0.3f);
            body.linearVelocity = direction * speed;
            input.CurrentState = new DriveInputState { pedal = 1f };
            Begin(plane + "-bottom-" + speed);
            yield return new WaitForSeconds(1.8f);
            var wallContact = body.position;
            input.CurrentState = new DriveInputState { pedal = -1f };
            yield return new WaitForSeconds(2f);
            End(wallContact);
        }

        File.WriteAllText(Path.Combine(folder, "report.json"), JsonUtility.ToJson(report, true));
        trace.Dispose(); trace = null;
        var failures = new List<string>();
        foreach (var t in report.trials)
        {
            if (t.name.StartsWith("straddled-wall-reverse-"))
            {
                if (t.finalPenetration > 0.05f || Mathf.Abs(t.retreatDistance) < 2f || t.finalPosition.y < 50.3f) failures.Add(t.name + ": trapped between axles");
            }
            else if (t.maxPenetration > 0.5f || t.maxPenetrationTime > 0.15f || t.finalPenetration > 0.05f || t.retreatDistance < 2f)
                failures.Add(t.name + ": body penetration or failed reverse");
            if (t.name.EndsWith("-angle-0") && t.maxReboundSpeed < 2f) failures.Add(t.name + ": missing wall reflection");
            if (t.maxSpeed > 65f || t.maxTilt > 65f) failures.Add(t.name + ": unstable collision");
        }
        string message = "CAR_WALL_ESCAPE_" + (failures.Count == 0 ? "PASS" : "FAIL") + " (" + report.trials.Count + " trials): " +
            string.Join("; ", failures) + " report=" + folder;
        File.WriteAllText(Path.Combine(folder, "result.txt"), message);
        var type = Type.GetType("CarWallEscapeValidation, Assembly-CSharp-Editor");
        bool baseline = Environment.GetEnvironmentVariable("RACING_WALL_BASELINE") == "1";
        type.GetMethod("Finish").Invoke(null, new object[] { baseline || failures.Count == 0, message });
    }

    private IEnumerator Place(Vector3 position, Quaternion rotation)
    {
        recording = false;
        input.CurrentState = DriveInputState.Neutral;
        mover.SetInputSource(input);
        body.GetComponent<CarItemEffects>()?.ClearAll();
        body.isKinematic = true;
        body.position = position; body.rotation = rotation;
        body.transform.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();
        direction = rotation * Vector3.forward;
        yield return new WaitForFixedUpdate();
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
    }

    private void Begin(string name)
    {
        trial = new Trial { name = name };
        report.trials.Add(trial);
        startingPosition = body.position;
        elapsed = 0f;
        penetrationRun = 0f;
        recording = true;
    }
    private void End(Vector3 reverseStart)
    {
        Sample();
        trial.retreatDistance = Vector3.Dot(reverseStart - body.position, direction);
        recording = false;
        trace.Flush();
    }
    private void FixedUpdate() { if (recording) Sample(); }
    private void Sample()
    {
        float penetration = 0f;
        foreach (var wall in walls)
            if (wall != null && Physics.ComputePenetration(envelope, body.position, body.rotation,
                wall, wall.transform.position, wall.transform.rotation, out _, out float distance))
                penetration = Mathf.Max(penetration, distance);
        penetrationRun = penetration > 0.05f ? penetrationRun + Time.fixedDeltaTime : 0f;
        trial.maxPenetrationTime = Mathf.Max(trial.maxPenetrationTime, penetrationRun);
        trial.maxPenetration = Mathf.Max(trial.maxPenetration, penetration);
        trial.finalPenetration = penetration;
        trial.maxForwardTravel = Mathf.Max(trial.maxForwardTravel, Vector3.Dot(body.position - startingPosition, direction));
        trial.maxSpeed = Mathf.Max(trial.maxSpeed, body.linearVelocity.magnitude);
        if (input.CurrentState.pedal > 0f)
            trial.maxReboundSpeed = Mathf.Max(trial.maxReboundSpeed, -Vector3.Dot(body.linearVelocity, direction));
        float tilt = Vector3.Angle(body.rotation * Vector3.up, Vector3.up);
        trial.maxTilt = Mathf.Max(trial.maxTilt, tilt);
        trial.finalPosition = body.position;
        elapsed += Time.fixedDeltaTime;
        var p = body.position;
        trace.WriteLine(FormattableString.Invariant($"{trial.name},{elapsed:F3},{p.x:F3},{p.y:F3},{p.z:F3},{body.linearVelocity.magnitude:F3},{tilt:F3},{penetration:F3}"));
    }
    private static BoxCollider CreateBox(string name, Vector3 position, Vector3 size)
    {
        var go = new GameObject(name); go.transform.position = position;
        var collider = go.AddComponent<BoxCollider>(); collider.size = size;
        return collider;
    }
    private static MeshCollider CreateMeshWall(Vector3 position)
    {
        var go = new GameObject("Single-sided FBX-style wall"); go.transform.position = position;
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-20f, 0f, 0f), new Vector3(-20f, 10f, 0f), new Vector3(20f, 10f, 0f), new Vector3(20f, 0f, 0f) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
        return collider;
    }
    private void DumpGeometry(GameObject car)
    {
        var text = new StringBuilder();
        foreach (var collider in car.GetComponentsInChildren<Collider>())
            text.AppendLine(collider.name + " " + collider.GetType().Name + " enabled=" + collider.enabled + " bounds=" + collider.bounds);
        foreach (var renderer in car.GetComponentsInChildren<MeshRenderer>())
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            var bounds = filter.sharedMesh.bounds;
            Vector3 minimum = Vector3.one * float.MaxValue, maximum = Vector3.one * float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                point = car.transform.InverseTransformPoint(renderer.transform.TransformPoint(point));
                minimum = Vector3.Min(minimum, point); maximum = Vector3.Max(maximum, point);
            }
            text.AppendLine(renderer.name + " visual local min=" + minimum.ToString("F3") + " max=" + maximum.ToString("F3"));
        }
        File.WriteAllText(Path.Combine(folder, "geometry.txt"), text.ToString());
    }
    private void OnDestroy() { trace?.Dispose(); }
}
#endif
