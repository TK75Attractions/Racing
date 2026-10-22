#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Natural human-input race observations plus a separate, labelled item attribution laboratory.</summary>
[DefaultExecutionOrder(9992)]
public sealed class RivalFeedbackPlaytestProbe : MonoBehaviour
{
    [Serializable] private sealed class Report
    {
        public bool passed, isolatedAttribution, realOilTrigger, realShieldCollision, realConfusedWallContact, cameraIsolation, liveSwitches, notificationsNamed;
        public string error = "";
        public int frontFrames, rearFrames, cameraChecks;
        public List<string> itemEvents = new List<string>();
    }
    private readonly Report report = new Report();
    private readonly List<GameObject> laboratory = new List<GameObject>();
    private Gmanager manager;
    private RivalFeedbackHUD[] hud;
    private float started = -1, frontSeenAt = -1, demoOneAt = -1, demoTwoAt = -1;
    private bool demoOneShot, demoTwoShot;
    private bool labStarted, demoOne, demoTwo, switchedOff, switchedBack, frontShot, rearShot, done;
    private string Folder => Environment.GetEnvironmentVariable("RACING_BALANCE_OUTPUT") ?? "Logs/RivalFeedback";
    private void OnEnable() { RenderPipelineManager.beginCameraRendering += CheckCamera; }
    private void OnDisable() { RenderPipelineManager.beginCameraRendering -= CheckCamera; }
    private void LateUpdate()
    {
        if (done) return;
        try { Observe(); }
        catch (Exception error) { Fail(error); }
    }
    private void Observe()
    {
        manager = Gmanager.Control;
        if (manager == null || manager.state != Gmanager.State.Game) return;
        if (started < 0)
        {
            started = Time.time;
            hud = new RivalFeedbackHUD[2];
            foreach (var entry in FindObjectsByType<RivalFeedbackHUD>(FindObjectsSortMode.None))
                hud[entry.gameObject.name.EndsWith("_P2") ? 1 : 0] = entry;
            Require(hud[0] != null && hud[1] != null, "Both screens need their own rival feedback.");
            foreach (var entry in manager.Participants)
                entry.Effects.AttackLanded += (target,kind) => report.itemEvents.Add(entry.Label + " -> " + target.PlayerIndex + ": " + kind);
        }
        float elapsed = Time.time - started;
        foreach (var display in hud)
        {
            if (display.FrontVisible) report.frontFrames++;
            if (display.RearVisible) report.rearFrames++;
        }
        if (hud[0].FrontVisible && frontSeenAt < 0) frontSeenAt = elapsed;
        if (!frontShot && hud[0].FrontVisible && elapsed - frontSeenAt > .3f)
        {
            Require(hud[0].ArrowRenderer.transform.lossyScale.x >= .8f,"3D arrows must retain metre-scale geometry outside the UI transform.");
            Bounds bounds=hud[0].ArrowRenderer.bounds;
            foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
            {
                Vector3 screen=manager.GetPlayerViewCamera(0).WorldToViewportPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
                Require(screen.z>0&&screen.x>0&&screen.x<1&&screen.y>0&&screen.y<1,"The nearby rival's arrow must fit within the actual camera view.");
            }
            Shot("front-p1",0); frontShot = true;
        }
        if (!rearShot && elapsed > 1 && hud[1].RearVisible)
        { RearComparison(1); rearShot = true; }
        if (!rearShot && elapsed > 3 && hud[0].RearVisible)
        { RearComparison(0); rearShot = true; }
        if (!labStarted && elapsed > .5f)
        { labStarted = true; StartCoroutine(Lab()); }
        if (!demoOne && elapsed > 6 && elapsed < 10)
        {
            var target = manager.GetParticipant(2);
            if (target != null && target.Car.GetComponent<DebugMover>().HasGroundedWheels && !target.Effects.IsRecoveryProtected)
            {
                manager.GetParticipant(0).Effects.UseItem(RaceItemType.Confuse,target.Effects);
                if (target.Effects.IsConfused)
                {
                    Require(hud[0].HitNotice.Contains(target.Label),"The attacking player's notice must name the real victim.");
                    Require(!hud[1].HitNotice.Contains(target.Label),"An opponent's success must not appear on the other display.");
                    demoOne = true; demoOneAt = elapsed;
                }
            }
        }
        if (!demoTwo && elapsed > 10 && elapsed < 14)
        {
            var target = manager.GetParticipant(0);
            if (target.Car.GetComponent<DebugMover>().HasGroundedWheels && !target.Effects.IsRecoveryProtected)
            {
                manager.GetParticipant(1).Effects.UseItem(RaceItemType.Confuse,target.Effects);
                if (target.Effects.IsConfused)
                {
                    Require(hud[1].HitNotice.Contains(target.Label),"P2 must receive a named confirmation for P1.");
                    demoTwo = true; demoTwoAt = elapsed;
                }
            }
        }
        if (demoOne && !demoOneShot && elapsed > demoOneAt + .25f)
        { Shot("hit-cpu-p1",0); demoOneShot = true; }
        if (demoTwo && !demoTwoShot && elapsed > demoTwoAt + .25f)
        { Shot("hit-player-p2",1); Shot("victim-p1",0); demoTwoShot = true; }
        if (!switchedOff && elapsed > 15)
        {
            manager.Features.SetValidationFeature(RaceFeature.RivalProximity | RaceFeature.ItemHitFeedback,false); switchedOff = true;
        }
        if (switchedOff && !switchedBack && elapsed > 15.3f)
        {
            foreach (var display in hud)
                Require(!display.FrontVisible&&!display.RearVisible&&display.HitNotice.Length==0&&!display.ArrowRenderer.gameObject.activeSelf,"OFF must clear warnings, meshes and pending hit messages.");
            manager.Features.SetValidationFeature(RaceFeature.RivalProximity | RaceFeature.ItemHitFeedback,true);
            report.liveSwitches = true; switchedBack = true;
        }
        if (elapsed > 20)
        {
            Require(report.isolatedAttribution && report.realOilTrigger && report.realShieldCollision && report.realConfusedWallContact,"The item attribution laboratory must finish.");
            Require(report.frontFrames > 0 && report.rearFrames > 0,"Natural driving must exercise both front and rear warnings.");
            Require(demoOne && demoTwo && report.liveSwitches && report.cameraChecks > 0,"Both names, live switches and per-camera isolation must run.");
            report.notificationsNamed = report.cameraIsolation = report.passed = true; done = true; Save();
            Debug.Log("RIVAL_FEEDBACK_PLAYTEST_PASS: natural front/rear warnings, named P1/P2 hits, item protections, physical oil/shield/wall contacts, camera isolation and live switches.");
        }
    }
    private void CheckCamera(ScriptableRenderContext context, Camera camera)
    {
        if (hud == null || done) return;
        try
        {
            for (int i = 0; i < hud.Length; i++)
            {
                var renderer = hud[i].ArrowRenderer;
                if (renderer != null && renderer.enabled)
                    Require(camera == manager.GetPlayerViewCamera(i),"A rival arrow leaked to another player's or overlay camera.");
            }
            report.cameraChecks++;
        }
        catch (Exception error) { Fail(error); }
    }
    private IEnumerator Lab()
    {
        // This laboratory never repositions race cars or injects their velocity. It uses separate test bodies.
        var source = Car("Lab source",new Vector3(10000,1000,10000));
        var victim = Car("Lab victim",new Vector3(10020,1000,10000));
        var counts = new Dictionary<ItemImpactKind,int>();
        source.AttackLanded += (target,kind) => { Require(target == victim,"Hit attribution must preserve the victim instance."); counts[kind] = Count(counts,kind)+1; };
        bool protection = manager.Features.Settings.IsEnabled(RaceFeature.HitProtection);
        manager.Features.SetValidationFeature(RaceFeature.HitProtection,false);
        try
        {
            victim.UseItem(RaceItemType.Shield,null);
            Require(victim.ApplyOil(source)&&!victim.IsOiled&&!victim.ApplyConfuse(source)&&!victim.ApplySpin(Vector3.right,source),"Shield blocks cannot become successful hits.");
            Require(counts.Count==0,"Defended attacks must emit no success event.");
            victim.ClearAll(); victim.UseItem(RaceItemType.Rocket,null);
            Require(victim.ApplyOil(source)&&!victim.IsOiled&&!victim.ApplySpin(Vector3.right,source),"Rocket immunity must not emit oil or spin hits.");
            Require(counts.Count==0,"Rocket immunity must emit no success event."); victim.ClearAll();
            victim.GrantRecoveryProtection(2);
            Require(!victim.ApplyOil(source)&&!victim.ApplyConfuse(source)&&!victim.ApplySpin(Vector3.right,source),"Recovery protection blocks all attack confirmations.");
            victim.ClearAll(); victim.enabled=false;
            Require(!victim.ApplyConfuse(source),"Finished/disabled cars must not confirm hits."); victim.enabled=true;
            Require(victim.ApplyOil(victim)&&counts.Count==0,"Own oil cannot produce outgoing opponent feedback."); victim.ClearAll();
            manager.Features.SetValidationFeature(RaceFeature.HitProtection,true);
            Require(victim.ApplyConfuse(source)&&!victim.ApplyOil(source),"Protection must reject a second affliction rather than confirm it.");
            Require(Count(counts,ItemImpactKind.SteeringReversed)==1&&Count(counts,ItemImpactKind.OilSlip)==0,"Only the accepted affliction can confirm.");
            victim.ClearAll(); manager.Features.SetValidationFeature(RaceFeature.HitProtection,false);
            Require(victim.ApplySpin(Vector3.right,source)&&!victim.ApplySpin(Vector3.left,source),"Duplicate spin contacts must not confirm twice.");
            Require(Count(counts,ItemImpactKind.ShieldSpin)==1,"Exactly one spin confirmation is required."); victim.ClearAll();
            var slick = OilSlick.Spawn(victim.transform.position,Vector3.up,Vector3.forward,source);
            laboratory.Add(slick.gameObject);
            typeof(OilSlick).GetMethod("OnTriggerEnter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(slick,new object[]{victim.GetComponent<Collider>()});
            typeof(OilSlick).GetMethod("OnTriggerEnter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(slick,new object[]{victim.GetComponent<Collider>()});
            Require(Count(counts,ItemImpactKind.OilSlip)==1,"A delayed oil slick must preserve its source and consume only once.");
            report.isolatedAttribution=true;
        }
        catch (Exception error) { Fail(error); yield break; }
        finally { manager.Features.SetValidationFeature(RaceFeature.HitProtection,protection); }

        // Let actual trigger and contact callbacks run through Unity physics too.
        victim.ClearAll(); source.ClearAll();
        int oilsBefore = Count(counts,ItemImpactKind.OilSlip);
        var actualOil=OilSlick.Spawn(victim.transform.position,Vector3.up,Vector3.forward,source); laboratory.Add(actualOil.gameObject);
        yield return new WaitForSeconds(.12f);
        if (!Check(Count(counts,ItemImpactKind.OilSlip)==oilsBefore+1,"Actual oil trigger must report exactly one named source/victim.")) yield break;
        report.realOilTrigger=true;
        victim.ClearAll(); source.ClearAll();
        source.transform.position=victim.transform.position+Vector3.back*3; source.UseItem(RaceItemType.Shield,null);
        source.GetComponent<Rigidbody>().linearVelocity=Vector3.forward*12;
        int spinsBefore=Count(counts,ItemImpactKind.ShieldSpin);
        yield return new WaitForSeconds(.3f);
        if (!Check(Count(counts,ItemImpactKind.ShieldSpin)==spinsBefore+1,"Actual shield collision must report exactly one hit.")) yield break;
        report.realShieldCollision=true;
        source.GetComponent<Rigidbody>().linearVelocity=Vector3.zero;
        victim.ClearAll(); source.ClearAll();
        // Stop the source from re-colliding; the victim then reaches a separate stationary wall.
        source.GetComponent<Collider>().enabled=false;
        var wall=new GameObject("Lab static wall"); laboratory.Add(wall);
        wall.transform.position=victim.transform.position+Vector3.forward*3;
        wall.AddComponent<BoxCollider>().size=new Vector3(8,8,.5f);
        victim.ApplyConfuse(source); victim.GetComponent<Rigidbody>().linearVelocity=Vector3.forward*12;
        yield return new WaitForSeconds(.4f);
        if (!Check(Count(counts,ItemImpactKind.ConfusedWallContact)==1,"Only an actual wall impact during confusion may upgrade the hit notification.")) yield break;
        report.realConfusedWallContact=true;
        foreach(var item in laboratory) if(item!=null) Destroy(item);
        laboratory.Clear();
    }
    private CarItemEffects Car(string name,Vector3 position)
    {
        var root=new GameObject(name); laboratory.Add(root); root.transform.position=position;
        root.AddComponent<BoxCollider>().size=new Vector3(1.5f,1,2);
        var body=root.AddComponent<Rigidbody>(); body.useGravity=false; body.mass=1000;
        return root.AddComponent<CarItemEffects>();
    }
    private static int Count(Dictionary<ItemImpactKind,int> counts,ItemImpactKind kind)=>counts.TryGetValue(kind,out int n)?n:0;
    private void Shot(string name,int index)
    {
        Directory.CreateDirectory(Folder);
        var rigs=(PlayerDisplayRig[])typeof(Gmanager).GetField("displayRigs",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(manager);
        File.WriteAllBytes(Path.Combine(Folder,name+".jpg"),rigs[index].CaptureJpeg(1280,720));
    }
    private void RearComparison(int index)
    {
        var graphic=hud[index].GetComponentInChildren<RivalWarningGraphic>();
        Require(graphic!=null&&graphic.canvasRenderer!=null,"Rear warnings require a real UI renderer.");
        Shot("rear-p"+(index+1),index);
    }
    private bool Check(bool ok,string reason) { if(!ok)Fail(new InvalidOperationException(reason)); return ok; }
    private static void Require(bool ok,string reason) { if(!ok)throw new InvalidOperationException(reason); }
    private void Fail(Exception error)
    {
        if(done)return;
        report.error=error.ToString();done=true;Save();Debug.LogException(error);
        if(Application.isBatchMode)UnityEditor.EditorApplication.Exit(1);
    }
    private void Save(){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"rival-feedback-checks.json"),JsonUtility.ToJson(report,true));}
    private void OnDestroy(){foreach(var item in laboratory)if(item!=null)Destroy(item);}
}
#endif
