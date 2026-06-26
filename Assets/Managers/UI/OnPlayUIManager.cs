using UnityEngine;

public class OnPlayUIManager : MonoBehaviour
{
    [SerializeField] private UIPosition position = new UIPosition();
    [SerializeField] private UILap lap = new UILap();
    [SerializeField] private UITime time = new UITime();

    public UIPosition Position => position;
    public UILap Lap => lap;
    public UITime TimeView => time;

    private void Awake()
    {
        position.Initialize();
        lap.Initialize();
        time.Initialize();
    }

    public void SetPosition(int value)
    {
        position.SetPosition(value);
    }

    public void SetLap(int value)
    {
        lap.SetLap(value);
    }

    public void SetTime(float totalSeconds, float lapSeconds)
    {
        time.SetTotalTime(totalSeconds);
        time.SetLapTime(lapSeconds);
    }
}
