using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Click to place course-light prefabs directly in the Scene view.</summary>
public sealed class CourseLightPlacementWindow : EditorWindow
{
    private const string CatalogPath = "Assets/Settings/CourseLightCatalog.asset";
    private const string AndonPrefabPath = "Assets/Prefab/Course/NeonAndonLight.prefab";
    private const string DirectionPropsFolder = "Assets/Prefab/Course/DirectionProps/";

    [SerializeField] private CourseLightPlacementCatalog catalog;
    [SerializeField] private int selectedIndex;
    [SerializeField] private bool placing;
    [SerializeField] private bool alignToSurface;
    [SerializeField] private float yaw;
    [SerializeField] private float fallbackPlaneHeight;
    [SerializeField] private Transform parent;

    private bool hasCursorPoint;
    private Vector3 cursorPoint;
    private Vector3 cursorNormal = Vector3.up;

    [MenuItem("Window/Racing/光源オブジェクト配置")]
    [MenuItem("Window/Racing/コースオブジェクト配置")]
    [MenuItem("Racing/Light Placement/Open Placement Tool")]
    [MenuItem("Racing/Placement/Open Course Placement Tool")]
    public static void Open()
    {
        var window = GetWindow<CourseLightPlacementWindow>();
        window.titleContent = new GUIContent("コース配置");
        window.minSize = new Vector2(300f, 300f);
        window.Show();
    }

    [MenuItem("Racing/Light Placement/Start Placement Mode")]
    [MenuItem("Racing/Placement/Start Placement Mode")]
    public static void StartPlacementModeFromMenu()
    {
        var window = GetWindow<CourseLightPlacementWindow>();
        window.titleContent = new GUIContent("コース配置");
        if (window.catalog == null)
        {
            window.catalog = AssetDatabase.LoadAssetAtPath<CourseLightPlacementCatalog>(CatalogPath);
            if (window.catalog == null)
            {
                CreateOrUpdateDefaultCatalog();
                window.catalog = AssetDatabase.LoadAssetAtPath<CourseLightPlacementCatalog>(CatalogPath);
            }
        }

        if (window.catalog != null && window.catalog.Entries.Count > 0)
        {
            window.selectedIndex = Mathf.Clamp(window.selectedIndex, 0, window.catalog.Entries.Count - 1);
            var entry = window.catalog.Entries[window.selectedIndex];
            if (entry != null && entry.Prefab != null && PrefabUtility.IsPartOfPrefabAsset(entry.Prefab))
            {
                window.placing = true;
                window.Show();
                window.Repaint();
                SceneView.RepaintAll();
                return;
            }
        }

        window.placing = false;
        window.Show();
        window.ShowNotification(new GUIContent("カタログに有効なPrefabを登録してください"));
        window.Repaint();
    }

    [MenuItem("Racing/Light Placement/Create or Update Default Catalog")]
    [MenuItem("Racing/Placement/Add Standard Course Prefabs to Catalog")]
    public static void CreateOrUpdateDefaultCatalog()
    {
        var asset = AssetDatabase.LoadAssetAtPath<CourseLightPlacementCatalog>(CatalogPath);
        if (asset == null)
        {
            asset = CreateInstance<CourseLightPlacementCatalog>();
            AssetDatabase.CreateAsset(asset, CatalogPath);
        }

        bool changed = AddPrefabIfAvailable(asset, AndonPrefabPath, "ネオン行燈");
        changed |= AddPrefabIfAvailable(asset, DirectionPropsFolder + "ArrowGuardrail.prefab", "矢印ガードレール");
        changed |= AddPrefabIfAvailable(asset, DirectionPropsFolder + "ArrowSign.prefab", "矢印案内看板");
        changed |= AddPrefabIfAvailable(asset, DirectionPropsFolder + "NoEntryPole.prefab", "黄黒の進入禁止ポール");
        if (changed)
            EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Selection.activeObject = asset;
        Debug.Log("Course placement catalog is ready: " + CatalogPath);
    }

