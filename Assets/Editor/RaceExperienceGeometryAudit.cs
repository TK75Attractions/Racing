using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Audit historical band-only telemetry against the actual production driving-area geometry.</summary>
public static class RaceExperienceGeometryAudit
{
    [Serializable] private sealed class Point
    {
        public string player; public float time; public Vector3 position;
        public bool insideTube, insideDrivingArea, outsideProductionArea;
    }
    [Serializable] private sealed class Report
    {
        public string trace;
        public int oldBandOutsideSamples, legalTubeSamples, outsideProductionSamples;
        public List<Point> samples = new List<Point>();
    }
    public static void Run()
    {
        string path=Environment.GetEnvironmentVariable("RACING_GEOMETRY_AUDIT_TRACE");
        if(string.IsNullOrEmpty(path))return;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        RaceCourse course=UnityEngine.Object.FindFirstObjectByType<RaceCourse>();course.RebuildRoad();Physics.SyncTransforms();
        var report=new Report {trace=path};
        string[] lines=File.ReadAllLines(path);string[] header=lines[0].Split(',');
        int car=Array.IndexOf(header,"car"), off=Array.IndexOf(header,"offCourse"), x=Array.IndexOf(header,"x"), y=Array.IndexOf(header,"y"), z=Array.IndexOf(header,"z");
        for(int i=1;i<lines.Length;i++)
        {
            string[] row=lines[i].Split(',');
            if(row.Length<header.Length || (row[car]!="P1" && row[car]!="P2") || row[off]!="True")continue;
            var point=new Point {player=row[car],time=float.Parse(row[0],CultureInfo.InvariantCulture),position=new Vector3(float.Parse(row[x],CultureInfo.InvariantCulture),float.Parse(row[y],CultureInfo.InvariantCulture),float.Parse(row[z],CultureInfo.InvariantCulture))};
            point.insideTube=course.IsPointInsideTube(point.position);point.insideDrivingArea=course.IsPointInsideDrivingArea(point.position);
            course.TryGetSampleAtProgress(course.GetProgressDistance(point.position),out RaceCourse.CourseSample sample);
            point.outsideProductionArea=!point.insideDrivingArea || (point.position.y<sample.position.y-4f && !point.insideTube);
            report.oldBandOutsideSamples++;if(point.insideTube && !point.outsideProductionArea)report.legalTubeSamples++;
            if(point.outsideProductionArea)report.outsideProductionSamples++;report.samples.Add(point);
        }
        string output=Environment.GetEnvironmentVariable("RACING_GEOMETRY_AUDIT_OUTPUT")??"/private/tmp/racing-tuning-tube-audit.json";
        File.WriteAllText(output,JsonUtility.ToJson(report,true));
        Debug.Log("RACE_GEOMETRY_AUDIT: old band samples="+report.oldBandOutsideSamples+", legal tube="+report.legalTubeSamples+", outside actual driving area="+report.outsideProductionSamples);
    }
}
