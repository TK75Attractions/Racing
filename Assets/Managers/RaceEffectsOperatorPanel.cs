using UnityEngine;

/// <summary>F8 operator panel for saved accessibility settings.</summary>
[DisallowMultipleComponent]
public sealed class RaceEffectsOperatorPanel : MonoBehaviour
{
    private RaceFeatureController features;
    private Rect panel = new Rect(16, 430, 330, 190);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<RaceEffectsOperatorPanel>() != null) return;
        new GameObject("Race Effects Operator Panel").AddComponent<RaceEffectsOperatorPanel>();
    }
    private void Update() { if (features == null) features = FindFirstObjectByType<RaceFeatureController>(); }
    private void OnGUI()
    {
        if (features == null || !features.PanelVisible) return;
        float scale = Mathf.Clamp(Screen.height / 900f, .75f, 1.5f);
        Matrix4x4 old = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        Rect position = panel; position.x = Mathf.Max(16f, Screen.width / scale - 350f);
        panel = GUILayout.Window(GetInstanceID(), position, DrawPanel, "Race effects - F8 panel");
        GUI.matrix = old;
    }
    private void DrawPanel(int id)
    {
        Toggle("Camera shake", RaceEffectsSettings.CameraShake, RaceEffectsSettings.SetCameraShake);
        Toggle("Speed lines", RaceEffectsSettings.SpeedLines, RaceEffectsSettings.SetSpeedLines);
        Toggle("Strong flashes", RaceEffectsSettings.StrongFlashes, RaceEffectsSettings.SetStrongFlashes);
        Toggle("Speed / boost FOV", RaceEffectsSettings.FieldOfView, RaceEffectsSettings.SetFieldOfView);
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }
    private static void Toggle(string label, bool value, System.Action<bool> setter)
    { bool next = GUILayout.Toggle(value, label); if (next != value) setter(next); }
}
