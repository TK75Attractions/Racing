using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class StartCeilingValidation
{
    [MenuItem("Racing/Validation/Start Ceiling Audit")]
    public static void Audit()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var manager = Object.FindFirstObjectByType<Gmanager>();
        manager.course.RebuildCache(); Physics.SyncTransforms();
        var start = (Transform)typeof(Gmanager).GetMethod("GetStartPoint", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
        var lines = new List<string> { "START " + start.position + " forward " + start.forward };
        bool old = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
        try
        {
            foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                var b = collider.bounds;
                if (!collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy ||
                    b.max.x < start.position.x - 25 || b.min.x > start.position.x + 25 ||
                    b.max.z < start.position.z - 25 || b.min.z > start.position.z + 25 || b.max.y < start.position.y + 2) continue;
                string path = collider.name; for (var p=collider.transform.parent;p!=null;p=p.parent) path=p.name+"/"+path;
                string mesh = collider is MeshCollider m ? " mesh="+m.sharedMesh.name+" vertices="+m.sharedMesh.vertexCount+" localBounds="+m.sharedMesh.bounds : "";
                lines.Add(path + " " + collider.GetType().Name + " bounds=" + b + mesh);
            }
            for (int z=-12;z<=12;z+=6) for (int x=-6;x<=6;x+=6)
            {
                Vector3 origin=start.position + new Vector3(x,1,z);
                foreach(var hit in Physics.RaycastAll(origin,Vector3.up,50,~0,QueryTriggerInteraction.Ignore))
                {
                    bool downward=hit.collider.Raycast(new Ray(hit.point+Vector3.up*2,Vector3.down),out _,4);
                    Physics.queriesHitBackfaces=false;
                    bool upward=hit.collider.Raycast(new Ray(origin,Vector3.up),out _,50);
                    Physics.queriesHitBackfaces=true;
                    lines.Add("UP " + origin + " -> " + hit.collider.name + " at="+hit.point+" normal="+hit.normal+" frontUp="+upward+" down="+downward);
                }
            }
        }
        finally { Physics.queriesHitBackfaces=old; }
        Directory.CreateDirectory("Logs/StartCeiling"); File.WriteAllLines("Logs/StartCeiling/audit.txt", lines);
        Debug.Log("START_CEILING_AUDIT_COMPLETE");
    }
}
