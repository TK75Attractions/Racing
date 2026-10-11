#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>Actual car2 Prefabs driven using pedal and steering only; no velocity or pose correction.</summary>
[DefaultExecutionOrder(10000)]
public sealed class CourseJumpPlaythroughProbe : MonoBehaviour
{
    private sealed class Input : IDriveInputSource
    {
        public int PlayerIndex { get; set; }
        public string DeviceId => "course-playthrough";
        public bool IsConnected => true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }

    [Serializable] private sealed class Trial
    {
        public string player;
        public float distance;
        public float maximumHeight;
        public float maximumTilt;
        public float airborneSeconds;
        public float maximumUnexpectedAirborneSeconds;
        public bool tookOff;
        public bool landed;
        public Vector3 takeoffPosition;
        public Vector3 landingPosition;
        public int wallContacts;
        public string lastWall;
        public Vector3 finalPosition;
    }

    [Serializable] private sealed class Report
    {
        public List<Trial> trials = new List<Trial>();
        public float courseLength;
        public float simulatedSeconds;
        public bool offCoursePositionPreserved;
        public float offCourseSeconds;
        public string error;
    }

    private sealed class Driver
    {
        public Rigidbody body;
        public DebugMover mover;
        public TireForce[] tires;
        public Input input;
        public Trial trial;
        public float previousProgress;
        public Vector3 previousPosition;
        public float stalledSeconds;
        public float unexpectedAirborneSeconds;
    }

    private readonly List<Driver> drivers = new List<Driver>();
    private readonly Report report = new Report();
    private RaceCourse course;
    private bool driving;
    private bool failed;
    private string output;
    private StreamWriter trace;
    private float directionSign;
    private float elapsed;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private IEnumerator Start()
    {
        output = Environment.GetEnvironmentVariable("RACING_COURSE_PLAYTHROUGH_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "racing-course-playthrough");
        Directory.CreateDirectory(output);
        trace = new StreamWriter(Path.Combine(output, "trace.csv"));
        trace.WriteLine("player,time,x,y,z,speed,steering,pedal,grounded,distance,wall");
        Time.timeScale = 3f;
        Application.runInBackground = true;
        Gmanager manager = UnityEngine.Object.FindFirstObjectByType<Gmanager>();
        RaceRemoteFirebasePublisher publisher = manager.GetComponent<RaceRemoteFirebasePublisher>();
        if (publisher != null) publisher.enabled = false;
        manager.StartGame();
        while (manager.state != Gmanager.State.Tutorial || TransitionActive()) yield return null;
        typeof(Gmanager).GetMethod("FinishTutorialWhenScreenCovered", Private).Invoke(manager, null);
        typeof(Gmanager).GetMethod("CompleteGameStart", Private).Invoke(manager, null);
        foreach (ScreenTransitionController transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))
            transition.ApplyStateImmediate(Gmanager.State.Countdown);
        while (manager.state != Gmanager.State.Game || !manager.IsDrivingEnabled) yield return null;
        course = UnityEngine.Object.FindFirstObjectByType<RaceCourse>();
        report.courseLength = course.TotalLength;
        LapManager laps = UnityEngine.Object.FindFirstObjectByType<LapManager>();
        directionSign = laps.IsProgressReversed ? -1f : 1f;
        for (int index = 0; index < 2; index++)
        {
            GameObject car = GameObject.Find("Player" + (index + 1) + "_Car");
            Driver driver = new Driver
            {
                body = car.GetComponent<Rigidbody>(), mover = car.GetComponent<DebugMover>(),
                tires = car.GetComponentsInChildren<TireForce>(), input = new Input { PlayerIndex = index },
                trial = new Trial { player = car.name }
            };
            driver.previousProgress = course.GetProgressDistance(driver.body.position);
            driver.previousPosition = driver.body.position;
            driver.mover.SetInputSource(driver.input);
            car.AddComponent<CourseJumpContactRecorder>().Owner = this;
            drivers.Add(driver); report.trials.Add(driver.trial);
            foreach (PlayerAudioCapture capture in car.GetComponentsInChildren<PlayerAudioCapture>()) capture.enabled = false;
        }
        yield return new WaitForSeconds(0.5f);
        driving = true;
        while (!failed && elapsed < 180f && drivers.Exists(driver => driver.trial.distance < course.TotalLength)) yield return null;
        driving = false;
        report.simulatedSeconds = elapsed;
        foreach (Driver driver in drivers)
        {
            driver.input.CurrentState = DriveInputState.Neutral;
            driver.trial.finalPosition = driver.body.position;
            if (driver.trial.distance < course.TotalLength || !driver.trial.tookOff || !driver.trial.landed || driver.trial.wallContacts != 0)
                Fail(driver.trial.player + " did not complete a clean lap with jump and landing.");
        }
        if (!failed)
        {
            Rigidbody body = drivers[1].body;
            body.isKinematic = true;
            Vector3 outside = new Vector3(800f, 8f, 850f);
            body.position = outside;
            yield return new WaitForSeconds(4f);
            LapManager.CarTimeData data = laps.GetCarData(body);
            report.offCourseSeconds = data.offCourseTimer;
            report.offCoursePositionPreserved = Vector3.Distance(body.position, outside) < 0.01f && data.isOffCourse && data.offCourseTimer >= 3f;
            if (!report.offCoursePositionPreserved) Fail("Car was returned from outside course.");
        }
        File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
        trace.Dispose(); trace = null;
        Type editor = Type.GetType("CourseJumpPlaythroughValidation, Assembly-CSharp-Editor");
        editor.GetMethod("Finish").Invoke(null, new object[] { !failed, failed ? report.error : "COURSE_JUMP_PLAYTHROUGH_PASS: both actual cars complete a lap, jump/land, no wall contacts, no off-course respawn after 4 seconds." });
    }

