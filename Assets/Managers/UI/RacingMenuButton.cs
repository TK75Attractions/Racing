using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Shared pointer/keyboard states and deliberate, one-shot hold confirmation.</summary>
public sealed class RacingMenuButton : Button
{
    private PedalButtonFeedback feedback;
    private float holdSeconds, heldTime;
    private bool holding, keyboardHold, committing;
    private Coroutine commitRoutine;
    public float HoldSeconds=>holdSeconds;
    public void Configure(RacingPanelGraphic value)
    {
        feedback=GetComponent<PedalButtonFeedback>() ?? gameObject.AddComponent<PedalButtonFeedback>();feedback.Bind(value);
        transition=Transition.None;DoStateTransition(currentSelectionState,true);
    }
    public void ConfigureHold(float seconds,bool retainStart)
    {
        holdSeconds=Mathf.Max(.1f,seconds);feedback.EnableGauge(retainStart);
    }
    protected override void DoStateTransition(SelectionState state,bool instant)
    {
        base.DoStateTransition(state,instant);
        if(feedback==null)return;
        bool focused=state==SelectionState.Highlighted || state==SelectionState.Selected || state==SelectionState.Pressed;
        feedback.SetPointerState(focused,state==SelectionState.Pressed,state!=SelectionState.Disabled);
    }
    public override void OnPointerDown(PointerEventData data)
    {
        base.OnPointerDown(data);
        if(data.button!=PointerEventData.InputButton.Left || !IsActive() || !IsInteractable() || committing || feedback.IsRetained)return;
        if(holdSeconds>0){holding=true;keyboardHold=false;heldTime=0;}
    }
    public override void OnPointerUp(PointerEventData data)
    {
        base.OnPointerUp(data);if(data.button==PointerEventData.InputButton.Left)CancelHold();
    }
    public override void OnPointerExit(PointerEventData data){base.OnPointerExit(data);CancelHold();}
    private void CancelHold(){holding=keyboardHold=false;heldTime=0;if(!committing)feedback?.SetPointerCharge(0);}
    public override void OnPointerClick(PointerEventData data)
    {if(data.button==PointerEventData.InputButton.Left && holdSeconds==0)Commit();}
    public override void OnSubmit(BaseEventData data)
    {
        if(!IsActive() || !IsInteractable() || committing || feedback.IsRetained)return;
        if(holdSeconds>0 && Keyboard.current!=null){holding=keyboardHold=true;heldTime=0;}
        else Commit();
    }
    private void Update()
    {
        if(!holding)return;
        if(!IsInteractable() || (keyboardHold && !SubmitHeld())){CancelHold();return;}
        heldTime+=Time.unscaledDeltaTime;feedback.SetPointerCharge(Mathf.Clamp01(heldTime/holdSeconds));
        if(heldTime>=holdSeconds){holding=keyboardHold=false;Commit();}
    }
    private static bool SubmitHeld()
    {
        Keyboard key=Keyboard.current;
        return key!=null && (key.enterKey.isPressed || key.numpadEnterKey.isPressed || key.spaceKey.isPressed);
    }
    private void Commit()
    {
        if(!IsActive() || !IsInteractable() || committing || feedback==null || feedback.IsRetained)return;
        committing=true;feedback.PlayConfirm();
        if(holdSeconds>0)feedback.SetPointerCharge(1f);
        commitRoutine=StartCoroutine(ConfirmThenInvoke());
    }
    private IEnumerator ConfirmThenInvoke()
    {
        // Keep one visible press frame before an action closes the sheet or starts a fade.
        yield return new WaitForSecondsRealtime(.10f);
        if(IsActive() && IsInteractable())onClick.Invoke();
        committing=false;commitRoutine=null;
        if(!feedback.IsRetained)feedback.SetPointerCharge(0);
    }
    protected override void OnDisable()
    {
        if(commitRoutine!=null)StopCoroutine(commitRoutine);commitRoutine=null;committing=false;CancelHold();base.OnDisable();
    }
}
