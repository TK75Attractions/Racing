using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Records the real floor shader and forward arrows without editing the photographs.</summary>
public static class TubePadEffectCapture
{
 public static void Run()
 {
  var course=Object.FindFirstObjectByType<RaceCourse>();var layout=course.GetComponent<TubeChargeLineLayout>();var pad=layout.Pads[0];
  var car=Object.Instantiate(Object.FindFirstObjectByType<Gmanager>().carPrefab,pad.transform.position-pad.transform.forward*3f+pad.transform.up*.65f,pad.transform.rotation);
  (car.GetComponent<PlayerCarPaint>()??car.AddComponent<PlayerCarPaint>()).SetPlayerIndex(0);car.GetComponent<Rigidbody>().isKinematic=true;
  var host=new GameObject("Pad effect capture camera");var camera=host.AddComponent<Camera>();camera.enabled=false;
  Vector3 eye=pad.transform.position-pad.transform.forward*12f+pad.transform.up*6f,target=pad.transform.position+pad.transform.forward*3f+pad.transform.up;
  camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));camera.fieldOfView=55f;camera.nearClipPlane=.1f;camera.farClipPlane=500;
  var output=new RenderTexture(960,540,24);Texture2D image=null;var active=RenderTexture.active;Color32[] first=null;
  string directory="Logs/TutorialRealPhotos/PadAnimation";Directory.CreateDirectory(directory);
  try
  {
   camera.targetTexture=output;
   for(int i=0;i<24;i++)
   {
    float time=i/8f;
    foreach(var arrow in layout.GetComponentsInChildren<ForwardBoostArrows>())arrow.SetPhase(time);
    foreach(var renderer in layout.GetComponentsInChildren<MeshRenderer>())if(renderer.sharedMaterial.HasProperty("_PreviewTime"))renderer.sharedMaterial.SetFloat("_PreviewTime",time);
    camera.Render();if(image==null)image=new Texture2D(960,540,TextureFormat.RGB24,false);RenderTexture.active=output;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();
    File.WriteAllBytes($"{directory}/frame-{i:000}.png",image.EncodeToPNG());
    if(i==0)first=image.GetPixels32();
    if(i==4)
    {var current=image.GetPixels32();int changed=0;for(int k=0;k<current.Length;k++)if(Math.Abs(current[k].r-first[k].r)+Math.Abs(current[k].g-first[k].g)+Math.Abs(current[k].b-first[k].b)>40)changed++;if(changed<current.Length/500)throw new InvalidOperationException("Boost arrows did not visibly animate");}
   }
   Debug.Log("TUBE_PAD_ANIMATION_CAPTURE_PASS: actual shader flow and forward arrow pixels change between phases.");
  }
  finally
  {
   foreach(var renderer in layout.GetComponentsInChildren<MeshRenderer>())if(renderer.sharedMaterial.HasProperty("_PreviewTime"))renderer.sharedMaterial.SetFloat("_PreviewTime",-1f);
   camera.targetTexture=null;RenderTexture.active=active;Object.DestroyImmediate(host);Object.DestroyImmediate(car);Object.DestroyImmediate(output);if(image!=null)Object.DestroyImmediate(image);
  }
 }
}
