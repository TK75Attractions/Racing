#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>Observe the real pre-race flow without changing cars, time or finish outcomes.</summary>
[DefaultExecutionOrder(9990)]
public sealed class CourseIntroPlaytestProbe : MonoBehaviour
{
    [Serializable] private sealed class Report
    {
        public string mode; public bool passed; public string error;
        public float observedIntroSeconds, countdownAtEntry, maximumRaceTimeDuringIntro, maximumGridMovement;
        public int introFrames; public bool sawCountdownAfterIntro;
        public List<Vector3> cameraSamples = new List<Vector3>();
    }
    private readonly Report report = new Report();
    private readonly Dictionary<int, Vector3> grid = new Dictionary<int, Vector3>();
    private Gmanager manager;
    private PlayerDisplayRig[] rigs;
    private bool seen, intervened, finished;
    private float introEndedAt = -1;
    private int captures;
    private string Folder => Environment.GetEnvironmentVariable("RACING_BALANCE_OUTPUT") ?? "Artifacts/CourseIntro/Preview";
    private void Awake() { Application.logMessageReceived += OnLog; }
    private void OnDestroy() { Application.logMessageReceived -= OnLog; }
    private void OnLog(string message, string stack, LogType type)
    {
        if ((type == LogType.Exception || type == LogType.Error) && !stack.Contains("UnityEditor.Search") && Application.isBatchMode)
            UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(1);
    }
    private void LateUpdate()
    {
        if (finished) return;
        try { Observe(); }
        catch (Exception error) { report.error = error.ToString(); Save(); finished = true; Debug.LogException(error); }
    }
    private void Observe()
    {
        manager = Gmanager.Control; if (manager == null) return;
        if (rigs == null) rigs = (PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
        var intro = manager.GetComponent<CourseIntroDirector>();
        if (manager.IsCourseIntroPlaying)
        {
            if (!seen)
            {
                seen = true; report.mode = Environment.GetEnvironmentVariable("RACING_COURSE_INTRO_MODE") ?? "normal";
                report.countdownAtEntry = manager.CountdownTimeRemaining;
                foreach (var car in manager.Participants) grid[car.Index] = car.Body.position;
            }
            report.introFrames++; report.observedIntroSeconds = intro.ElapsedSeconds;
            report.maximumRaceTimeDuringIntro = Mathf.Max(report.maximumRaceTimeDuringIntro, manager.time);
            Require(manager.state == Gmanager.State.Countdown && manager.time == 0, "The flyover must not start the race clock.");
            Require(Mathf.Abs(manager.CountdownTimeRemaining-report.countdownAtEntry) < .001f, "The countdown must wait for the flyover.");
            foreach (var car in manager.Participants)
            {
                Require(!manager.CanDriveVehicle(car.Body), "Cars must not accept driving during the flyover.");
                float movement = Vector3.ProjectOnPlane(car.Body.position-grid[car.Index], Vector3.up).magnitude;
                report.maximumGridMovement = Mathf.Max(report.maximumGridMovement, movement);
                Require(movement < .15f, "The flyover must hold the start grid.");
            }
            foreach (var rig in rigs)
                Require(rig.CanvasRoot.transform.Find("OnPlay").GetComponent<CanvasGroup>().alpha == 0, "Driving HUD must stay hidden during the flyover.");
            if (intro.ElapsedSeconds < CourseIntroDirector.FlightSeconds-.1f && rigs.Length == 2)
                Require(Vector3.Distance(rigs[0].MainCamera.transform.position, rigs[1].MainCamera.transform.position) < .1f, "Both displays must share the campus flight.");
            float[] shots = { .6f, 3.5f, 6.9f, 8.9f };
            if (captures < shots.Length && intro.ElapsedSeconds >= shots[captures])
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(Path.Combine(Folder, "intro-" + captures + ".jpg"), rigs[0].CaptureJpeg(1280,720));
                report.cameraSamples.Add(rigs[0].MainCamera.transform.position); captures++;
            }
            if (!intervened && intro.ElapsedSeconds > 2.5f && report.mode != "normal")
            {
                intervened = true;
                if (report.mode == "off") manager.Features.SetFeature(RaceFeature.CourseIntro, false);
                else if (report.mode == "disable") intro.enabled = false;
                else if (report.mode == "restart")
                {
                    intro.enabled = false;
                    Require(!intro.IsPlaying, "Disabling must cleanly stop the current intro.");
                    intro.enabled = true; intro.Begin(manager, rigs);
                }
                else throw new InvalidOperationException("Unknown intro check mode: " + report.mode);
                Require(report.mode == "restart" ? intro.IsPlaying && intro.ElapsedSeconds == 0 : !intro.IsPlaying,
                    "Live OFF/disable must release the flyover; a restart must begin with clean resources.");
            }
        }
        else if (seen)
        {
            if (introEndedAt < 0) introEndedAt = Time.time;
            if (manager.state == Gmanager.State.Countdown) report.sawCountdownAfterIntro = true;
            foreach (var rig in rigs)
                Require(rig.CanvasRoot.transform.Find("OnPlay").GetComponent<CanvasGroup>().alpha == 1, "Normal HUD alpha must be restored.");
            if (manager.state != Gmanager.State.Game) return;
            Require(report.sawCountdownAfterIntro && Time.time-introEndedAt >= report.countdownAtEntry-.2f, "The complete countdown must follow the flyover.");
            if (report.mode == "normal" || report.mode == "restart") Require(report.observedIntroSeconds >= CourseIntroDirector.Duration-.15f, "Normal entry must play the complete flight.");
            Require(manager.GetComponentsInChildren<CinemachineCamera>().Length == 0, "Temporary intro cameras must be removed.");
            Require(GameObject.Find("CourseIntroBackdrop") == null, "The flyover ground extension must be removed before driving.");
            foreach (var rig in rigs) Require(rig.CanvasRoot.transform.Find("CourseIntro") == null, "Temporary intro UI must be removed.");
            foreach (var rig in rigs)
                Require(rig.MainCamera.GetComponent<CinemachineBrain>().ActiveVirtualCamera == rig.RaceCamera,
                    "Each display must return to its own racing camera.");
            report.passed = true; finished = true; Save();
            Debug.Log("COURSE_INTRO_PLAYTEST_PASS: " + report.mode + ", synchronized cameras, grid/input/clock held, full countdown, HUD and camera cleanup.");
        }
    }
    private void Save() { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder,"course-intro-checks.json"), JsonUtility.ToJson(report,true)); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
