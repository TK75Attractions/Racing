using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RaceCourse))]
public sealed class RaceCourseEditor : Editor
{
    private readonly List<Vector3> center = new List<Vector3>();
    private readonly List<Vector3> left = new List<Vector3>();
    private readonly List<Vector3> right = new List<Vector3>();
    private readonly Vector3[] triangle = new Vector3[3];
    private SerializedProperty points;
    private bool drawing;
    private bool snapToSurface = true;
    private float planeHeight;
    private float defaultWidth = 10f;
    private float brushSpacing = 5f;
    private int selected = -1;
    private int strokeGroup = -1;
    private int drawingControl;
    private bool showPoints;

    [MenuItem("GameObject/Racing/Race Course", false, 10)]
    private static void CreateCourse(MenuCommand command)
    {
        GameObject course = new GameObject("Race Course", typeof(RaceCourse));
        GameObjectUtility.SetParentAndAlign(course, command.context as GameObject);
        Undo.RegisterCreatedObjectUndo(course, "Create race course");
        Selection.activeGameObject = course;
    }

    private void OnEnable()
    {
        points = serializedObject.FindProperty("waypoints");
        planeHeight = ((RaceCourse)target).transform.position.y;
        if (points.arraySize > 0)
            defaultWidth = points.GetArrayElementAtIndex(points.arraySize - 1).FindPropertyRelative("width").floatValue;
        Undo.undoRedoPerformed += OnUndo;
    }

    private void OnDisable()
    {
        EndStroke();
        Undo.undoRedoPerformed -= OnUndo;
    }

