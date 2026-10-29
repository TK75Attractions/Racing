using UnityEditor;
using UnityEngine;

/// <summary>Applies the shared surface recipe to the named materials used by every selectable car.</summary>
public static class CarMaterialTuningBuilder
{
    [MenuItem("Racing/Visuals/Build Car Material Tuning")]
    public static void Build()
    {
        string[] folders =
        {
            "Assets/SportCar/Models/SportCar_1/Materials",
            "Assets/SportCar/Models/Wheels",
            "Assets/RealisticMobileCars - Pro3DModels/RMCar26/Materials"
        };
        int changed = 0;
        foreach (string folder in folders)
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) continue;
            float metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
            float smoothness = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : 0f;
            Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
                material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            CarSelectionMaterialSet.ApplySurfaceTuning(material, material.name);
            if (material.HasProperty("_Metallic") && !Mathf.Approximately(metallic, material.GetFloat("_Metallic")) ||
                material.HasProperty("_Smoothness") && !Mathf.Approximately(smoothness, material.GetFloat("_Smoothness")) ||
                material.HasProperty("_BaseColor") && color != material.GetColor("_BaseColor") ||
                material.HasProperty("_Color") && color != material.GetColor("_Color"))
            { EditorUtility.SetDirty(material); changed++; }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Car material tuning complete: {changed} material assets updated.");
    }
}
