using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>F8 controls are available in player builds and never save debugging state to disk.</summary>
[DisallowMultipleComponent]
public sealed class RaceFeatureController : MonoBehaviour
{
    public RaceFeatureSettings Settings { get; } = new RaceFeatureSettings();
    public bool PanelVisible { get; set; }
    public event System.Action FeaturesChanged;
    private Gmanager manager;
    private int appliedRevision = -1;
    private Vector2 scroll;
    private Rect panel = new Rect(16, 16, 550, 560);

    public static bool Enabled(RaceFeature feature) => Gmanager.Control != null &&
        Gmanager.Control.Features != null && Gmanager.Control.Features.Settings.IsEnabled(feature);

    public void Configure(Gmanager race) { manager = race; }
    public void SetFeature(RaceFeature feature, bool enabled)
    {
        Settings.SetFeature(feature, enabled);
        ApplyChanges();
    }
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public void SetValidationFeature(RaceFeature feature, bool enabled)
    {
        Settings.SetValidationFeature(feature, enabled); ApplyChanges();
    }
    public void SelectPreset(RacePreset preset)
    {
        Settings.SelectPreset(preset);
        if (!Settings.RulesLocked) manager?.ApplyFeatureRules();
        ApplyChanges();
    }
    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame) PanelVisible = !PanelVisible;
        ApplyChanges();
    }
    private void ApplyChanges()
    {
        if (appliedRevision == Settings.Revision) return;
        appliedRevision = Settings.Revision;
        if (manager != null)
        {
            manager.GetComponent<MenuSceneryDirector>()?.Refresh();
            manager.GetComponent<CampusAtmosphereController>()?.ApplySwitch();
            if (!Settings.IsEnabled(RaceFeature.CourseIntro)) manager.GetComponent<CourseIntroDirector>()?.Stop();
            foreach (RaceParticipant entry in manager.Participants)
            {
                if (!Settings.IsEnabled(RaceFeature.FinishRun)) entry.Car?.GetComponent<FinishRunController>()?.Stop();
                entry.Car?.GetComponent<FinishRunController>()?.ApplyVisibilitySwitch();
                entry.Effects?.ApplyFeatureSwitches();
                entry.Car?.GetComponent<PlayerAutodrive>()?.ApplySwitch();
                entry.Car?.GetComponent<LandingRewardController>()?.ApplySwitches();
                if (!Settings.IsEnabled(RaceFeature.TubeChargeLines)) entry.Car?.GetComponent<TubeChargeLineController>()?.Clear();
                if (!Settings.IsEnabled(RaceFeature.SteeringAssist)) entry.Car?.GetComponent<CourseSteeringAssist>()?.Clear();
                if (!Settings.IsEnabled(RaceFeature.DriftCoaching)) entry.Car?.GetComponent<DriftCoachingUI>()?.ClearNotice();
                if (!Settings.IsEnabled(RaceFeature.Slipstream)) entry.Car?.GetComponent<SlipstreamController>()?.Clear();
            }
            manager.course?.GetComponent<TubeChargeLineLayout>()?.ApplySwitch();
            manager.RefreshResultHighlights();
            if (Settings.IsEnabled(RaceFeature.ExperienceItems)) OilSlick.ClearAll();
        }
        FeaturesChanged?.Invoke();
    }
    private void OnGUI()
    {
        if (manager?.IsMaintenanceActive == true || !PanelVisible) return;
        // Keep IMGUI in ASCII: the cabinet's default IMGUI font does not contain Japanese.
        float scale = Mathf.Clamp(Screen.height / 900f, .75f, 1.5f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        panel.height = Mathf.Min(800f, Screen.height / scale - 32f);
        panel = GUILayout.Window(GetInstanceID(), panel, DrawPanel, "Racing debug - F8 to close");
        GUI.matrix = previous;
    }
    private void DrawPanel(int id)
    {
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Max(200f, panel.height - 45f)));
        if (manager != null && (manager.state == Gmanager.State.Title || manager.state == Gmanager.State.Result))
        {
            if (GUILayout.Button("Start test race - P1 manual / P2 AUTO")) manager.StartAutodriveTestRace();
            if (GUILayout.Button("Start AUTO race - P1 AUTO vs P2 AUTO")) manager.StartAutodriveTestRace(true);
        }
        if (manager?.BothPlayersAutodrive == true) GUILayout.Label("P1 AUTO vs P2 AUTO / excluded from daily ranking");
        GUILayout.Space(6);
        GUILayout.Label("Feature switches apply immediately (both players).");
        for (int i = 0; i < RaceDebugFeatureCatalog.Entries.Count; i++)
        {
            RaceDebugFeatureCatalog.Entry entry = RaceDebugFeatureCatalog.Entries[i];
            bool current = Settings.IsEnabled(entry.Feature);
            bool next = GUILayout.Toggle(current, entry.PanelLabel);
            if (current != next) SetFeature(entry.Feature, next);
        }
        GUILayout.Space(8);
        GUILayout.Label("Other gameplay improvements are standard and always enabled.");
        GUILayout.Label("Debug choices reset on application restart.");
        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

}
