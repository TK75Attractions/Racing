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
            if (phase == 0 && manager.state == Gmanager.State.Title)
            {
                CarSelectionUI[] screens = UnityEngine.Object.FindObjectsByType<CarSelectionUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                Require(screens.Length == 2, "Each player receives a selection screen");
                foreach (CarSelectionUI screen in screens)
                {
                    screen.Open();
                    manager.ConfirmTitleStart(screen.PlayerIndex);
                    Require(screen.IsOpen && manager.state == Gmanager.State.Title, "Title start is blocked while choosing a car");
                    screen.SelectVehicle(screen.PlayerIndex == 0 ? 1 : 2); screen.SelectPaint(screen.PlayerIndex == 0 ? 2 : 3);
                    screen.Confirm();
                }
                manager.StartGame(); SessionState.SetInt(Phase, 1);
            }
            else if (phase == 1 && manager.state == Gmanager.State.Tutorial && manager.IsDrivingEnabled)
            {
                VerifyCar(GameObject.Find("Player1_TutorialCar"), true);
                VerifyCar(GameObject.Find("Player2_TutorialCar"), false);
                Invoke(manager, "FinishTutorialWhenScreenCovered"); Invoke(manager, "CompleteGameStart");
                SessionState.SetInt(Phase, 2);
            }
            else if (phase == 2 && (manager.state == Gmanager.State.Countdown || manager.state == Gmanager.State.Game))
            {
                VerifyCar(manager.GetPlayerCarTransform(0)?.gameObject, true);
                VerifyCar(manager.GetPlayerCarTransform(1)?.gameObject, false);
                foreach (CarSelectionUI screen in UnityEngine.Object.FindObjectsByType<CarSelectionUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    Require(!screen.IsOpen && !screen.Showroom.Model.activeInHierarchy, "Showroom is inactive during gameplay");
                Debug.Log("CAR_SELECTION_PLAY_MODE_PASS: two-player title, selected tutorial cars and selected race cars");
                Finish(0);
            }
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }
    private static void VerifyCar(GameObject car, bool sedan)
    {
        Require(car != null && car.GetComponent<Rigidbody>() != null, "Production car and rigidbody exist");
        Require((car.transform.Find("SelectedVehicleBody") != null) == sedan, "Selected body reaches the production car");
        Vector4 expected = sedan ? new Vector4(1.1f, .9f, 1.15f, .9f) : new Vector4(.95f, 1.15f, .9f, 1.15f);
        Require(car.GetComponent<DebugMover>().VehicleMultipliers == expected, "Selected grade reaches production driving settings");
    }
    private static void Invoke(Gmanager manager, string method) => typeof(Gmanager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
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
