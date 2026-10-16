using TMPro;
using UnityEngine;

/// <summary>Displays the same transmission state that drives the viewed car's engine sound.</summary>
public sealed class RacingGearDisplay : MonoBehaviour
{
    private int player;
    private TMP_Text label;
    private Transform viewedCar;
    private CarSoundController engine;
    private string displayed;
    private static readonly string[] Forward = { "1", "2", "3", "4", "5", "6" };
    public int PlayerIndex => player;
    public void Configure(int index, TMP_Text text) { player = index; label = text; }
    private void LateUpdate()
    {
        Gmanager manager = Gmanager.Control;
        if (manager == null || label == null) return;
        Transform car = manager.GetPlayerCarTransform(manager.GetAudioViewedPlayer(player));
        if (car != viewedCar)
        {
            viewedCar = car;
            engine = car != null ? car.GetComponentInChildren<CarSoundController>() : null;
        }
        string value = engine == null ? "1" : engine.IsReverse ? "R" : Forward[Mathf.Clamp(engine.CurrentGear - 1, 0, 5)];
        if (value != displayed) { displayed = value; label.text = value; }
        label.color = engine != null && engine.IsShifting ? NeonUI.Pink : NeonUI.Cyan;
    }
}
