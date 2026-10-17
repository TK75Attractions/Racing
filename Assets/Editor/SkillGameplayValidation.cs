using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SkillGameplayValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch editor.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        ValidateStartingLine();
        ValidateOperatorStop();
        Debug.Log("SKILL_GAMEPLAY_RULES_PASS");
    }
    private static void ValidateStartingLine()
    {
        GameObject root = new GameObject("Starting line QA");
        try
        {
            RaceCourse course = MakeCourse(root);
            LapManager laps = root.AddComponent<LapManager>(); Set(laps,"raceCourse",course);
            GameObject goal = new GameObject("Goal",typeof(GoalSensor)); goal.transform.SetParent(root.transform);
            GameObject start = new GameObject("Grid"); start.transform.SetParent(root.transform); start.transform.position=Vector3.back*20;
            GameObject car = new GameObject("Car",typeof(Rigidbody)); car.transform.SetParent(root.transform);
            Rigidbody body=car.GetComponent<Rigidbody>(); body.isKinematic=true; body.position=start.transform.position;
            laps.RegisterCar(body,start.transform); var data=laps.GetCarData(body); data.currentLapTime=8;
            Require(!laps.OnCarPassGoal(body,goal.transform) && data.lapCount==0 && data.currentLapTime==8,
                "Initial grid-to-line crossing must not complete a lap or discard race time.");
            Require(laps.OnCarPassGoal(body,goal.transform) && data.lapCount==1,"A subsequent goal crossing must count without checkpoints.");
            Require(laps.OnCarPassGoal(body,goal.transform) && data.isFinished,"Two full circuits must finish a two-lap race.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static RaceCourse MakeCourse(GameObject root)
    {
        RaceCourse course=root.AddComponent<RaceCourse>();
        var settings=new SerializedObject(course); settings.FindProperty("cornerRoundingDistance").floatValue=0;
        settings.FindProperty("slopeBlendDistance").floatValue=0; settings.FindProperty("closedLoop").boolValue=true;
        var points=settings.FindProperty("waypoints"); points.arraySize=4;
        Vector3[] positions={new Vector3(0,0,-100),new Vector3(0,0,100),new Vector3(100,0,100),new Vector3(100,0,-100)};
        for(int i=0;i<4;i++)
        {
            var point=points.GetArrayElementAtIndex(i); point.FindPropertyRelative("position").vector2Value=new Vector2(positions[i].x,positions[i].z);
            point.FindPropertyRelative("height").floatValue=0; point.FindPropertyRelative("width").floatValue=20;
            point.FindPropertyRelative("curve").floatValue=0; point.FindPropertyRelative("jumpToNext").boolValue=false;
        }
        settings.ApplyModifiedPropertiesWithoutUndo(); course.RebuildCache(); return course;
    }
    private static void ValidateOperatorStop()
    {
        GameObject root=new GameObject("Operator stop QA"); root.SetActive(false);
        try
        {
            Gmanager manager=root.AddComponent<Gmanager>(); manager.state=Gmanager.State.Game;
            manager.ShowResult();
            Require(manager.state==Gmanager.State.Result,"An operator stop must reach results.");
            for(int i=0;i<2;i++)Require(manager.LatestSessionResult.GetPlayerResult(i)?.didFinish==false,
                "Stopping an unfinished race must not manufacture a winner.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
    private static object Get(object target,string name)=>target.GetType().GetField(name,Private).GetValue(target);
    private static void Call(object target,string name)=>target.GetType().GetMethod(name,Private).Invoke(target,null);
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
