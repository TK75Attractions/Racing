#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Smoke test of the production two-player title, tutorial and race spawning path.</summary>
[InitializeOnLoad]
public static class CarSelectionPlayModeValidation
{
    private const string Active = "Racing.CarSelectionSmoke.Active", Phase = "Racing.CarSelectionSmoke.Phase";
    private static readonly string[] Keys = { "Racing.Car.P0", "Racing.Paint.P0", "Racing.Car.P1", "Racing.Paint.P1" };
    static CarSelectionPlayModeValidation() { EditorApplication.update += Tick; }

    [MenuItem("Racing/UI/Validate Car Selection in Play Mode")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before starting this validation.");
        for (int i = 0; i < Keys.Length; i++)
        {
            SessionState.SetBool(Active + i + ".exists", PlayerPrefs.HasKey(Keys[i]));
            SessionState.SetString(Active + i + ".value", i % 2 == 0 ? PlayerPrefs.GetString(Keys[i]) : PlayerPrefs.GetInt(Keys[i]).ToString());
        }
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        InputManager input = UnityEngine.Object.FindFirstObjectByType<InputManager>();
        input.isDebugMode = true; input.isP1SerialP2KeyboardDebugMode = false;
        SessionState.SetBool(Active, true); SessionState.SetInt(Phase, 0);
        SessionState.SetFloat(Active + ".began", (float)EditorApplication.timeSinceStartup);
        EditorApplication.isPlaying = true;
    }
    private sealed class StubInput : IDriveInputSource
    {
        public int PlayerIndex { get; set; }
        public string DeviceId => "CAR_SELECTION_QA";
        public bool IsConnected => true;
        public DriveInputState CurrentState { get; set; }
        public void UpdateInput(float dt) { }
        public void Dispose() { }
    }
    private static StubInput[] inputs;
    private static CarSelectionUI[] screens;
    private static double phaseBegan;
    private static Vector3 drivingPosition;
    private static bool checkedGauge;
    private static bool Ready(Gmanager manager, int player, Gmanager.State expected) =>
        manager.GetPlayerPreparationState(player) == expected && !screens[player].GetComponent<ScreenTransitionController>().IsTransitioning;
    private static void PhaseTo(int phase)
    { SessionState.SetInt(Phase, phase); phaseBegan = EditorApplication.timeSinceStartup; }
    private static void Tick()
    {
        if (!SessionState.GetBool(Active, false)) return;
        try
        {
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Active + ".began", 0) > 120)
                throw new InvalidOperationException("Car selection Play Mode validation timed out.");
            if (!EditorApplication.isPlaying || Gmanager.Control == null) return;
            Gmanager manager = Gmanager.Control;
            int phase = SessionState.GetInt(Phase, 0);
            double age = EditorApplication.timeSinceStartup - phaseBegan;
            if (phase == 0 && manager.state == Gmanager.State.Title)
            {
                screens = new CarSelectionUI[2];
                foreach (CarSelectionUI screen in UnityEngine.Object.FindObjectsByType<CarSelectionUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) screens[screen.PlayerIndex] = screen;
                Require(screens[0] != null && screens[1] != null, "Each player receives a selection screen");
                IDriveInputSource[] sources = (IDriveInputSource[])typeof(InputManager).GetField("inputSources", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager.IManager);
                inputs = new[] { new StubInput { PlayerIndex = 0 }, new StubInput { PlayerIndex = 1 } };
                sources[0] = inputs[0]; sources[1] = inputs[1];
                inputs[1].CurrentState = new DriveInputState { pedal = 1f };
                manager.ConfirmTitleStart(1); PhaseTo(1);
            }
            else if (phase == 1 && Ready(manager, 1, Gmanager.State.CarSelection))
            {
                Require(Ready(manager, 0, Gmanager.State.Title), "P2 starts selection without waiting for P1");
                Require(screens[1].ConfirmationProgress == 0f && !screens[1].IsSelectionLocked, "Held start pedal cannot confirm a car");
                Require(screens[1].Showroom.Model.transform.parent.localRotation != Quaternion.identity, "Preview automatically rotates in Play Mode");
                screens[1].SelectVehicle(2); screens[1].SelectPaint(3);
                inputs[1].CurrentState = DriveInputState.Neutral; PhaseTo(2);
            }
            else if (phase == 2 && age > .15)
            { inputs[1].CurrentState = new DriveInputState { pedal = 1f }; checkedGauge = false; PhaseTo(3); }
            else if (phase == 3)
            {
                if (!checkedGauge && age > .25 && !screens[1].IsSelectionLocked)
                {
                    Require(screens[1].ConfirmationProgress > 0f && screens[1].ConfirmationProgress < 1f, "Pedal fills the visible confirmation gauge");
                    checkedGauge = true;
                }
                if (Ready(manager, 1, Gmanager.State.Tutorial))
                {
                    DrivingTutorialController tutorial = manager.GetComponent<DrivingTutorialController>();
                    Require(tutorial.HasStarted(1) && !tutorial.HasStarted(0) && !tutorial.AllComplete, "P2 tutorial begins independently");
                    VerifyCar(tutorial.GetPlayerCar(1), false);
                    Require(tutorial.GetPlayerCar(1).GetComponent<DebugMover>().InputSource.CurrentState.pedal == 0f, "Confirmation pedal is blocked until release in the tutorial");
                    Require(Ready(manager, 0, Gmanager.State.Title), "P1 remains on its title");
                    inputs[1].CurrentState = DriveInputState.Neutral;
                    manager.ConfirmTitleStart(0); PhaseTo(4);
                }
            }
            else if (phase == 4 && Ready(manager, 0, Gmanager.State.CarSelection))
            {
                Require(manager.IsDrivingEnabled && manager.GetComponent<DrivingTutorialController>().HasStarted(1), "P2 can practice while P1 selects");
                screens[0].SelectVehicle(1); screens[0].SelectPaint(2);
                drivingPosition = manager.GetComponent<DrivingTutorialController>().GetPlayerCar(1).transform.position;
                inputs[1].CurrentState = new DriveInputState { pedal = 1f }; PhaseTo(5);
            }
            else if (phase == 5 && age > 1.0)
            {
                GameObject car = manager.GetComponent<DrivingTutorialController>().GetPlayerCar(1);
                Require(Vector3.Distance(drivingPosition, car.transform.position) > .1f, "Tutorial car moves while the other selection screen stays open");
                Require(screens[0].IsOpen && !screens[1].IsOpen, "Only P1 selection remains visible");
                inputs[1].CurrentState = DriveInputState.Neutral;
                inputs[0].CurrentState = new DriveInputState { pedal = 1f }; PhaseTo(6);
            }
            else if (phase == 6 && Ready(manager, 0, Gmanager.State.Tutorial))
            {
                DrivingTutorialController tutorial = manager.GetComponent<DrivingTutorialController>();
                VerifyCar(tutorial.GetPlayerCar(0), true);
                VerifyCar(tutorial.GetPlayerCar(1), false);
                inputs[0].CurrentState = DriveInputState.Neutral;
                CompleteLesson(tutorial, 1);
                Require(!tutorial.AllComplete && manager.state == Gmanager.State.Tutorial, "One completed lesson cannot start the race");
                CompleteLesson(tutorial, 0); PhaseTo(7);
            }
            else if (phase == 7 && (manager.state == Gmanager.State.Countdown || manager.state == Gmanager.State.Game))
            {
                VerifyCar(manager.GetPlayerCarTransform(0)?.gameObject, true);
                VerifyCar(manager.GetPlayerCarTransform(1)?.gameObject, false);
                foreach (CarSelectionUI screen in screens)
                    Require(!screen.IsOpen && !screen.Showroom.Model.activeInHierarchy, "Showroom is inactive during gameplay");
                Debug.Log("CAR_SELECTION_PLAY_MODE_PASS: independent P2-first start, pedal gauge, mixed selection/practice, confirmation release gate, synchronized race");
                Finish(0);
            }
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }
    private static void CompleteLesson(DrivingTutorialController tutorial, int index)
    {
        var practices = (System.Collections.IList)typeof(DrivingTutorialController).GetField("players", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tutorial);
        foreach (object practice in practices)
        {
            Type type = practice.GetType();
            if ((int)type.GetField("playerIndex").GetValue(practice) != index) continue;
            var lesson = (DrivingTutorialProgress)type.GetField("lesson").GetValue(practice);
            lesson.Tick(.01f, true, 0f, 0f, 0f, 0f);
            lesson.Tick(.7f, true, 1f, 0f, 4f, 0f);
            lesson.Tick(.7f, true, 1f, 10f, 4f, 30f);
            lesson.Tick(.7f, true, 0f, 0f, 0f, 30f);
            Require(lesson.IsComplete, "Simulated lesson completion is registered");
        }
    }
    private static void VerifyCar(GameObject car, bool sedan)
    {
        Require(car != null && car.GetComponent<Rigidbody>() != null, "Production car and rigidbody exist");
        Require((car.transform.Find("SelectedVehicleBody") != null) == sedan, "Selected body reaches the production car");
        Vector4 expected = sedan ? new Vector4(1.1f, .9f, 1.15f, .9f) : new Vector4(.95f, 1.15f, .9f, 1.15f);
        Require(car.GetComponent<DebugMover>().VehicleMultipliers == expected, "Selected grade reaches production driving settings");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Finish(int code)
    {
        SessionState.SetBool(Active, false);
        for (int i = 0; i < Keys.Length; i++)
        {
            if (!SessionState.GetBool(Active + i + ".exists", false)) PlayerPrefs.DeleteKey(Keys[i]);
            else if (i % 2 == 0) PlayerPrefs.SetString(Keys[i], SessionState.GetString(Active + i + ".value", ""));
            else PlayerPrefs.SetInt(Keys[i], int.Parse(SessionState.GetString(Active + i + ".value", "0")));
        }
        PlayerPrefs.Save(); EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
}
#endif
