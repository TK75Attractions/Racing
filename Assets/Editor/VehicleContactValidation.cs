using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class VehicleContactValidation
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var manager=UnityEngine.Object.FindFirstObjectByType<Gmanager>();
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var physics=scene.GetPhysicsScene();
            if(!physics.IsValid()||physics==Physics.defaultPhysicsScene)throw new Exception("An isolated physics scene is required.");
            foreach(int trial in new[]{0,1,2})
            {
                var cars=new GameObject[2];var bodies=new Rigidbody[2];
                for(int i=0;i<2;i++)
                {
                    cars[i]=UnityEngine.Object.Instantiate(manager.carPrefab);SceneManager.MoveGameObjectToScene(cars[i],scene);
                    foreach(var script in cars[i].GetComponentsInChildren<MonoBehaviour>())script.enabled=false;
                    bodies[i]=cars[i].GetComponent<Rigidbody>();bodies[i].isKinematic=false;bodies[i].useGravity=false;
                    bodies[i].constraints=RigidbodyConstraints.FreezeRotation;
                    bodies[i].position=trial==2?new Vector3(i==0?-8:8,10,0):new Vector3(0,10,i==0?-12:12);
                    bodies[i].rotation=Quaternion.identity;
                    bodies[i].linearVelocity=trial==2?new Vector3(i==0?65:-65,0,0):new Vector3(0,0,i==0?90:trial==0?-90:0);
                    if(cars[i].GetComponent<BoxCollider>()?.isTrigger!=false)throw new Exception("The production chassis must be solid.");
                }
                Physics.SyncTransforms();
                var first=cars[0].GetComponent<BoxCollider>();var second=cars[1].GetComponent<BoxCollider>();
                if(Physics.GetIgnoreLayerCollision(first.gameObject.layer,second.gameObject.layer)||Physics.GetIgnoreCollision(first,second))throw new Exception("Vehicle contact must be enabled.");
                for(int frame=0;frame<100;frame++)physics.Simulate(.02f);
                if(trial==2?bodies[0].position.x>bodies[1].position.x:bodies[0].position.z>bodies[1].position.z)throw new Exception("Production cars passed through one another at high speed.");
                if(trial==1&&bodies[1].linearVelocity.z<10)throw new Exception("Rear contact must transmit a physical impulse.");
                Debug.Log($"VEHICLE_CONTACT_TRIAL_PASS: trial={trial}, A={bodies[0].position}, B={bodies[1].position}, speedB={bodies[1].linearVelocity.magnitude}");
                foreach(var car in cars)UnityEngine.Object.DestroyImmediate(car);
            }
            Debug.Log("VEHICLE_CONTACT_PASS: actual prefab chassis / head-on, rear impact, side contact.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