    private void OnUndo()
    {
        if (target == null) return;
        serializedObject.Update();
        ((RaceCourse)target).RebuildCache();
        ((RaceCourse)target).RebuildRoad();
        selected = Mathf.Min(selected, points.arraySize - 1);
        Repaint();
        SceneView.RepaintAll();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("中心線と幅から道路・中央の白い破線・両端の赤白模様・MeshColliderを自動生成します。位置はXYZで編集できます。", MessageType.Info);
        DrawPropertiesExcluding(serializedObject, "m_Script", "waypoints");
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sceneでコースを描く", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            bool nextDrawing = GUILayout.Toggle(drawing, drawing ? "描画を終了 (Esc)" : "描画を開始 / 末尾に追記", "Button");
            if (nextDrawing != drawing)
            {
                EndStroke();
                drawing = nextDrawing;
                SceneView.RepaintAll();
            }
            snapToSurface = EditorGUILayout.Toggle("Colliderの表面に配置", snapToSurface);
            planeHeight = EditorGUILayout.FloatField("描画平面の高さ (World Y)", planeHeight);
            defaultWidth = Mathf.Max(0.1f, EditorGUILayout.FloatField("新しい点の幅 (World)", defaultWidth));
            brushSpacing = Mathf.Max(0.1f, EditorGUILayout.FloatField("ドラッグの点間隔 (World)", brushSpacing));
            EditorGUILayout.HelpBox(drawing
                ? "左クリックで追加、左ドラッグで連続描画。Shift＋クリックで線の途中に挿入。Altで視点操作、Escで終了。Colliderがない場所は指定高さの平面に描画します。"
                : "点をクリックして選択し、XYZハンドルで移動。緑のハンドルで幅を変更。Shift＋クリックで途中に挿入。Deleteで選択点を削除。", MessageType.None);
            if (points.arraySize < 2)
                EditorGUILayout.HelpBox("2点以上でコースを生成します。周回コースには3点以上を配置してください。", MessageType.Warning);

            if (selected >= 0 && selected < points.arraySize)
            {
                EditorGUILayout.LabelField($"選択点 {selected}" + (selected == 0 ? " (開始点)" : ""), EditorStyles.boldLabel);
                DrawPoint(points.GetArrayElementAtIndex(selected));
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("次に点を追加"))
                    {
                        Vector3 world = WorldPosition(selected) + ((RaceCourse)target).transform.forward * brushSpacing;
                        InsertPoint(selected + 1, world);
                    }
                    if (GUILayout.Button("選択点を削除")) RemoveSelected();
                }
            }
            showPoints = EditorGUILayout.Foldout(showPoints, $"全ての点 ({points.arraySize})", true);
            if (showPoints)
            {
                for (int i = 0; i < points.arraySize; i++)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        if (GUILayout.Button($"点 {i}" + (i == 0 ? " / 開始点" : ""))) selected = i;
                        DrawPoint(points.GetArrayElementAtIndex(i));
                    }
                }
            }
        }
        ApplyChanges();
        if (GUILayout.Button("道路を再生成")) ((RaceCourse)target).RebuildRoad();
        EditorGUILayout.LabelField("中心線の全長", $"{((RaceCourse)target).TotalLength:F1} m");
    }

    private static Vector3 LocalPosition(SerializedProperty point)
    {
        Vector2 xz = point.FindPropertyRelative("position").vector2Value;
        return new Vector3(xz.x, point.FindPropertyRelative("height").floatValue, xz.y);
    }

    private static void SetLocalPosition(SerializedProperty point, Vector3 position)
    {
        point.FindPropertyRelative("position").vector2Value = new Vector2(position.x, position.z);
        point.FindPropertyRelative("height").floatValue = position.y;
    }

    private void DrawPoint(SerializedProperty point)
    {
        SetLocalPosition(point, EditorGUILayout.Vector3Field("位置 (Local XYZ)", LocalPosition(point)));
        SerializedProperty width = point.FindPropertyRelative("width");
        width.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField("幅 (World)", width.floatValue));
        EditorGUILayout.PropertyField(point.FindPropertyRelative("curve"), new GUIContent("次の点への曲がり"));
    }

    private Vector3 WorldPosition(int index) => ((RaceCourse)target).transform.TransformPoint(LocalPosition(points.GetArrayElementAtIndex(index)));

    private void ApplyChanges()
    {
        if (!serializedObject.ApplyModifiedProperties()) return;
        ((RaceCourse)target).RebuildCache();
        ((RaceCourse)target).RebuildRoad();
        SceneView.RepaintAll();
    }

    private void InsertPoint(int index, Vector3 world)
    {
        points.InsertArrayElementAtIndex(index);
        SerializedProperty point = points.GetArrayElementAtIndex(index);
        SetLocalPosition(point, ((RaceCourse)target).transform.InverseTransformPoint(world));
        point.FindPropertyRelative("curve").floatValue = 0f;
        point.FindPropertyRelative("width").floatValue = defaultWidth;
        selected = index;
        ApplyChanges();
        Repaint();
    }

    private void RemoveSelected()
    {
        if (selected < 0 || selected >= points.arraySize) return;
        points.DeleteArrayElementAtIndex(selected);
        selected = Mathf.Min(selected, points.arraySize - 1);
        ApplyChanges();
        Repaint();
    }

    private void OnSceneGUI()
    {
        RaceCourse course = (RaceCourse)target;
        serializedObject.Update();
        course.CopyCenterPathWorld(center);
        course.CopyCourseBandWorld(left, right);
        DrawBand();
        if (Application.isPlaying) return;
        Event evt = Event.current;
        drawingControl = GUIUtility.GetControlID("RaceCourseDrawing".GetHashCode(), FocusType.Passive);
        if (drawing || evt.shift)
        {
            if (evt.type == EventType.Layout) HandleUtility.AddDefaultControl(drawingControl);
            HandleDrawing(evt);
        }
        else if (strokeGroup >= 0) HandleDrawing(evt);

        if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
        {
            drawing = false;
            EndStroke();
            evt.Use();
            Repaint();
        }
        if (!drawing && evt.type == EventType.KeyDown &&
            (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) && selected >= 0)
        {
            RemoveSelected();
            evt.Use();
        }
        if (drawing || evt.shift) return;
        for (int i = 0; i < points.arraySize; i++)
        {
            Vector3 world = WorldPosition(i);
            float size = HandleUtility.GetHandleSize(world) * 0.075f;
            Handles.color = selected == i ? Color.white : Color.cyan;
            if (Handles.Button(world, Quaternion.identity, size, size, Handles.SphereHandleCap))
            {
                selected = i;
                Repaint();
            }
            Handles.Label(world + Vector3.up * size * 2f, i == 0 ? "0 / START" : i.ToString());
        }
        if (selected < 0 || selected >= points.arraySize) return;
        SerializedProperty selectedPoint = points.GetArrayElementAtIndex(selected);
        Vector3 position = WorldPosition(selected);
        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(position, course.transform.rotation);
        if (EditorGUI.EndChangeCheck())
        {
            SetLocalPosition(selectedPoint, course.transform.InverseTransformPoint(moved));
            ApplyChanges();
        }
        course.TryGetNearestCenterLineDirection(position, out Vector3 direction);
        Vector3 lateral = Vector3.Cross(Vector3.up, direction).normalized;
        if (lateral.sqrMagnitude < 0.001f) lateral = Vector3.right;
        SerializedProperty widthProperty = selectedPoint.FindPropertyRelative("width");
        Vector3 edge = moved + lateral * (widthProperty.floatValue * 0.5f);
        Handles.color = Color.green;
        Handles.DrawLine(moved, edge);
        EditorGUI.BeginChangeCheck();
        Vector3 newEdge = Handles.Slider(edge, lateral, HandleUtility.GetHandleSize(edge) * 0.1f, Handles.CubeHandleCap, 0f);
        if (EditorGUI.EndChangeCheck())
        {
            widthProperty.floatValue = Mathf.Max(0f, Vector3.Dot(newEdge - moved, lateral) * 2f);
            ApplyChanges();
        }
    }

    private void DrawBand()
    {
        if (Event.current.type != EventType.Repaint || center.Count < 2) return;
        Handles.color = new Color(0.1f, 0.8f, 1f, 0.1f);
        for (int i = 1; i < left.Count; i++)
        {
            triangle[0] = left[i - 1]; triangle[1] = right[i - 1]; triangle[2] = left[i];
            Handles.DrawAAConvexPolygon(triangle);
            triangle[0] = right[i - 1]; triangle[1] = right[i]; triangle[2] = left[i];
            Handles.DrawAAConvexPolygon(triangle);
        }
        Handles.color = Color.cyan;
        Handles.DrawAAPolyLine(3f, center.ToArray());
        Handles.color = Color.yellow;
        Handles.DrawAAPolyLine(2f, left.ToArray());
        Handles.DrawAAPolyLine(2f, right.ToArray());
    }

    private bool TryDrawPosition(Vector2 mouse, out Vector3 position)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
        if (snapToSurface && Physics.Raycast(ray, out RaycastHit hit, 100000f, ~0, QueryTriggerInteraction.Ignore))
        {
            position = hit.point;
            return true;
        }
        Plane plane = new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f));
        if (plane.Raycast(ray, out float distance))
        {
            position = ray.GetPoint(distance);
            return true;
        }
        position = default;
        return false;
    }

    private void HandleDrawing(Event evt)
    {
        if (evt.type == EventType.MouseUp && GUIUtility.hotControl == drawingControl)
        {
            EndStroke();
            evt.Use();
            return;
        }
        if (evt.alt || evt.button != 0) return;
        if (evt.type == EventType.MouseDown && HandleUtility.nearestControl == drawingControl &&
            TryDrawPosition(evt.mousePosition, out Vector3 world))
        {
            Undo.IncrementCurrentGroup();
            strokeGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Draw race course");
            GUIUtility.hotControl = drawingControl;
            if (evt.shift && center.Count > 1) InsertPoint(FindInsertionIndex(world), world);
            else if (drawing) InsertPoint(points.arraySize, world);
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && drawing && !evt.shift && GUIUtility.hotControl == drawingControl &&
            TryDrawPosition(evt.mousePosition, out Vector3 dragged))
        {
            if (points.arraySize == 0 || Vector3.Distance(WorldPosition(points.arraySize - 1), dragged) >= brushSpacing)
                InsertPoint(points.arraySize, dragged);
            evt.Use();
        }
        if ((evt.type == EventType.MouseMove || evt.type == EventType.Repaint) && drawing &&
            TryDrawPosition(evt.mousePosition, out Vector3 preview))
        {
            Handles.color = Color.white;
            Handles.DrawWireDisc(preview, Vector3.up, defaultWidth * 0.5f);
            if (points.arraySize > 0) Handles.DrawDottedLine(WorldPosition(points.arraySize - 1), preview, 4f);
            if (evt.type == EventType.MouseMove) SceneView.currentDrawingSceneView?.Repaint();
        }
    }

    private int FindInsertionIndex(Vector3 world)
    {
        return ((RaceCourse)target).GetWaypointInsertionIndexWorld(world);
    }

    private void EndStroke()
    {
        if (strokeGroup < 0) return;
        if (GUIUtility.hotControl == drawingControl) GUIUtility.hotControl = 0;
        Undo.CollapseUndoOperations(strokeGroup);
        strokeGroup = -1;
    }
}