    private void FixedUpdate()
    {
        if (!driving) return;
        elapsed += Time.fixedDeltaTime;
        for (int index = 0; index < drivers.Count; index++)
        {
            Driver driver = drivers[index];
            Rigidbody body = driver.body;
            float progress = course.GetProgressDistance(body.position);
            float delta = Mathf.Repeat(directionSign * (progress - driver.previousProgress) + course.TotalLength * 0.5f, course.TotalLength) - course.TotalLength * 0.5f;
            driver.trial.distance += delta;
            driver.previousProgress = progress;
            if (Vector3.Distance(body.position, driver.previousPosition) > 10f) Fail("Unexpected teleport of " + driver.trial.player);
            driver.previousPosition = body.position;
            driver.trial.maximumHeight = Mathf.Max(driver.trial.maximumHeight, body.position.y);
            driver.trial.maximumTilt = Mathf.Max(driver.trial.maximumTilt, Vector3.Angle(body.rotation * Vector3.up, Vector3.up));
            if (driver.trial.maximumTilt > 80f) Fail(driver.trial.player + " overturned during the trial.");
            int grounded = 0; foreach (TireForce tire in driver.tires) if (tire.IsGrounded) grounded++;
            bool overJump = body.position.x > 605f && body.position.x < 623f &&
                body.position.z > 590f && body.position.z < 606f && body.position.y > 14f;
            driver.unexpectedAirborneSeconds = grounded == 0 && !overJump
                ? driver.unexpectedAirborneSeconds + Time.fixedDeltaTime : 0f;
            driver.trial.maximumUnexpectedAirborneSeconds = Mathf.Max(driver.trial.maximumUnexpectedAirborneSeconds,
                driver.unexpectedAirborneSeconds);
            if (driver.unexpectedAirborneSeconds > .3f) Fail(driver.trial.player + " became airborne outside the intended jump.");
            if (body.position.x > 548f && body.position.x < 680f && body.position.z < 625f && body.position.z > 550f)
            {
                if (grounded == 0 && body.position.y > 10f)
                {
                    if (!driver.trial.tookOff) driver.trial.takeoffPosition = body.position;
                    driver.trial.tookOff = true; driver.trial.airborneSeconds += Time.fixedDeltaTime;
                }
                if (driver.trial.tookOff && !driver.trial.landed && grounded >= 2 && body.position.x > 566f && body.position.y < 18f)
                {
                    driver.trial.landed = true; driver.trial.landingPosition = body.position;
                }
            }
            float speed = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
            driver.stalledSeconds = speed < 0.1f && driver.trial.distance > 20f ? driver.stalledSeconds + Time.fixedDeltaTime : 0f;
            if (driver.stalledSeconds > 3f)
            {
                foreach (TireForce tire in driver.tires)
                    Debug.Log($"STALLED_WHEEL {driver.trial.player} front={tire.IsFrontWheel} grounded={tire.IsGrounded} hit={tire.GroundHit.collider?.name} normal={tire.GroundHit.normal} position={tire.transform.position} force={typeof(TireForce).GetField("appliedDriveForce", Private).GetValue(tire)}");
                Debug.Log($"STALLED_BODY rotation={body.rotation.eulerAngles} pedal={driver.mover.PedalInput} constraints={body.constraints} suppressed={driver.mover.IsInputSuppressed} enabled={driver.mover.enabled}");
                Fail(driver.trial.player + " stalled on course.");
            }
            float lookAhead = Mathf.Clamp(5f + speed * 0.4f, 8f, 17f);
            course.TryGetSampleAtProgress(progress + directionSign * lookAhead, out RaceCourse.CourseSample target);
            course.TryGetSampleAtProgress(progress + directionSign * 25f, out RaceCourse.CourseSample far);
            float bend = Vector3.Angle(target.forward, far.forward);
            float targetSpeed = bend > 15f ? 14f : 25f;
            if (body.position.x > 538f && body.position.x < 555f && body.position.z > 590f && body.position.z < 607f) targetSpeed = 24f;
            Vector3 aim = target.position + target.right * (index == 0 ? 2f : -2f) - body.position;
            Vector3 local = Quaternion.Inverse(body.rotation) * aim;
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float steering = Mathf.Clamp(angle * 1.5f - body.angularVelocity.y * 8f, -30f, 30f);
            float pedal = Mathf.Clamp((targetSpeed - speed) * 0.12f, -1f, 1f);
            if (grounded == 0 && driver.trial.tookOff && !driver.trial.landed) { steering = 0f; pedal = 0f; }
            driver.input.CurrentState = new DriveInputState { pedal = pedal, steering = steering };
            trace.WriteLine(FormattableString.Invariant($"{driver.trial.player},{elapsed:F2},{body.position.x:F3},{body.position.y:F3},{body.position.z:F3},{speed:F3},{steering:F2},{pedal:F2},{grounded},{driver.trial.distance:F2},{driver.trial.lastWall}"));
            if (body.position.y < -35f) Fail(driver.trial.player + " fell below the course.");
        }
    }

