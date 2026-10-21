using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class CampusAtmosphereValidation
{
    public static void Audit()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var output = new System.Text.StringBuilder();
        var campus = GameObject.Find("1016こうしゃ");
        foreach (var renderer in campus.GetComponentsInChildren<MeshRenderer>())
        {
            var b = renderer.bounds; var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            output.Append(renderer.name + " center=" + b.center + " size=" + b.size + " verts=" + (mesh?.vertexCount ?? 0) + " mats=");
            foreach (var mat in renderer.sharedMaterials)
                if (mat != null) output.Append(mat.name + ":" + mat.shader.name + ":" + mat.color + ";");
            output.AppendLine();
        }
        foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if(light.type==LightType.Directional) output.AppendLine("LIGHT " + light.name + " rot=" + light.transform.eulerAngles + " intensity=" + light.intensity);
        var pipe = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        output.AppendLine("PIPE " + pipe.name + " shadowDistance=" + pipe.shadowDistance + " cascades=" + pipe.shadowCascadeCount);
        Directory.CreateDirectory("Logs/CampusAtmosphere");
        File.WriteAllText("Logs/CampusAtmosphere/scene-audit.txt", output.ToString());
        Debug.Log("CAMPUS_ATMOSPHERE_AUDIT_PASS");
    }
}
