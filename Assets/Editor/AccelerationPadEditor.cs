using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AccelerationPad))]
public sealed class AccelerationPadEditor : Editor
{
    private SerializedProperty padSize;

    private void OnEnable()
    {
        padSize = serializedObject.FindProperty("padSize");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "padSize");
        EditorGUILayout.PropertyField(padSize, new GUIContent("盤面サイズ"));
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("地面へ配置"))
        {
            AccelerationPad pad = (AccelerationPad)target;
            Undo.RecordObject(pad.transform, "Place acceleration pad on ground");
            if (pad.PlaceOnGround())
            {
                EditorUtility.SetDirty(pad);
                EditorUtility.SetDirty(pad.transform);
            }
            else Debug.LogWarning("加速度盤の下に地面を検出できませんでした。", pad);
        }

        if (GUILayout.Button("表示を再生成"))
        {
            ((AccelerationPad)target).Refresh();
            EditorUtility.SetDirty(target);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("Sceneビューで移動・回転・盤面サイズを直接調整できます。盤面の青い矢印が加速方向です。", MessageType.Info);
    }

    private void OnSceneGUI()
    {
        AccelerationPad pad = (AccelerationPad)target;
        serializedObject.Update();

        Handles.color = new Color(0.1f, 0.8f, 1f, 0.9f);
        Vector3 center = pad.transform.TransformPoint(Vector3.up * padSize.vector3Value.y * 0.5f);
        Vector3 moved = Handles.PositionHandle(pad.transform.position, pad.transform.rotation);
        if (moved != pad.transform.position)
        {
            Undo.RecordObject(pad.transform, "Move acceleration pad");
            pad.transform.position = moved;
            EditorUtility.SetDirty(pad.transform);
        }

        Quaternion rotated = Handles.RotationHandle(pad.transform.rotation, pad.transform.position);
        if (rotated != pad.transform.rotation)
        {
            Undo.RecordObject(pad.transform, "Rotate acceleration pad");
            pad.transform.rotation = rotated;
            EditorUtility.SetDirty(pad.transform);
        }

        Vector3 size = padSize.vector3Value;
        Vector3 scaled = Handles.ScaleHandle(size, center, pad.transform.rotation,
            HandleUtility.GetHandleSize(center));
        scaled.x = Mathf.Max(0.1f, scaled.x);
        scaled.y = Mathf.Max(0.02f, scaled.y);
        scaled.z = Mathf.Max(0.1f, scaled.z);
        if ((scaled - size).sqrMagnitude > 0.000001f)
        {
            Undo.RecordObject(pad, "Resize acceleration pad");
            padSize.vector3Value = scaled;
            serializedObject.ApplyModifiedProperties();
            pad.Refresh();
            EditorUtility.SetDirty(pad);
        }

        Handles.color = new Color(0.1f, 0.8f, 1f, 0.65f);
        Handles.ArrowHandleCap(0, pad.transform.position + pad.transform.up * (size.y + 0.05f),
            pad.transform.rotation, Mathf.Min(2f, size.z * 0.3f), EventType.Repaint);
        serializedObject.ApplyModifiedProperties();
    }

    [MenuItem("Racing/Acceleration Pad/Create Acceleration Pad")]
    private static void CreateAccelerationPad()
    {
        GameObject padObject = new GameObject("Acceleration Pad");
        Undo.RegisterCreatedObjectUndo(padObject, "Create acceleration pad");
        AccelerationPad pad = padObject.AddComponent<AccelerationPad>();
        if (SceneView.lastActiveSceneView != null)
        {
            Transform cameraTransform = SceneView.lastActiveSceneView.camera.transform;
            padObject.transform.position = cameraTransform.position + cameraTransform.forward * 10f;
        }
        Selection.activeGameObject = padObject;
        EditorGUIUtility.PingObject(padObject);
    }
}
