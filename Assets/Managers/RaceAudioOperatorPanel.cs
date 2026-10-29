using UnityEngine;

/// <summary>Audio venue controls shown alongside the existing F8 operator panel.</summary>
[DisallowMultipleComponent]
public sealed class RaceAudioOperatorPanel : MonoBehaviour
{
    private RaceFeatureController features;
    private Rect panel=new Rect(16,590,330,150);
    private void Update() { if (features==null) features=FindFirstObjectByType<RaceFeatureController>(); }
    private void OnGUI()
    {
        if (features==null || !features.PanelVisible) return;
        float scale=Mathf.Clamp(Screen.height/900f,.75f,1.5f);
        Matrix4x4 previous=GUI.matrix;
        GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1f));
        Rect position=panel;
        position.x=Mathf.Max(16f,Screen.width/scale-350f);
        panel=GUILayout.Window(GetInstanceID(),position,DrawPanel,"Audio venue - F8 panel");
        GUI.matrix=previous;
    }
    private void DrawPanel(int id)
    {
        GUILayout.Label("Sound level presets (both players)");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Quiet")) RaceAudioSettings.ApplyVenuePreset(RaceAudioSettings.VenuePreset.Quiet);
        if (GUILayout.Button("Standard")) RaceAudioSettings.ApplyVenuePreset(RaceAudioSettings.VenuePreset.Standard);
        if (GUILayout.Button("Noisy venue")) RaceAudioSettings.ApplyVenuePreset(RaceAudioSettings.VenuePreset.NoisyVenue);
        GUILayout.EndHorizontal();
        GUILayout.Label("Current: "+RaceAudioSettings.Preset+"  Master "+Mathf.RoundToInt(RaceAudioSettings.Master*100f)+"% / Effects "+Mathf.RoundToInt(RaceAudioSettings.Effects*100f)+"%");
        for (int player = 0; player < 2; player++)
        {
            int current = RaceAudioSettings.OutputDelayMs(player);
            GUILayout.Label("P" + (player + 1) + " output delay: " + current + " ms");
            int next = Mathf.RoundToInt(GUILayout.HorizontalSlider(current, 0, RaceAudioSettings.MaximumOutputDelayMs) / 10f) * 10;
            if (next != current) RaceAudioSettings.SetOutputDelayMs(player, next);
        }
        GUILayout.Label("Delay the faster speaker to match Bluetooth.");
        bool diagnostics=GUILayout.Toggle(RaceAudioSettings.DiagnosticsEnabled,"Log opponent distance and stereo pan");
        if (diagnostics!=RaceAudioSettings.DiagnosticsEnabled) RaceAudioSettings.SetDiagnostics(diagnostics);
        GUI.DragWindow(new Rect(0,0,10000,20));
    }
}
