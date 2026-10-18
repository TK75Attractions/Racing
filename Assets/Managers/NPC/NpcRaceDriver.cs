using UnityEngine;
using System.Collections.Generic;

/// <summary>Physical pedal/steering driver. Never writes pose or velocity during normal driving.</summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(DebugMover), typeof(Rigidbody))]
public sealed class NpcRaceDriver : MonoBehaviour, IDriveInputSource
{
    [SerializeField] private float cruiseSpeed = 25f;
    [SerializeField] private float maximumCatchup = 5f;
    [SerializeField] private float maximumSlowdown = 25f;
    [SerializeField] private float accelerationSlew = 3f;
    private Gmanager manager;
    private RaceCourse course;
    private LapManager laps;
    private Rigidbody body;
    private TireForce[] tires;
    private RaceParticipant entry;
    private float targetSpeed = 25f, lane, desiredLane, nextDecision, stuck, reverseUntil, yieldUntil;
    private int rivalIndex;
    private bool driving;
    private int groundedTires;
    private float safeRawProgress, safeRaceProgress;
    private LapManager.NpcRecoveryState safeState;
    private bool hasSafeState;
    private sealed class ContactGrace { public Rigidbody Other; public Collider[] Colliders; public float Until; }
    private Collider[] ownColliders;
    private readonly List<ContactGrace> contactGrace = new List<ContactGrace>(2);
    private readonly Collider[] overlaps = new Collider[32];
    public int PlayerIndex => -1;
    public string DeviceId => "CPU";
    public bool IsConnected => true;
    public DriveInputState CurrentState { get; private set; }
    public int RivalIndex => rivalIndex;
    public int RecoveryCount { get; private set; }
    public float TargetSpeed => targetSpeed * 3.6f;
    public bool HasContactGrace => contactGrace.Count > 0;
    // Read-only diagnostics use the same decisions as the physical driver.
    public string DrivingStatus => !driving ? "Stopped" :
        groundedTires == 0 ? "Airborne" : Time.time < reverseUntil ? "Reversing" :
        Time.time < yieldUntil ? "Yielding" : stuck > .5f ? "Stalled" : "Driving";
    public void UpdateInput(float dt) { }
    public void Dispose() { }

