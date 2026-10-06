using UnityEngine;
using UnityEngine.UI;

/// <summary>Real pedal and steering input, rather than decorative TCS/ABS statuses.</summary>
public sealed class RacingTelemetryUI : MonoBehaviour
{
    private int player;
    private RectTransform pedal,steering;
    public void Configure(int index)
    {
        player=index;
        NeonUI.Icon(transform,"Car",RacingIconGraphic.Icon.Car,new Vector2(.035f,.18f),new Vector2(.13f,.81f));
        NeonUI.Text(transform,"PedalLabel","ペダル",new Vector2(.18f,.51f),new Vector2(.43f,.86f),16f);
        NeonUI.Text(transform,"PedalEnglish","PEDAL",new Vector2(.18f,.12f),new Vector2(.38f,.48f),12f,tint:NeonUI.Cyan);
        NeonUI.Text(transform,"SteerLabel","ハンドル",new Vector2(.59f,.51f),new Vector2(.95f,.86f),16f);
        NeonUI.Text(transform,"SteerEnglish","STEER",new Vector2(.59f,.12f),new Vector2(.80f,.48f),12f,tint:NeonUI.Cyan);
        RacingUITheme.Rule(transform,"PedalTrack",new Vector2(.39f,.17f),new Vector2(.54f,.41f),new Color(.035f,.08f,.20f));
        RacingUITheme.Rule(transform,"PedalFill",new Vector2(.39f,.17f),new Vector2(.40f,.41f),NeonUI.Cyan);
        pedal=transform.Find("PedalFill") as RectTransform;
        RacingUITheme.Rule(transform,"SteerTrack",new Vector2(.81f,.17f),new Vector2(.96f,.41f),new Color(.035f,.08f,.20f));
        RacingUITheme.Rule(transform,"SteerFill",new Vector2(.88f,.17f),new Vector2(.90f,.41f),NeonUI.Cyan);
        steering=transform.Find("SteerFill") as RectTransform;
    }
    private void LateUpdate()
    {
        if(Gmanager.Control?.IManager==null || pedal==null)return;
        DriveInputState input=Gmanager.Control.IManager.GetInputState(player);
        SetInput(input.pedal, input.steering);
    }
    public void SetInput(float pedalAmount, float steeringAmount)
    {
        if(pedal==null || steering==null)return;
        Vector2 max=new Vector2(Mathf.Lerp(.39f,.54f,Mathf.Clamp01(pedalAmount)),.41f);
        if(pedal.anchorMax!=max)pedal.anchorMax=max;
        float x=Mathf.Lerp(.815f,.945f,(Mathf.Clamp(steeringAmount,-1,1)+1)*.5f);
        Vector2 min=new Vector2(x,.17f); max=new Vector2(x+.015f,.41f);
        if(steering.anchorMin!=min)steering.anchorMin=min;
        if(steering.anchorMax!=max)steering.anchorMax=max;
    }
}
