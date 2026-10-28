using UnityEngine;

/// <summary>Tube-pad guidance and hit feedback. Legacy public names preserve achievements/settings.</summary>
public sealed class TubeChargeLineController : MonoBehaviour
{
    private Gmanager manager;
    private RaceParticipant own;
    private DebugMover mover;
    private float noticeUntil;
    private string guidance="";
    public TubeChargeLineLayout Layout { get; private set; }
    public int SuccessfulLines { get; private set; }
    public bool Pending => false;
    public string Notice => RaceFeatureController.Enabled(RaceFeature.TubeChargeLines)?(Time.time<noticeUntil?"加速パネル！":guidance):"";
    public void Configure(Gmanager race,RaceParticipant driver)
    {
        Unsubscribe();manager=race;own=driver;mover=GetComponent<DebugMover>();
        if(race.course==null)return;
        Layout=race.course.GetComponent<TubeChargeLineLayout>()??race.course.gameObject.AddComponent<TubeChargeLineLayout>();
        Layout.Configure(race.course,race.CourseProgressSign);
        foreach(var pad in Layout.Pads)pad.Activated+=OnActivated;
    }
    private void OnActivated(DebugMover car)
    {
        if(car!=mover)return;SuccessfulLines++;noticeUntil=Time.time+1.5f;
    }
    public void Clear(){noticeUntil=0f;guidance="";}
    private void OnDisable(){Clear();}
    private void OnDestroy(){Unsubscribe();}
    private void Unsubscribe(){if(Layout!=null)foreach(var pad in Layout.Pads)if(pad!=null)pad.Activated-=OnActivated;}
    private void FixedUpdate(){Tick(Time.fixedDeltaTime);}
    public void Tick(float dt)
    {
        if(manager==null||manager.state!=Gmanager.State.Game||own?.IsRacing!=true||Layout?.Available!=true||
            !RaceFeatureController.Enabled(RaceFeature.TubeChargeLines)){Clear();return;}
        float distance=Layout.DistanceFromEntry(manager.course.GetProgressDistance(own.Body.position));
        guidance=distance>=-25f&&distance<0f?"光るパネルをふむと加速！":"";
    }
}