    private static bool AddPrefabIfAvailable(CourseLightPlacementCatalog asset, string path, string displayName)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning("Course placement prefab was not found: " + path);
            return false;
        }
        return asset.AddIfMissing(prefab, displayName);
    }

    private void OnEnable()
    {
        if (catalog == null)
            catalog = AssetDatabase.LoadAssetAtPath<CourseLightPlacementCatalog>(CatalogPath);
        SceneView.duringSceneGui += DuringSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DuringSceneGUI;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("コースオブジェクト配置", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "光源や交通誘導などのPrefabを選び、配置モード中にSceneビューをクリックすると設置します。",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            catalog = (CourseLightPlacementCatalog)EditorGUILayout.ObjectField(
                "配置カタログ", catalog, typeof(CourseLightPlacementCatalog), false);
            if (GUILayout.Button("選択", GUILayout.Width(44f)) && catalog != null)
                Selection.activeObject = catalog;
        }

        if (catalog == null)
        {
            EditorGUILayout.HelpBox("配置カタログがありません。下のボタンで標準Prefabを登録したカタログを作成してください。", MessageType.Warning);
            if (GUILayout.Button("標準Prefab入りのカタログを作成"))
                CreateOrUpdateCatalogForWindow();
            return;
        }

        var entries = catalog.Entries;
        if (entries == null || entries.Count == 0)
        {
            EditorGUILayout.HelpBox("カタログが空です。標準Prefabを追加するか、Inspectorで配置Prefabを登録してください。", MessageType.Warning);
            if (GUILayout.Button("標準Prefabをカタログに追加"))
                CreateOrUpdateCatalogForWindow();
            return;
        }

        string[] labels = new string[entries.Count];
        for (int i = 0; i < entries.Count; i++)
            labels[i] = entries[i] == null ? "未設定" : entries[i].DisplayName;
        selectedIndex = Mathf.Clamp(selectedIndex, 0, entries.Count - 1);
        selectedIndex = EditorGUILayout.Popup("配置するオブジェクト", selectedIndex, labels);

        var selected = entries[selectedIndex];
        GameObject prefab = selected != null ? selected.Prefab : null;
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Prefab", prefab, typeof(GameObject), false);

        EditorGUILayout.Space(4f);
        yaw = EditorGUILayout.FloatField("Y回転", yaw);
        alignToSurface = EditorGUILayout.Toggle("面の向きに合わせる", alignToSurface);
        fallbackPlaneHeight = EditorGUILayout.FloatField("地面がない場合の高さ", fallbackPlaneHeight);
        parent = (Transform)EditorGUILayout.ObjectField("配置先（任意）", parent, typeof(Transform), true);

        EditorGUILayout.Space(8f);
        bool validPrefab = prefab != null && PrefabUtility.IsPartOfPrefabAsset(prefab);
        if (!validPrefab)
            EditorGUILayout.HelpBox("この項目にはProject内のPrefabを登録してください。", MessageType.Warning);

        using (new EditorGUI.DisabledScope(!validPrefab))
        {
            string buttonLabel = placing ? "配置モードを終了" : "配置モードを開始";
            if (GUILayout.Button(buttonLabel, GUILayout.Height(32f)))
            {
                placing = !placing;
                hasCursorPoint = false;
                SceneView.RepaintAll();
            }
        }

        if (placing)
            EditorGUILayout.HelpBox("Sceneビューをクリック: 配置　/　Esc: 終了　/　Y回転は上の欄で指定", MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("カタログをInspectorで編集"))
                Selection.activeObject = catalog;
            if (GUILayout.Button("標準Prefabを追加"))
                CreateOrUpdateCatalogForWindow();
        }

        EditorGUILayout.HelpBox(
            "新しい光源・標識などを増やすときはPrefabを作成し、配置カタログのEntriesに表示名とPrefabを追加してください。配置ツール本体の変更は不要です。PrefabのルートPivotを接地位置に合わせると扱いやすくなります。",
            MessageType.None);
    }

    private void CreateOrUpdateCatalogForWindow()
    {
        CreateOrUpdateDefaultCatalog();
        catalog = AssetDatabase.LoadAssetAtPath<CourseLightPlacementCatalog>(CatalogPath);
        Repaint();
    }

    private void DuringSceneGUI(SceneView sceneView)
    {
        if (!placing || catalog == null || catalog.Entries.Count == 0)
            return;

        int index = Mathf.Clamp(selectedIndex, 0, catalog.Entries.Count - 1);
        var entry = catalog.Entries[index];
        if (entry == null || entry.Prefab == null)
            return;

        Event current = Event.current;
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
        {
            placing = false;
            hasCursorPoint = false;
            Repaint();
            current.Use();
            SceneView.RepaintAll();
            return;
        }

        if (current.alt || current.type == EventType.MouseDown && current.button != 0)
            return;

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        if (current.type == EventType.Layout || current.type == EventType.MouseMove || current.type == EventType.MouseDown)
            HandleUtility.AddDefaultControl(controlId);

        hasCursorPoint = TryGetPlacementPoint(HandleUtility.GUIPointToWorldRay(current.mousePosition), out cursorPoint, out cursorNormal);

        if (current.type == EventType.MouseMove)
        {
            sceneView.Repaint();
            Repaint();
        }
        else if (current.type == EventType.MouseDown && current.button == 0 && hasCursorPoint)
        {
            Place(entry, cursorPoint, cursorNormal);
            current.Use();
        }

        if (hasCursorPoint)
        {
            float radius = HandleUtility.GetHandleSize(cursorPoint) * 0.18f;
            Handles.color = new Color(0.2f, 0.9f, 1f, 0.95f);
            Handles.DrawWireDisc(cursorPoint, alignToSurface ? cursorNormal : Vector3.up, radius);
            Handles.DrawLine(cursorPoint, cursorPoint + (alignToSurface ? cursorNormal : Vector3.up) * radius * 2f);
        }

        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(12f, 12f, 290f, 54f), GUI.skin.box);
        GUILayout.Label("配置中: " + entry.DisplayName, EditorStyles.boldLabel);
        GUILayout.Label("クリックで配置 / Escで終了");
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    private bool TryGetPlacementPoint(Ray ray, out Vector3 point, out Vector3 normal)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, 10000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            normal = hit.normal;
            return true;
        }

        var plane = new Plane(Vector3.up, new Vector3(0f, fallbackPlaneHeight, 0f));
        if (plane.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            normal = Vector3.up;
            return true;
        }

        point = Vector3.zero;
        normal = Vector3.up;
        return false;
    }

    private void Place(CourseLightPlacementCatalog.Entry entry, Vector3 point, Vector3 normal)
    {
        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
        {
            ShowNotification(new GUIContent("Prefab ModeではなくSceneを開いてください"));
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (parent != null && parent.gameObject.scene != scene)
        {
            ShowNotification(new GUIContent("配置先は現在のSceneから選んでください"));
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.Prefab, scene);
        if (instance == null)
        {
            Debug.LogError("光源Prefabを配置できませんでした: " + entry.Prefab.name);
            return;
        }

        instance.name = entry.Prefab.name;
        instance.transform.SetPositionAndRotation(point, GetPlacementRotation(normal));
        if (parent != null)
            instance.transform.SetParent(parent, true);
        Undo.RegisterCreatedObjectUndo(instance, "Place " + entry.DisplayName);
        Selection.activeGameObject = instance;
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private Quaternion GetPlacementRotation(Vector3 normal)
    {
        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        return alignToSurface ? Quaternion.FromToRotation(Vector3.up, normal) * yawRotation : yawRotation;
    }
}