    public void Contact(GameObject car, Collision collision)
    {
        if (!driving || collision.collider.GetComponentInParent<RaceCourse>() != null || collision.collider.GetComponentInParent<Rigidbody>() != null) return;
        foreach (ContactPoint contact in collision.contacts)
        {
            if (contact.normal.y >= 0.5f || collision.impulse.magnitude < 0.2f) continue;
            Driver driver = drivers.Find(candidate => candidate.body.gameObject == car);
            if (driver == null) return;
            driver.trial.wallContacts++; driver.trial.lastWall = collision.collider.name;
            Debug.Log("COURSE_WALL_CONTACT " + car.name + " " + collision.collider.name + " " + car.transform.position);
            Fail(car.name + " touched " + collision.collider.name);
            return;
        }
    }

    private void Fail(string message)
    {
        if (failed) return;
        failed = true; report.error = message; Debug.LogError(message);
    }

    private static bool TransitionActive()
    {
        foreach (ScreenTransitionController transition in UnityEngine.Object.FindObjectsByType<ScreenTransitionController>(FindObjectsSortMode.None))
            if (transition.IsTransitioning) return true;
        return false;
    }

    private void OnDestroy() => trace?.Dispose();
}

public sealed class CourseJumpContactRecorder : MonoBehaviour
{
    public CourseJumpPlaythroughProbe Owner;
    private void OnCollisionEnter(Collision collision) => Owner?.Contact(gameObject, collision);
}
#endif
