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
            text.gameObject.SetActive(true);pulse.PlayRank(Color.green,1);pulse.Tick(.03f);
            Require(rect.localScale.y<1&&rect.localScale.x>1,"The number must compress before releasing its energy.");pulse.Tick(.11f);
            Require(rect.localScale.y>rect.localScale.x&&rect.localScale.y>1,"The release must stretch vertically rather than uniformly zooming.");
            var impact=text.transform.Find("NumberImpact").GetComponent<UINumberImpactGraphic>();
            var populate=typeof(UINumberImpactGraphic).GetMethod("OnPopulateMesh",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(VertexHelper)},null);
            using(var vertices=new VertexHelper())
            {
                rect.sizeDelta=new Vector2(140,160);populate.Invoke(impact,new object[]{vertices});Require(vertices.currentVertCount>0&&!impact.raycastTarget,"Landing speed marks must be real geometry that cannot intercept input.");
                UIVertex v=new UIVertex();for(int i=0;i<vertices.currentVertCount;i++){vertices.PopulateUIVertex(ref v,i);Require(rect.rect.Contains(new Vector2(v.position.x,v.position.y)),"Number accents stay inside their existing label bounds.");}
                pulse.ResetPose();populate.Invoke(impact,new object[]{vertices});Require(vertices.currentVertCount==0,"Cancelled number accents must draw nothing.");
            }
            int impactCount=text.GetComponentsInChildren<UINumberImpactGraphic>(true).Length;
            pulse.PlayRank(Color.green,1);pulse.Tick(.01f);
            long impulseAllocated=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<120;i++){if(i%30==0)pulse.PlayRank(Color.green,1);pulse.Tick(1f/60);}
            impulseAllocated=GC.GetAllocatedBytesForCurrentThread()-impulseAllocated;
            Require(impulseAllocated==0&&text.GetComponentsInChildren<UINumberImpactGraphic>(true).Length==impactCount,"Repeated number impulses reuse their accent geometry without managed allocations.");pulse.ResetPose();
            Debug.Log("UI_NUMBER_IMPACT_PASS: compression, vertical punch, bounded non-interactive geometry, exact cancel and allocation-free repeated impulses.");
            var boardRoot=RacingUITheme.Rect(root.transform,"Board",Vector2.zero,Vector2.one);var board=boardRoot.gameObject.AddComponent<RacingLeaderboardUI>();board.Configure(0);board.SetPosition(3,5);board.Tick(1);
            Require(boardRoot.Find("Player0/Rank").GetComponent<TMP_Text>().text=="3","Classification must retain the actual rank.");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
        Debug.Log("UI_MOTION_FOUNDATION_PASS: finite curves, directional ranks, interruption, replay, disable reset and classification.");
    }
    public static void RunMeters()
    {
        RunFoundation();var root=new GameObject("Meter motion QA",typeof(RectTransform));
        try
        {
            var hud=RacingHUDBuilder.Build(root.transform,0,2);var dial=hud.Find("Speed/Gauge").GetComponent<ArcadeDialGraphic>();
            dial.SetReading(60);dial.SetTargetReading(180);dial.Tick(1f/60);
            Require(dial.Reading>60&&dial.Reading<180,"Needle must ease into acceleration.");
            float before=dial.Reading;dial.SetTargetReading(20);dial.Tick(1f/60);Require(dial.Reading<before&&dial.Reading>20,"Braking must immediately reverse needle travel.");
            dial.SetTargetBoost(0,false);dial.SetTargetBoost(1);dial.FlashBoost();
            for(int i=0;i<120;i++)dial.Tick(1f/60);
            Require(dial.Reading==20&&dial.Boost==1&&!dial.IsAnimating,"Meter motion must settle without overshoot.");
            int dirty=0;dial.RegisterDirtyVerticesCallback(()=>dirty++);
            long allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<1000;i++){dial.SetTargetReading(20);dial.SetTargetBoost(1);dial.Tick(1f/60);}
            allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            Require(dirty==0&&allocated==0,"Settled instrument must allocate nothing and rebuild no mesh.");
            dial.SetReading(float.NaN);dial.SetBoost(float.PositiveInfinity);Require(dial.Reading==0&&dial.Boost==0,"Invalid samples cannot damage the UI mesh.");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
        Debug.Log("UI_MOTION_METERS_PASS: acceleration/braking response, finite values, settle, boost impulse, zero idle mesh rebuilds and zero idle managed allocations.");
    }
    internal static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
#endif
