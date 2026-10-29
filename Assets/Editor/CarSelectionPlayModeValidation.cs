#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

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
        // An old saved colour must never override the seat's blue/pink identity.
        PlayerPrefs.SetInt(Keys[1],0);PlayerPrefs.SetInt(Keys[3],4);
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
    private static float phaseStartFixedTime;
    private static Vector3 drivingPosition;
    private static bool checkedGauge;
    private static bool Ready(Gmanager manager, int player, Gmanager.State expected) =>
        manager.GetPlayerPreparationState(player) == expected && !screens[player].GetComponent<ScreenTransitionController>().IsTransitioning;
    private static void PhaseTo(int phase)
    { SessionState.SetInt(Phase, phase); phaseBegan = EditorApplication.timeSinceStartup;phaseStartFixedTime=Time.fixedTime; }
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
                ValidateNativeResolution(manager);
                screens = new CarSelectionUI[2];
                foreach (CarSelectionUI screen in UnityEngine.Object.FindObjectsByType<CarSelectionUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) screens[screen.PlayerIndex] = screen;
                Require(screens[0] != null && screens[1] != null, "Each player receives a selection screen");
                IDriveInputSource[] sources = (IDriveInputSource[])typeof(InputManager).GetField("inputSources", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager.IManager);
                inputs = new[] { new StubInput { PlayerIndex = 0 }, new StubInput { PlayerIndex = 1 } };
                sources[0] = inputs[0]; sources[1] = inputs[1];
                for(int i=0;i<2;i++)((LiveDriveInputSource)manager.IManager.GetPlayerInputSource(i)).SwitchSource(inputs[i],false);
                inputs[1].CurrentState = new DriveInputState { pedal = 1f };
                manager.ConfirmTitleStart(1); PhaseTo(1);
            }
            else if (phase == 1 && Ready(manager, 1, Gmanager.State.CarSelection))
            {
                Require(Ready(manager, 0, Gmanager.State.Title), "P2 starts selection without waiting for P1");
                Require(screens[1].ConfirmationProgress == 0f && !screens[1].IsSelectionLocked, "Held start pedal cannot confirm a car");
                Require(screens[1].Showroom.Turntable.localRotation != Quaternion.identity,"The car and its turntable rotate together.");
                Require(screens[0].SelectedPaint==2&&screens[1].SelectedPaint==3,"Player colours override saved paint choices.");
                Require(screens[1].Showroom.transform.Find("Performance/Paint0")==null,"Paint selection controls are not built.");
                Require(Mathf.Abs(CarSelectionModel.Bounds(screens[1].Showroom.Model).min.y-screens[1].Showroom.Turntable.position.y-screens[1].Showroom.DeckHeight)<.01f,"The tyres sit on the turntable deck.");
                screens[1].SelectVehicle(2); screens[1].SelectPaint(3);
                Capture(manager,1,"car-p2.png");
                inputs[1].CurrentState = DriveInputState.Neutral; PhaseTo(2);
            }
            else if (phase == 2 && age > .15)
            {
                screens[1].TickInput(DriveInputState.Neutral,true,.01f);
                inputs[1].CurrentState=new DriveInputState{pedal=1f};screens[1].TickInput(inputs[1].CurrentState,true,.2f);
                Require(screens[1].ConfirmationProgress>0&&screens[1].ConfirmationProgress<1,"Pedal fills the visible confirmation gauge");
                checkedGauge=true;PhaseTo(3);
            }
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
                Capture(manager,0,"car-p1.png");Capture(manager,1,"tutorial-p2.png");
                drivingPosition = manager.GetComponent<DrivingTutorialController>().GetPlayerCar(1).transform.position;
                inputs[1].CurrentState = new DriveInputState { pedal = 1f }; PhaseTo(5);
            }
            else if (phase == 5 && Time.fixedTime-phaseStartFixedTime>=1.2f)
            {
                GameObject car = manager.GetComponent<DrivingTutorialController>().GetPlayerCar(1);
                Require(Vector3.Distance(drivingPosition, car.transform.position) > .1f, $"Tutorial car moves while the other selection screen stays open: distance={Vector3.Distance(drivingPosition,car.transform.position)}, velocity={car.GetComponent<Rigidbody>().linearVelocity}, canDrive={manager.CanDriveVehicle(car.GetComponent<Rigidbody>())}, pedal={car.GetComponent<DebugMover>().InputSource.CurrentState.pedal}, suppressed={car.GetComponent<DebugMover>().IsInputSuppressed}, enabled={car.GetComponent<DebugMover>().enabled}");
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
                tutorial.Tick(DrivingTutorialProgress.DefaultTimeLimitSeconds + 1f);
                Require(tutorial.GetPlayerCar(0).GetComponent<Rigidbody>().isKinematic, "Timeout stops the unfinished practice car");
                Require(tutorial.AllComplete, "An early completion and a timeout allow the race to start together");
                Require(!tutorial.GetPlayerCar(0).GetComponent<DebugMover>().enabled, "Timeout disables practice driving");
                PhaseTo(7);
            }
            else if (phase == 7 && (manager.state == Gmanager.State.Countdown || manager.state == Gmanager.State.Game))
            {
                VerifyCar(manager.GetPlayerCarTransform(0)?.gameObject, true);
                VerifyCar(manager.GetPlayerCarTransform(1)?.gameObject, false);
                foreach (CarSelectionUI screen in screens)
                    Require(!screen.IsOpen && !screen.Showroom.Model.activeInHierarchy, "Showroom is inactive during gameplay");
                Debug.Log("CAR_SELECTION_PLAY_MODE_PASS: independent P2-first start, pedal gauge, mixed selection/practice, confirmation release gate, tutorial timeout, synchronized race");
                Finish(0);
            }
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }
    private static void Capture(Gmanager manager,int player,string name)
    {
        screens[player].Showroom.RenderNow();
        string directory=System.IO.Path.GetFullPath("Artifacts/ArcadeSportUI/Live");System.IO.Directory.CreateDirectory(directory);
        typeof(DrivingTutorialValidation).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{manager,player,System.IO.Path.Combine(directory,name)});
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
            lesson.CollectItem(true);
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
    private static void ValidateNativeResolution(Gmanager manager)
    {
        var pipeline = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (pipeline == null) return;
        var rigs = (PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
        PlayerDisplayRig rig = rigs[0];
        float before = pipeline.renderScale;
        bool worldReduced = false, uiNative = false;
        System.Action<ScriptableRenderContext, Camera> observe = (context, camera) =>
        {
            if (camera == rig.BackImageCamera) worldReduced = Mathf.Abs(pipeline.renderScale - .5f) < .001f;
            if (camera == rig.UiCamera) uiNative = Mathf.Abs(pipeline.renderScale - 1f) < .001f;
        };
        var target = RenderTexture.GetTemporary(320, 180, 24);
        try
        {
            pipeline.renderScale = .5f;
            RenderPipelineManager.beginCameraRendering += observe;
            rig.RenderToTarget(target);
            Require(worldReduced && uiNative, "World rendering may scale down while the arcade UI renders at native resolution.");
            Require(Mathf.Abs(pipeline.renderScale - .5f) < .001f, "UI capture must restore the world render scale.");
        }
        finally
        {
            RenderPipelineManager.beginCameraRendering -= observe;
            pipeline.renderScale = before;
            RenderTexture.ReleaseTemporary(target);
        }
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
