#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.EventSystems;

public static class UIInteractionMotionValidation
{
    private static void Require(bool value,string message)=>UIMotionValidation.Require(value,message);
    public static void Run()
    {
        UIMotionValidation.RunMeters();
        var root=new GameObject("Interaction motion QA",typeof(RectTransform),typeof(EventSystem));
        try
        {
            int clicks=0;
            var button=NeonUI.Button(root.transform,"Hold","START","",RacingIconGraphic.Icon.Flag,Vector2.zero,Vector2.one,true,()=>clicks++);
            button.ConfigureHold(.3f,false);button.PlayConfirmationSound=false;
            var pointer=new PointerEventData(root.GetComponent<EventSystem>()){button=PointerEventData.InputButton.Left};
            button.OnPointerDown(pointer);button.Tick(.15f);button.OnPointerUp(pointer);button.Tick(.5f);
            Require(clicks==0,"A short hold must cancel.");
            button.OnPointerDown(pointer);button.Tick(.31f);button.OnPointerClick(pointer);button.Tick(.1f);button.Tick(1);
            Require(clicks==1,"A completed hold must invoke exactly once.");button.OnPointerUp(pointer);
            var normal=NeonUI.Button(root.transform,"Normal","BACK","",RacingIconGraphic.Icon.Back,Vector2.zero,Vector2.one,false,()=>clicks++);normal.PlayConfirmationSound=false;
            normal.OnPointerClick(pointer);normal.OnPointerClick(pointer);normal.Tick(.1f);
            Require(clicks==2,"Repeated clicks during confirmation must not duplicate actions.");
            normal.OnPointerClick(pointer);normal.interactable=false;normal.Tick(1);normal.interactable=true;normal.Tick(1);
            Require(clicks==2,"An invalidated pending action must never invoke later.");
            var feedback=normal.GetComponent<PedalButtonFeedback>();feedback.SetPointerState(true,true,true);feedback.Tick(.05f);feedback.SetPointerState(false,false,true);
            for(int i=0;i<120;i++)feedback.Tick(1f/60);
            long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)feedback.Tick(1f/60);allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            Require(allocated==0,"A settled button must allocate nothing.");
            var menu=InterruptionMenuUI.Create(root.transform,null,null,null);menu.Show(false);
            var sequence=root.transform.Find("InterruptionMenu/MenuCard").GetComponent<UIRevealSequence>();sequence.Play();sequence.Tick(.1f);menu.Hide();sequence.Cancel();menu.Show(false);sequence.Play();sequence.Tick(1);
            Require(!sequence.IsPlaying&&root.transform.Find("InterruptionMenu/MenuCard").GetComponent<RectTransform>().anchoredPosition==Vector2.zero,"Repeated menu entry cannot drift.");
            var ending=ArcadeEndingUI.Get(root.transform);int done=0;ending.Show(()=>done++);ending.TickInput(1,2);Require(done==0,"An entrance must not bypass held-pedal safety.");ending.TickInput(0,.1f);ending.TickInput(1,.7f);Require(done==1,"Confirmation remains usable during entrance.");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
        Debug.Log("UI_INTERACTION_MOTION_PASS: cancel/complete hold, one-shot confirmation, pending-action invalidation, menu reopen, ending input gate and zero idle button allocations.");
    }
}
#endif
