using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.EventSystems;

/// <summary>Opt-in rehearsal using the real scene, lifecycle, canvases, input gates and physics.</summary>
public sealed class MaintenancePlaytestProbe : MonoBehaviour
{
    public bool VerifyRestart;
    public Action<string> Completed;
    private bool finished;
    private Gmanager manager;
    private const string Output = "Artifacts/Maintenance";
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        manager = Gmanager.Control;
        Require(manager != null && manager.MaintenanceAvailable, "Real manager must initialize maintenance.");
        if (VerifyRestart)
        {
            Require(manager.IsMaintenanceActive && Time.timeScale == 0, "Restart must restore saved maintenance before input.");
            Require(manager.Maintenance.ScreenCount == 3, "Restart must cover both players and broadcast.");
            manager.SetMaintenance(false);
            Require(!manager.IsMaintenanceActive && Time.timeScale == 1, "Restarted cabinet must be able to exit maintenance.");
            Finish("passed"); yield break;
        }
        Directory.CreateDirectory(Output);
        Time.timeScale = 2;
        yield return CheckPhase("Title");
        manager.StartGame();
        Require(!manager.CanUseDashboardControls,"The real selection transition must be active.");
        yield return CheckPhase(manager.state.ToString());
        Debug.Log("MAINTENANCE_TRANSITION_PASS: a selection transition stays frozen until maintenance clears.");
        yield return new WaitUntil(() => manager.state == Gmanager.State.CarSelection && manager.CanUseDashboardControls);
        yield return CheckPhase("CarSelection");
        Require(manager.AdvanceDashboardToTutorial(), "Operator must advance to actual tutorial.");
        yield return new WaitUntil(() => manager.state == Gmanager.State.Tutorial && manager.CanUseDashboardControls);
        yield return CheckPhase("Tutorial");
        Require(manager.AdvanceDashboardToCountdown(), "Operator must advance to actual countdown.");
        yield return new WaitUntil(() => manager.state == Gmanager.State.Countdown && manager.CanUseDashboardControls);
        yield return CheckPhase("Countdown");
        yield return new WaitUntil(() => manager.state == Gmanager.State.Game && manager.CanUseDashboardControls);
        yield return CheckPhase("Game", capture: true);
        // Existing debug finish commands exercise the real celebration callback without driving two laps.
        var finish=typeof(Gmanager).GetMethod("AdvanceDebugLap",BindingFlags.Instance|BindingFlags.NonPublic);
        finish.Invoke(manager,new object[]{0,true}); finish.Invoke(manager,new object[]{1,true});
        Require(manager.state == Gmanager.State.Goal, "Debug finish must enter the real goal celebration phase.");
        yield return CheckPhase("Goal");
        yield return new WaitUntil(() => manager.state == Gmanager.State.Result && manager.CanUseDashboardControls);
        yield return CheckPhase("Result");
        // Leave the switch ON and restart Play: this tests reading persisted state, not just writing it.
        manager.SetMaintenance(true);
        Finish("restart");
    }
    private IEnumerator CheckPhase(string phase, bool capture = false)
    {
        Require(manager.state.ToString() == phase, "Wrong phase: " + phase);
        float raceTime = manager.time, countdown = manager.CountdownTimeRemaining;
        var preparation = new[] {manager.GetPlayerPreparationState(0), manager.GetPlayerPreparationState(1)};
        var positions = new Vector3[manager.Participants.Count];
        for (int i=0;i<positions.Length;i++) positions[i]=manager.Participants[i].Body.position;
        PlayerDriveInputGate heldGate=null, blockedGate=null; IDriveInputSource original=null; var held=new HeldInput();
        if(capture)
        {
            var runtime=(Array)typeof(Gmanager).GetField("players",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager);
            heldGate=(PlayerDriveInputGate)runtime.GetValue(0).GetType().GetField("inputGate").GetValue(runtime.GetValue(0));
            blockedGate=(PlayerDriveInputGate)runtime.GetValue(1).GetType().GetField("inputGate").GetValue(runtime.GetValue(1));
            original=heldGate.Source; heldGate.SwitchSource(held); blockedGate.SetBlocked(true);
        }
        RectTransform motionFixture=null, movingCard=null; UIRevealSequence reveal=null;
        RacingMenuButton pendingButton=null, blockedButton=null; int pendingActions=0, blockedActions=0;
        if(capture)
        {
            var rigs=(PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager);
            motionFixture=RacingUITheme.Rect(rigs[0].Canvas.transform,"Maintenance motion QA",Vector2.zero,Vector2.one);
            movingCard=RacingUITheme.Rect(motionFixture,"Card",Vector2.zero,Vector2.one);
            reveal=motionFixture.gameObject.AddComponent<UIRevealSequence>();
            reveal.Configure(motionFixture,new[]{"Card"},new Vector2(24,0),0,.2f);reveal.Play();
            pendingButton=NeonUI.Button(motionFixture,"Pending","QA","",RacingIconGraphic.Icon.Back,Vector2.zero,Vector2.one,false,()=>pendingActions++);
            pendingButton.PlayConfirmationSound=false;pendingButton.ConfirmationDelay=.2f;
            blockedButton=NeonUI.Button(motionFixture,"Blocked","QA","",RacingIconGraphic.Icon.Back,Vector2.zero,Vector2.one,false,()=>blockedActions++);
            blockedButton.PlayConfirmationSound=false;blockedButton.ConfirmationDelay=0;
            pendingButton.OnSubmit(new BaseEventData(EventSystem.current));
        }
        bool broadcastEnabled=manager.Features.Settings.IsEnabled(RaceFeature.BroadcastScreen);
        if(capture) manager.Features.SetFeature(RaceFeature.BroadcastScreen,false);
        bool oldPause=AudioListener.pause;
        Require(manager.SetMaintenance(true), "Enable maintenance");
        Require(manager.Maintenance.ScreenCount == 3 && manager.IsMaintenanceActive && Time.timeScale == 0 && AudioListener.pause && RaceAudioSettings.MaintenanceMuted,
            "Every output, physics and audio must enter maintenance in " + phase);
        Require(!manager.CanUseDashboardControls && !manager.CanUseDashboardPlayerInput(0) && !manager.CanUseDashboardPlayerInput(1), "Race controls and input must be blocked.");
        double presentationTime=UIMotion.Realtime;
        Vector2 frozenCard=movingCard!=null?movingCard.anchoredPosition:Vector2.zero;
        if(capture)
        {
            blockedButton.OnSubmit(new BaseEventData(EventSystem.current));
            Require(blockedActions==0,"Keyboard confirmation must not act behind the maintenance overlay.");
        }
        var screens=manager.transform.parent.GetComponentsInChildren<Canvas>(true);
        int visible=0;
        foreach(var canvas in screens)
        {
            var screen=canvas.transform.Find(RaceMaintenanceScreen.RootName);
            if(screen==null || !screen.gameObject.activeInHierarchy)continue;
            foreach(var label in screen.GetComponentsInChildren<TMPro.TMP_Text>()) Require(label.font.HasCharacters(label.text),"Maintenance text missing font glyphs: "+label.name);
            visible++; Require(screen.GetSiblingIndex()==canvas.transform.childCount-1,"Maintenance must cover transitions and other UI.");
        }
        Require(visible==3,"All three real canvases must show maintenance.");
        manager.StartGame(); manager.StartAutodriveTestRace(); manager.ShowResult(); manager.RetryGame(); manager.ResetGame();
        yield return new WaitForSecondsRealtime(phase=="Goal"?3.5f:.3f);
        Require(Math.Abs(UIMotion.Realtime-presentationTime)<.0001 && UIMotion.DeltaTime==0,
            "Presentation clock must freeze without treating an ordinary game pause as maintenance.");
        if(capture)Require(pendingActions==0 && blockedActions==0 && Vector2.Distance(movingCard.anchoredPosition,frozenCard)<.01f && reveal.IsPlaying,
            $"Pending buttons/reveal advanced: pending={pendingActions}, blocked={blockedActions}, card={movingCard.anchoredPosition}, frozen={frozenCard}, playing={reveal.IsPlaying}.");
        Require(manager.GetPlayerPreparationState(0)==preparation[0] && manager.GetPlayerPreparationState(1)==preparation[1], "Maintenance must preserve individual preparation phases.");
        Require(manager.state.ToString()==phase && Mathf.Abs(manager.time-raceTime)<.001f && Mathf.Abs(manager.CountdownTimeRemaining-countdown)<.001f,"State and timers advanced behind maintenance: "+phase);
        for(int i=0;i<positions.Length;i++) Require(Vector3.Distance(positions[i],manager.Participants[i].Body.position)<.0001f,"A car moved while in maintenance.");
        Require(manager.Broadcast.PresentationCamera.isActiveAndEnabled,"Broadcast output must show maintenance even when its normal feature is OFF.");
        if(capture)
        {
            var rigs=(PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager);
            for(int i=0;i<rigs.Length;i++)
            {
                byte[] frame=rigs[i].CaptureJpeg(1280,720); Require(frame!=null,"Live preview must contain maintenance.");
                File.WriteAllBytes(Output+"/player-"+(i+1)+".jpg",frame);
            }
            CaptureBroadcast();
            manager.Maintenance.enabled=false;
            Require(Time.timeScale==2 && !manager.IsMaintenanceActive,"Disabling the owner must release its pause.");
            manager.Maintenance.enabled=true;
            Require(Time.timeScale==0 && manager.IsMaintenanceActive && manager.Maintenance.ScreenCount==3,"Re-enable must restore without duplicated screens.");
        }
        Require(PlayerPrefs.GetInt(RaceMaintenanceController.PreferenceKey)==1,"Maintenance must persist across restart.");
        manager.SetMaintenance(false);
        Require(!manager.IsMaintenanceActive && Time.timeScale==2 && AudioListener.pause==oldPause && !RaceAudioSettings.MaintenanceMuted,"Restore the previous clock/audio state.");
        if(capture)
        {
            Require(heldGate.NeedsPedalRelease && heldGate.CurrentState.pedal==0,"Held pedal must stay neutral after maintenance.");
            Require(blockedGate.IsBlocked,"An existing player block must survive maintenance.");
            held.Pedal=0; _=heldGate.CurrentState; held.Pedal=.8f;
            Require(heldGate.CurrentState.pedal>.7f,"A deliberate pedal release must re-arm driving.");
            heldGate.SwitchSource(original); blockedGate.SetBlocked(false);
            manager.Features.SetFeature(RaceFeature.BroadcastScreen,broadcastEnabled);
        }
        if(capture)
        {
            blockedButton.OnSubmit(new BaseEventData(EventSystem.current));
            yield return new WaitForSecondsRealtime(.35f);
            Require(pendingActions==1 && blockedActions==1 && !reveal.IsPlaying,
                "Resuming maintenance must finish the existing UI beat once and accept a new deliberate action.");
            Destroy(motionFixture.gameObject);
            Debug.Log("MAINTENANCE_UI_MOTION_PASS: frozen presentation clock, reveal pose, pending callback and keyboard input; exact one-shot resume.");
        }
        Debug.Log("MAINTENANCE_PHASE_PASS: "+phase);
    }
    private void CaptureBroadcast()
    {
        var camera=manager.Broadcast.PresentationCamera; var canvas=manager.Broadcast.PresentationCanvas;
        var target=RenderTexture.GetTemporary(1280,720,24); var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        var previous=RenderTexture.active; var cameraTarget=camera.targetTexture; bool enabled=camera.enabled; var mode=canvas.renderMode;
        try
        {
            camera.enabled=true; camera.targetTexture=target; Canvas.ForceUpdateCanvases();
            // The broadcast presenter is a base camera. Its normal render includes
            // the owned ScreenSpaceCamera canvas; emitting geometry again duplicates it.
            camera.Render();
            RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
            File.WriteAllBytes(Output+"/broadcast.jpg",image.EncodeToJPG(85));
        }
        finally {canvas.renderMode=mode;camera.enabled=enabled;camera.targetTexture=cameraTarget;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Destroy(image);}
    }
    private sealed class HeldInput : IDriveInputSource
    {
        public float Pedal=.8f;
        public int PlayerIndex=>0; public string DeviceId=>"Maintenance test"; public bool IsConnected=>true;
        public DriveInputState CurrentState=>new DriveInputState{pedal=Pedal};
        public void UpdateInput(float dt){} public void Dispose(){}
    }
    private void OnLog(string message,string stack,LogType type) { if(type==LogType.Exception && !finished) Finish("failed: "+message); }
    private void Finish(string result) { if(finished)return;finished=true;Completed?.Invoke(result); }
    private void OnDestroy(){Application.logMessageReceived-=OnLog;}
}
