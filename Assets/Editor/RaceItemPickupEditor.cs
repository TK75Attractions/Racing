using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RaceItemPickup), true)]
[CanEditMultipleObjects]
public sealed class RaceItemPickupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("地面へ配置"))
        {
            foreach (Object selected in targets)
            {
                RaceItemPickup pickup = (RaceItemPickup)selected;
                Undo.RecordObject(pickup.transform, "Place item pickup on ground");
                if (pickup.PlaceOnGround()) EditorUtility.SetDirty(pickup.transform);
                else Debug.LogWarning("アイテムの下に地面を検出できませんでした。", pickup);
            }
        }

        if (GUILayout.Button("表示を再生成"))
        {
            foreach (Object selected in targets) ((RaceItemPickup)selected).Refresh();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("原点を路面に置き、表示と判定は Hover Height の高さに浮かびます。黄色の球が取得判定です。", MessageType.Info);
    }

    [MenuItem("Racing/Items/Create Charge Crystal")]
    private static void CreateChargeCrystal() => Create<ChargeCrystal>("Charge Crystal");

    [MenuItem("Racing/Items/Create Item Box")]
    private static void CreateItemBox() => Create<ItemBox>("Item Box");

    [MenuItem("Racing/Items/Create Item Box Row (3)")]
    private static void CreateItemBoxRow()
    {
        // コース幅に並べる定番の配置です。親を動かすとまとめて移動できます。
        GameObject row = new GameObject("Item Box Row");
        Undo.RegisterCreatedObjectUndo(row, "Create item box row");
        PlaceInFrontOfSceneCamera(row.transform);
        for (int i = -1; i <= 1; i++)
        {
            GameObject box = new GameObject($"Item Box {i + 2}");
            Undo.RegisterCreatedObjectUndo(box, "Create item box row");
            box.transform.SetParent(row.transform, false);
            box.transform.localPosition = Vector3.right * i * 3.5f;
            box.AddComponent<ItemBox>().PlaceOnGround();
        }
        Selection.activeGameObject = row;
    }

    private static void Create<T>(string name) where T : RaceItemPickup
    {
        GameObject pickupObject = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(pickupObject, $"Create {name}");
        PlaceInFrontOfSceneCamera(pickupObject.transform);
        T pickup = pickupObject.AddComponent<T>();
        pickup.PlaceOnGround();
        Selection.activeGameObject = pickupObject;
        EditorGUIUtility.PingObject(pickupObject);
    }

    private static void PlaceInFrontOfSceneCamera(Transform target)
    {
        if (SceneView.lastActiveSceneView == null) return;
        Transform cameraTransform = SceneView.lastActiveSceneView.camera.transform;
        target.position = cameraTransform.position + cameraTransform.forward * 10f;
    }
}
