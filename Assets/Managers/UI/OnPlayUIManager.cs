using System;
using UnityEngine;

[Serializable]
public class OnPlayUIManager
{
    private Transform trans;
    private CanvasGroup CG;
    [SerializeField] private UIPosition position = new UIPosition();
    [SerializeField] private UILap lap = new UILap();
    [SerializeField] private UITime time = new UITime();
    [SerializeField] private UISpeed speed = new UISpeed();
    [SerializeField] private UIMiniMap miniMap = new UIMiniMap();

    private bool initialized = false;

    public UIPosition Position => position;
    public UILap Lap => lap;
    public UITime TimeView => time;
    public UISpeed Speed => speed;
    public UIMiniMap MiniMap => miniMap;

    public void Init(Transform parent, RaceCourse course, int playerIndex, int totalLaps = 3)
    {
        trans = parent;
        if (parent == null)
        {
            Debug.LogWarning("OnPlayUIManager requires a parent Transform.");
            return;
        }

        if (position == null) position = new UIPosition();
        if (lap == null) lap = new UILap();
        if (time == null) time = new UITime();
        if (speed == null) speed = new UISpeed();
        if (miniMap == null) miniMap = new UIMiniMap();

        Transform hud = RacingHUDBuilder.Build(parent, playerIndex, totalLaps);
        position.Init(hud.Find("Position"));
        lap.Init(hud.Find("Lap"));
        time.Init(hud.Find("Time"));
        speed.Init(hud.Find("Speed"));
        // ミニマップはノードが無ければ自前で生成するため、OnPlay ルートごと渡します。
        miniMap.Init(parent, course, playerIndex);
        initialized = true;
    }

    public void UpdateUI(int positionValue, int lapValue, float totalSeconds, float lapSeconds, float speedValue)
    {
        SetPosition(positionValue);
        SetLap(lapValue);
        SetTime(totalSeconds, lapSeconds);
        SetSpeed(speedValue);
        miniMap.UpdateMarkers();
    }

    public void SetMiniMapCars(Transform playerOneCar, Transform playerTwoCar)
    {
        miniMap.SetCars(playerOneCar, playerTwoCar);
    }

    public void SetActive(bool isActive)
    {
        trans.gameObject.SetActive(isActive);
    }

    private void SetPosition(int value)
    {
        position.SetPosition(value);
    }

    private void SetLap(int value)
    {
        lap.SetLap(value);
    }

    private void SetTime(float totalSeconds, float lapSeconds)
    {
        time.SetTotalTime(totalSeconds);
        time.SetLapTime(lapSeconds);
    }

    private void SetSpeed(float speedValue)
    {
        speed.UpdateSpeedMeter(speedValue, Time.deltaTime);
    }
}
