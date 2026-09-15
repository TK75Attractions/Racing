using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RaceBarrierPath))]
public sealed class RaceBarrierPathEditor : Editor
{
    private SerializedProperty controlPoints;
    private SerializedProperty closedLoop;

    private readonly List<Vector3> previewPath = new List<Vector3>();

    private void OnEnable()
    {
        controlPoints = serializedObject.FindProperty("controlPoints");
        closedLoop = serializedObject.FindProperty("closedLoop");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "controlPoints");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("制御点", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(controlPoints, true);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("制御点を追加"))
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(target, "Add barrier control point");
            RaceBarrierPath path = (RaceBarrierPath)target;
            path.AddControlPoint();
            path.Rebuild();
            EditorUtility.SetDirty(target);
        }

        if (GUILayout.Button("最後の制御点を削除"))
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(target, "Remove barrier control point");
            RaceBarrierPath path = (RaceBarrierPath)target;
            path.RemoveLastControlPoint();
            path.Rebuild();
            EditorUtility.SetDirty(target);
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("壁を再生成"))
        {
            ((RaceBarrierPath)target).Rebuild();
            EditorUtility.SetDirty(target);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void OnSceneGUI()
    {
        RaceBarrierPath path = (RaceBarrierPath)target;
        path.CopyPreviewPathWorld(previewPath);

        if (previewPath.Count > 1)
        {
            Handles.color = new Color(0.15f, 0.9f, 1f, 0.95f);
            Handles.DrawAAPolyLine(3f, previewPath.ToArray());
        }

        SerializedObject serializedPath = new SerializedObject(path);
        SerializedProperty points = serializedPath.FindProperty("controlPoints");
        serializedPath.Update();

        for (int i = 0; i < points.arraySize; i++)
        {
            SerializedProperty point = points.GetArrayElementAtIndex(i);
            SerializedProperty positionProperty = point.FindPropertyRelative("localPosition");
            Vector3 worldPosition = path.transform.TransformPoint(positionProperty.vector3Value);

            Handles.color = Color.cyan;
            Handles.Label(worldPosition + Vector3.up * 0.35f, $"Barrier {i}");
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(worldPosition, path.transform.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(path, "Move barrier control point");
                positionProperty.vector3Value = path.transform.InverseTransformPoint(moved);
                serializedPath.ApplyModifiedProperties();
                path.Rebuild();
                EditorUtility.SetDirty(path);
            }
        }

        serializedPath.ApplyModifiedProperties();

        if (closedLoop != null && closedLoop.boolValue && previewPath.Count > 1)
        {
            Handles.color = new Color(0.15f, 0.9f, 1f, 0.5f);
            Handles.DrawDottedLine(previewPath[previewPath.Count - 1], previewPath[0], 4f);
        }
    }
}
