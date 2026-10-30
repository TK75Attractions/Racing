using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Real keyboard device samples and the same remote-input adapter used by the dashboard.</summary>
public static class NoviceInputValidation
{
    private sealed class Neutral:IDriveInputSource
    {
        public int PlayerIndex=>0;public string DeviceId=>"CONTRADICTORY_INPUT_QA";public bool IsConnected=>true;
        public DriveInputState CurrentState=>DriveInputState.Neutral;public void UpdateInput(float dt){}public void Dispose(){}
    }
    public static void ValidateDashboard()
    {
        var lease=new DashboardInputLease();var source=new DashboardDriveInputSource(new Neutral(),lease,()=>0);
        Require(lease.TryApply("qa",1,"begin",DashboardInputLease.Movement,0,out _),"Dashboard lease setup.");
        source.UpdateInput(.02f);source.UpdateInput(.02f);
        Require(source.CurrentState.pedal==0&&source.CurrentState.steering==0,"Opposing dashboard buttons must cancel each other.");
        long sequence=1;
        foreach(var button in new[]{DashboardInputButtons.Accelerate,DashboardInputButtons.Brake,DashboardInputButtons.Left,DashboardInputButtons.Right})
        {
            Require(lease.TryApply("qa",++sequence,"hold",button,0,out _),"Dashboard hold setup.");source.UpdateInput(.02f);
            var state=source.CurrentState;
            Require(state.pedal==(button==DashboardInputButtons.Accelerate?1:button==DashboardInputButtons.Brake?-1:0)&&
                state.steering==(button==DashboardInputButtons.Right?10:button==DashboardInputButtons.Left?-10:0),"Single dashboard buttons must retain their direction.");
        }
        Require(lease.TryApply("qa",++sequence,"release",DashboardInputButtons.None,0,out _),"Release setup.");source.UpdateInput(.02f);
        Require(source.CurrentState.pedal==0&&source.CurrentState.steering==0,"A release must not leave remote movement held.");
        Debug.Log("NOVICE_DASHBOARD_INPUT_PASS: conflicting buttons neutral, single directions retained, release clears movement.");
    }
    public static IEnumerator ValidateKeyboard()
    {
        var background=InputSystem.settings.backgroundBehavior;var editor=InputSystem.settings.editorInputBehaviorInPlayMode;
        var race=Gmanager.Control;bool maintenance=race?.IsMaintenanceActive==true;
        var keyboard=InputSystem.AddDevice<Keyboard>("NoviceInputQA");
        try
        {
            race?.SetMaintenance(true);
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard.MakeCurrent();
            for(int player=0;player<2;player++)
            {
                var source=new KeyboardDriveInputSource(player);
                Key forward=player==0?Key.W:Key.UpArrow,reverse=player==0?Key.S:Key.DownArrow,left=player==0?Key.A:Key.LeftArrow,right=player==0?Key.D:Key.RightArrow;
                foreach(var pair in new[]{new[]{forward,reverse},new[]{left,right},new[]{forward,reverse,left,right}})
                {
                    InputState.Change(keyboard,new KeyboardState(pair));keyboard.MakeCurrent();source.UpdateInput(.02f);
                    Require(source.CurrentState.pedal==0&&source.CurrentState.steering==0,"P"+(player+1)+" opposing keyboard inputs must cancel.");
                }
                foreach(var key in new[]{forward,reverse,left,right})
                {
                    InputState.Change(keyboard,new KeyboardState(key));keyboard.MakeCurrent();source.UpdateInput(.02f);
                    Require(source.CurrentState.pedal==(key==forward?1:key==reverse?-1:0)&&source.CurrentState.steering==(key==right?10:key==left?-10:0),"Single-key driving must remain unchanged: P"+(player+1)+" "+key+" pedal="+source.CurrentState.pedal+" steering="+source.CurrentState.steering);
                }
                InputState.Change(keyboard,new KeyboardState());source.UpdateInput(.02f);
                Require(source.CurrentState.pedal==0&&source.CurrentState.steering==0,"Key release must be neutral.");
            }
            yield return null;
            Debug.Log("NOVICE_KEYBOARD_INPUT_PASS: real P1/P2 opposing/single/released key samples.");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);InputSystem.settings.backgroundBehavior=background;
            InputSystem.settings.editorInputBehaviorInPlayMode=editor;race?.SetMaintenance(maintenance);
        }
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
