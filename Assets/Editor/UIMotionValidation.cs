#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Deterministic production-component checks, including interruption and idle rebuilds.</summary>
public static class UIMotionValidation
{
    public static void RunFoundation()
    {
        Require(UIMotion.Out(0)==0&&UIMotion.Out(1)==1,"Curve endpoints must be exact.");
        float shown=0;for(int i=0;i<100;i++){float next=UIMotion.Follow(shown,100,1f/60,.065f);Require(next>=shown&&next<=100,"Meters cannot overshoot or reverse.");shown=next;}
        Require(shown==100,"Damping must settle exactly.");
        var root=new GameObject("Motion QA",typeof(RectTransform));
        try
        {
            var child=RacingUITheme.Rect(root.transform,"Card",Vector2.zero,Vector2.one);child.anchoredPosition=new Vector2(5,9);
            var sequence=root.AddComponent<UIRevealSequence>();sequence.Configure(root.transform,new[]{"Card"},new Vector2(30,0));
            sequence.Play();Require(child.anchoredPosition.x>5,"Entrance must travel.");sequence.Tick(.1f);sequence.Play();sequence.Tick(1);
            Require(!sequence.IsPlaying&&child.anchoredPosition==new Vector2(5,9)&&child.GetComponent<CanvasGroup>().alpha==1,"Replay cannot accumulate an offset.");
            sequence.Play();root.SetActive(false);sequence.Cancel();Require(child.anchoredPosition==new Vector2(5,9)&&child.localScale==Vector3.one,"Cancellation restores the final pose.");root.SetActive(true);
            var text=ArcadeUI.Label(root.transform,"Number","2",Vector2.zero,Vector2.one,100,ArcadeUI.Blue.Ink,true);
            var pulse=text.gameObject.AddComponent<UIValuePulse>();var rect=(RectTransform)text.transform;Color ink=text.color;
            pulse.PlayRank(Color.green,1);Require(rect.anchoredPosition.y<0,"Gaining a place slides up from below.");pulse.Tick(.09f);pulse.PlayRank(Color.red,-1);pulse.Tick(1);
            Require(!pulse.IsPlaying&&rect.anchoredPosition==Vector2.zero&&rect.localScale==Vector3.one&&text.color==ink,"Interrupted rank motion must settle exactly.");
            pulse.Play(Color.yellow,4);text.gameObject.SetActive(false);pulse.ResetPose();Require(text.color==ink&&rect.localScale==Vector3.one,"Cancelled number must not retain an impulse.");
            var boardRoot=RacingUITheme.Rect(root.transform,"Board",Vector2.zero,Vector2.one);var board=boardRoot.gameObject.AddComponent<RacingLeaderboardUI>();board.Configure(0);board.SetPosition(3,5);board.Tick(1);
            Require(boardRoot.Find("Player0/Rank").GetComponent<TMP_Text>().text=="3","Classification must retain the actual rank.");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
        Debug.Log("UI_MOTION_FOUNDATION_PASS: finite curves, directional ranks, interruption, replay, disable reset and classification.");
    }
    internal static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
#endif