    public void Configure(Gmanager race, LapManager lapManager, RaceParticipant participant)
    {
        manager = race; course = race.course; laps = lapManager; entry = participant;
        body = GetComponent<Rigidbody>(); body.interpolation = RigidbodyInterpolation.Interpolate; ownColliders = GetComponentsInChildren<Collider>(); tires = GetComponentsInChildren<TireForce>();
        rivalIndex = entry.Index % 2; lane = desiredLane = entry.Index % 2 == 0 ? -2f : 2f;
        GetComponent<DebugMover>().SetInputSource(this);
        CarResetter resetter = GetComponent<CarResetter>(); if (resetter != null) resetter.enabled = false;
        SetDriving(false);
    }
    public void SetDriving(bool value)
    {
        driving = value; CurrentState = DriveInputState.Neutral;
        if (body == null) return;
        if (!value && !body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        body.isKinematic = !value;
    }
    private void FixedUpdate()
    {
        RestoreContactCollisions();
        if (!driving || manager.state != Gmanager.State.Game || !entry.IsRacing || laps == null || course == null || !course.HasValidPath)
        { CurrentState = DriveInputState.Neutral; return; }
        float dt = Time.fixedDeltaTime, sign = manager.CourseProgressSign;
        float raw = course.GetProgressDistance(body.position);
        float speed = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
        int grounded = 0; foreach (TireForce tire in tires) if (tire.IsGrounded) grounded++;
        groundedTires = grounded;
        course.TryGetSampleAtProgress(raw, out RaceCourse.CourseSample here);
        bool jumpAhead = false;
        float bendLimit = cruiseSpeed + maximumCatchup;
        Vector3 previous = here.forward;
        // Anticipatory braking uses stopping distance, rather than an instantaneous bend switch.
        for (float distance = 6f; distance <= 48f; distance += 6f)
        {
            course.TryGetSampleAtProgress(raw + sign * distance, out RaceCourse.CourseSample sample);
            float bend = Vector3.Angle(Vector3.ProjectOnPlane(previous, Vector3.up), Vector3.ProjectOnPlane(sample.forward, Vector3.up));
            float cornerSpeed = Mathf.Clamp(Mathf.Sqrt(6f * 6f / Mathf.Max(.005f, bend * Mathf.Deg2Rad)), 11f, 30f);
            bendLimit = Mathf.Min(bendLimit, Mathf.Sqrt(cornerSpeed * cornerSpeed + 2f * 5f * Mathf.Max(0f, distance - 8f)));
            if (distance <= 30f && !course.HasRoadAtProgress(raw + sign * distance)) jumpAhead = true;
            previous = sample.forward;
        }
        if (Time.time >= nextDecision)
        {
            nextDecision = Time.time + 4f; // Hold opponent/lane decisions; no frame-by-frame switching.
            RaceParticipant first = manager.GetParticipant(0), second = manager.GetParticipant(1);
            if (first?.IsRacing == true && second?.IsRacing == true)
            {
                bool split = Mathf.Abs(first.Progress - second.Progress) > 80f;
                rivalIndex = split ? (entry.Index == 2 ? (first.Progress < second.Progress ? 0 : 1) : (first.Progress < second.Progress ? 1 : 0)) : entry.Index % 2;
            }
            else rivalIndex = first?.IsRacing == true ? 0 : 1;
            RaceParticipant rival = manager.GetParticipant(rivalIndex);
            if (rival?.IsRacing == true)
            {
                float rivalSide = Vector3.Dot(rival.Body.position - here.position, here.right);
                desiredLane = rivalSide >= 0 ? -2.4f : 2.4f;
            }
        }
        RaceParticipant opponent = manager.GetParticipant(rivalIndex);
        float gap = opponent?.IsRacing == true ? opponent.Progress - laps.GetRaceProgressDistance(body) : 0;
        bool finalLap = laps.GetCarData(body)?.lapCount >= laps.GoalLap - 1;
        float contest = Mathf.Sin(Time.time * .22f + entry.Index * 2f) * (finalLap ? 8f : 16f);
        float partnerSpeed = opponent?.IsRacing == true ? opponent.Body.linearVelocity.magnitude : cruiseSpeed;
        float adjusted = Mathf.Clamp(Mathf.Clamp(partnerSpeed + (finalLap ? 5f : 3f), 5f, cruiseSpeed) + (gap - contest) * .14f,
            Mathf.Max(0, cruiseSpeed - maximumSlowdown), cruiseSpeed + maximumCatchup);
        float requested = Mathf.Min(adjusted, bendLimit);
        if (jumpAhead) { requested = Mathf.Min(bendLimit, Mathf.Max(24f, requested)); desiredLane = 0; }
        float safeLane = Mathf.Min(3f, Mathf.Max(0, here.width * .5f - 3.5f));
        lane = Mathf.MoveTowards(lane, Mathf.Clamp(desiredLane, -safeLane, safeLane), dt * 1.2f);
        course.TryGetSampleAtProgress(raw + sign * Mathf.Clamp(5f + speed * .4f, 8f, 17f), out RaceCourse.CourseSample target);
        Vector3 aim = target.position + target.right * Mathf.Clamp(lane, -Mathf.Max(0,target.width*.5f-2.5f), Mathf.Max(0,target.width*.5f-2.5f)) - body.position;
        Vector3 local = Quaternion.Inverse(body.rotation) * aim;
        float steering = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg * 1.5f - body.angularVelocity.y * 8f, -30f, 30f);
        // Yield to a car directly ahead, including narrow sections and the jump run-up.
        foreach (RaceParticipant other in manager.Participants)
        {
            if (other == entry || !other.IsRacing) continue;
            Vector3 delta = other.Body.position - body.position;
            float forward = Vector3.Dot(delta, body.rotation * Vector3.forward);
            float side = Mathf.Abs(Vector3.Dot(delta, body.rotation * Vector3.right));
            if (Mathf.Abs(delta.y) < 3f && forward > 0 && forward < 12f && side < 2.7f)
            {
                requested = Mathf.Min(requested, Mathf.Max(0, other.Body.linearVelocity.magnitude - (12f - forward) * .7f));
                if (!jumpAhead && safeLane > 2.5f) desiredLane = Vector3.Dot(other.Body.position - here.position, here.right) >= 0 ? -safeLane : safeLane;
            }
        }
        if (Time.time < yieldUntil) requested = Mathf.Min(requested, 8f);
        targetSpeed = Mathf.MoveTowards(targetSpeed, requested, dt * accelerationSlew);
        float signedSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
        float pedal = Mathf.Clamp((targetSpeed - signedSpeed) * .12f, -1f, 1f);
        stuck = ((speed < 1f && requested > 3f) || Vector3.Dot(transform.up, Vector3.up) < .3f || Mathf.Abs(body.position.y - here.position.y) > 6f || !course.IsPointInsideCourse(body.position)) ? stuck + dt : 0;
        if (stuck > 2f && reverseUntil <= Time.time) { reverseUntil = Time.time + 1.2f; stuck = 0; TryRecover(); }
        if (Time.time < reverseUntil && grounded >= 2 && !jumpAhead) { pedal = Mathf.Clamp((-3f - signedSpeed) * .12f, -.4f, .4f); steering = -steering; }
        if (grounded == 0) { pedal = 0; steering = 0; } // Preserve launch momentum through gaps.
        CurrentState = new DriveInputState { pedal = pedal, steering = steering };
        if (grounded >= 2 && speed > 3f && Vector3.Dot(transform.up, Vector3.up) > .8f && !jumpAhead &&
            course.HasRoadAtProgress(raw) && Mathf.Abs(body.position.y-here.position.y)<3f && course.IsPointInsideCourse(body.position))
        {
            float progress = laps.GetRaceProgressDistance(body);
            if (!hasSafeState || progress - safeRaceProgress > 10f)
            { safeRawProgress = raw; safeRaceProgress = progress; safeState = laps.CaptureNpcRecovery(body); hasSafeState = true; }
        }
    }
    private void TryRecover()
    {
        if (!hasSafeState || manager.IsNpcRecoveryVisible(body.position)) return;
        if (!course.TryGetSampleAtProgress(safeRawProgress, out RaceCourse.CourseSample sample) || !course.HasRoadAtProgress(safeRawProgress)) return;
        Vector3 position = sample.position + sample.up * 1.2f;
        if (manager.IsNpcRecoveryVisible(position) || !laps.CanRestoreNpcRecovery(body, safeState)) return;
        foreach (RaceParticipant other in manager.Participants)
            if (other != entry && other.Body != null && Vector3.Distance(position, other.Body.position) < 15f) return;
        int count = Physics.OverlapBoxNonAlloc(position, new Vector3(1.4f,.5f,2.5f), overlaps, Quaternion.LookRotation(sample.forward, sample.up), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) return;
        for (int i = 0; i < count; i++) if (overlaps[i].attachedRigidbody != body) return;
        if (!Physics.Raycast(sample.position + sample.up * 2f, -sample.up, out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || Vector3.Distance(hit.point,sample.position) > .5f) return;
        body.position = position; body.rotation = Quaternion.LookRotation(sample.forward * manager.CourseProgressSign, sample.up);
        body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
        laps.RestoreNpcRecovery(body, safeState); GetComponent<DebugMover>().SuppressInputAfterRespawn();
        targetSpeed = 0; RecoveryCount++;
    }
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.rigidbody == null || manager == null) return;
        RaceParticipant other = manager.FindParticipant(collision.rigidbody);
        if (other != null && !other.IsCpu)
        {
            if (Time.time >= yieldUntil) desiredLane = Vector3.Dot(other.Body.position - body.position, transform.right) >= 0 ? -3f : 3f;
            yieldUntil = Time.time + 3f;
            if (!contactGrace.Exists(grace => grace.Other == other.Body))
            {
                ContactGrace grace = new ContactGrace { Other = other.Body, Colliders = other.Car.GetComponentsInChildren<Collider>(), Until = Time.time + 2f };
                SetPairCollisions(grace, false); contactGrace.Add(grace);
            }
        }
    }
    private void RestoreContactCollisions()
    {
        for (int i = contactGrace.Count - 1; i >= 0; i--)
        {
            ContactGrace grace = contactGrace[i];
            if (grace.Other != null && (Time.time < grace.Until || Vector3.Distance(body.position, grace.Other.position) < 5f)) continue;
            SetPairCollisions(grace, true); contactGrace.RemoveAt(i);
        }
    }
    private void SetPairCollisions(ContactGrace grace, bool value)
    {
        foreach (Collider own in ownColliders)
        foreach (Collider other in grace.Colliders)
            if (own != null && other != null && !own.isTrigger && !other.isTrigger) Physics.IgnoreCollision(own, other, !value);
    }
    private void OnDestroy()
    {
        foreach (ContactGrace grace in contactGrace) SetPairCollisions(grace, true);
    }

}
