using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Visitor-facing regression checks using production UI, driver decisions and collision response.</summary>
public static class VisitorHandlingValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        RacingHUDFontBuilder.Build();
        ValidateDriving();
        SoundEffectsValidation.RunAudioCore();
        ArcadeUIValidation.Run();
        SteeringAndItemFairnessValidation.Run();
        PlayerStartTubeResultValidation.Run();
        Debug.Log("VISITOR_HANDLING_VALIDATION_PASS: CPU clearance, CPU-only confuse, bounded wall rebound and center assistance, dedicated rocket sound, UI labels, auto-return, 80-degree/three-second recovery.");
    }
    private static void ValidateDriving()
    {
        var previous=Gmanager.Control;
        var root=new GameObject("Visitor handling QA");
        try
        {
            var host=new GameObject("Inactive race manager");host.transform.SetParent(root.transform);host.SetActive(false);
            var race=host.AddComponent<Gmanager>();Gmanager.Control=race;race.state=Gmanager.State.Game;
            var course=root.AddComponent<RaceCourse>();race.course=course;
            JsonUtility.FromJsonOverwrite("{\"closedLoop\":false,\"generateRoad\":false,\"cornerRoundingDistance\":0,\"slopeBlendDistance\":0,\"waypoints\":[{\"position\":{\"x\":10000,\"y\":10000},\"width\":20},{\"position\":{\"x\":10000,\"y\":10100},\"width\":20}]}",course);course.RebuildCache();
            var cars=(List<RaceParticipant>)race.Participants;
            for(int i=0;i<2;i++)
            {
                var car=new GameObject("QA driver "+i,typeof(Rigidbody));car.transform.SetParent(root.transform);
                var body=car.GetComponent<Rigidbody>();body.useGravity=false;
                for(int t=0;t<4;t++)
                {
                    var wheel=new GameObject("Wheel "+t,typeof(TireForce));wheel.transform.SetParent(car.transform);
                    Set(wheel.GetComponent<TireForce>(),"isGrounded",true);
                }
                var effects=car.AddComponent<CarItemEffects>();Call(effects,"Awake");
                var mover=car.AddComponent<DebugMover>();Call(mover,"Awake");
                var entry=new RaceParticipant{Index=i==0?0:2,IsCpu=i==1,Car=car,Body=body,Effects=effects,Position=i==0?2:1};cars.Add(entry);
            }
            var cpu=cars[1];var human=cars[0];
            var driver=cpu.Car.AddComponent<NpcRaceDriver>();Set(driver,"manager",race);Set(driver,"course",course);Set(driver,"body",cpu.Body);Set(driver,"entry",cpu);
            course.TryGetSampleAtProgress(50,out var sample);
            bool Decide(float ownSide,float humanSide,float ahead,bool canManeuver,out float requested)
            {
                cpu.Body.position=sample.position+sample.right*ownSide;
                human.Body.position=sample.position+sample.right*humanSide+sample.forward*ahead;
                human.Body.linearVelocity=sample.forward*20f;
                Set(driver,"desiredLane",humanSide);Set(driver,"lane",ownSide);
                object[] args={sample,3f,canManeuver,30f};
                bool yielding=(bool)typeof(NpcRaceDriver).GetMethod("ApplyHumanClearance",Private).Invoke(driver,args);
                requested=(float)args[3];return yielding;
            }
            Require(Decide(1,2,0,true,out float speed)&&speed<=17f,"A close alongside CPU must fall behind instead of matching speed.");
            Require((float)Get(driver,"desiredLane")<0,"CPU must move away from the human, without crossing their side.");
            Require(Decide(3,1,0,true,out speed)&&speed<=17f&&(float)Get(driver,"desiredLane")==3f,"A blocked passing lane must retain physical separation, even when the old lane target crossed the player.");
            Require(!Decide(-3,3,0,true,out speed)&&speed==30,"A separated CPU may race normally.");
            Require(!Decide(1,2,0,false,out speed)&&speed==30,"Airborne/ramp clearance must preserve launch speed.");
            Require(!Decide(1,2,-20,true,out speed)&&speed==30,"A distant car must not trigger close-contact braking.");

            var response=cpu.Car.AddComponent<CarWallCollisionResponse>();Call(response,"Awake");
            foreach(float incoming in new[]{6f,25f,50f})
            {
                cpu.Body.linearVelocity=new Vector3(-incoming,-2,20);
                for(int n=0;n<4;n++)Call(response,"ResolveWallVelocity",Vector3.right,incoming);
                Require(cpu.Body.linearVelocity.x>=0&&cpu.Body.linearVelocity.x<=1.2001f&&cpu.Body.linearVelocity.y==-2&&cpu.Body.linearVelocity.z==20,"Impact must cap rebound and preserve tangential/fall motion without stacking.");
            }
            cpu.Body.position=sample.position+sample.right*5;
            cpu.Body.linearVelocity=sample.forward*20;
            Call(response,"BeginCenterAssist",-sample.right);
            for(int n=0;n<60;n++)Call(response,"ApplyCenterAssist");
            Require(cpu.Body.linearVelocity.x<0&&cpu.Body.linearVelocity.x>=-2.5001f&&cpu.Body.linearVelocity.z==20,"Center assistance must be gentle, bounded and preserve forward speed.");
            cpu.Body.linearVelocity=Vector3.forward*20;
            foreach(var tire in cpu.Car.GetComponentsInChildren<TireForce>())Set(tire,"isGrounded",false);
            Call(response,"ApplyCenterAssist");Require(cpu.Body.linearVelocity==Vector3.forward*20,"Airborne cars must receive no wall-center assistance.");
            foreach(var tire in cpu.Car.GetComponentsInChildren<TireForce>())Set(tire,"isGrounded",true);
            Call(response,"BeginCenterAssist",sample.right);Call(response,"ApplyCenterAssist");
            Require(cpu.Body.linearVelocity==Vector3.forward*20,"An obstacle directly between the car and center must not be forced through.");
            Call(response,"BeginCenterAssist",-sample.right);Set(response,"centerAssistUntil",float.NegativeInfinity);Call(response,"ApplyCenterAssist");
            Require(cpu.Body.linearVelocity==Vector3.forward*20,"Assistance must expire.");

            var effectsBus=host.AddComponent<RaceSoundEffects>();Call(effectsBus,"Awake");
            var mixer=host.AddComponent<RaceSoundEffectsMixer>();effectsBus.RegisterMixer(0,mixer);
            int rockets=0,boosts=0;effectsBus.CueQueued+=(cue,player)=>{if(cue==RaceSoundCue.Rocket&&player==0)rockets++;if(cue==RaceSoundCue.Boost)boosts++;};
            human.Effects.UseItem(RaceItemType.Rocket,null);
            Require(rockets==1&&boosts==0,"A human rocket must enqueue its dedicated sound exactly once for its owner.");
            cpu.Effects.UseItem(RaceItemType.Rocket,null);Require(rockets==1,"A CPU must not play its rocket into a human's direct sound bus.");
        }
        finally {Object.DestroyImmediate(root);Gmanager.Control=previous;}
    }
    private static object Get(object target,string field)=>target.GetType().GetField(field,Private).GetValue(target);
    private static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private).SetValue(target,value);
    private static void Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Private).Invoke(target,args);
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
