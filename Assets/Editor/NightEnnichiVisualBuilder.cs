using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Applies persistent night materials and scene settings without renderer overrides.</summary>
public static class NightEnnichiVisualBuilder
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string ModelPath = "Assets/Scenes/1016こうしゃ.fbx";
    private const string TubePath = "Assets/Scenes/TubeCourseData/TubeInterior.mat";
    private const string Folder = "Assets/Art/NightEnnichi";
    private static readonly string[][] TreePairs =
    {
        new[] { "Material.014", "Material.015" }, new[] { "Material.016", "Material.017" },
        new[] { "Material.025", "Material.026" }, new[] { "Material.004", "Material.005" }
    };
    private static readonly string[] GlassNames =
        { "ガラス", "ガラス.001", "ガラス.002", "ガラス.003", "Material.018", "Material.030" };

    [MenuItem("Racing/Visual/Apply Night Ennichi Look")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run in edit mode.");
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Material tube = AssetDatabase.LoadAssetAtPath<Material>(TubePath);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        Require(importer != null && model != null, "School FBX / ModelImporter is missing.");
        Require(tube != null && shader != null, "TubeInterior or URP/Lit shader is missing.");

        // Embedded names disappear from LoadAllAssetsAtPath after remapping. Keep the
        // importer identifiers as the source of truth on subsequent runs.
        var sources = new Dictionary<string, AssetImporter.SourceAssetIdentifier>(StringComparer.Ordinal);
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            if (asset is Material material)
                sources[material.name] = new AssetImporter.SourceAssetIdentifier(material);
        var external = importer.GetExternalObjectMap();
        foreach (var entry in external)
            if (entry.Key.type == typeof(Material)) sources[entry.Key.name] = entry.Key;
        foreach (string[] pair in TreePairs)
            foreach (string name in pair) Require(sources.ContainsKey(name), "Missing tree material: " + name);
        foreach (string name in GlassNames) Require(sources.ContainsKey(name), "Missing glass material: " + name);
        Require(sources.ContainsKey("地下チューブ"), "Missing FBX material: 地下チューブ");

        var leavesBySource = new Dictionary<string, bool>(StringComparer.Ordinal);
        int treeCount = 0;
        foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (!renderer.name.StartsWith("tree", StringComparison.OrdinalIgnoreCase) &&
                (mesh == null || !mesh.name.StartsWith("tree", StringComparison.OrdinalIgnoreCase))) continue;
            Require(mesh != null && mesh.subMeshCount == 2 && renderer.sharedMaterials.Length == 2,
                "Expected two tree submeshes/materials: " + renderer.name);
            string first = SourceName(renderer.sharedMaterials[0], external);
            string second = SourceName(renderer.sharedMaterials[1], external);
            bool knownPair = false;
            foreach (string[] pair in TreePairs)
                if ((first == pair[0] && second == pair[1]) || (first == pair[1] && second == pair[0]))
                    knownPair = true;
            Require(knownPair, "Unexpected tree material pair: " + renderer.name);
            Require(mesh.GetTopology(0) == MeshTopology.Triangles && mesh.GetTopology(1) == MeshTopology.Triangles,
                "Tree submeshes must contain triangles: " + renderer.name);
            // GetLod reads index ranges without requiring a readable mesh or changing LODs.
            var firstIndices = mesh.GetLod(0, 0).indexCount;
            var secondIndices = mesh.GetLod(1, 0).indexCount;
            Require(firstIndices > 0 && secondIndices > 0 && firstIndices != secondIndices,
                "Cannot distinguish tree leaves/trunk by triangle count: " + renderer.name);
            RecordRole(leavesBySource, first, firstIndices > secondIndices);
            RecordRole(leavesBySource, second, secondIndices > firstIndices);
            treeCount++;
        }
        Require(treeCount == 15 && leavesBySource.Count == 8, "Expected 15 trees using all four material pairs.");

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        Require(openedHere || !scene.isDirty, "Save SampleScene changes before running this builder.");
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            // Check scene prerequisites before changing any assets.
            VManager manager = UniqueComponent<VManager>(scene);
            RaceEnvironmentController environment = UniqueComponent<RaceEnvironmentController>(scene);
            Require(environment.name == "RaceEnvironment", "Expected the RaceEnvironment GameObject.");
            var managerSettings = new SerializedObject(manager);
            Property(managerSettings, "speedMotionBlurLow");
            Property(managerSettings, "speedMotionBlurHigh");
            Property(managerSettings, "boostMotionBlur");
            var environmentSettings = new SerializedObject(environment);
            Property(environmentSettings, "timeOfDay");
            Property(environmentSettings, "weather");
            RaceCourse course = UniqueComponent<RaceCourse>(scene);
            Require(course.HasValidPath, "RaceCourse has no valid center line.");

            EnsureFolder("Assets/Art");
            EnsureFolder(Folder);
            foreach (string[] pair in TreePairs)
            foreach (string name in pair)
            {
                bool leaves = leavesBySource[name];
                // Separate stable assets preserve each source identity even after remapping.
                string path = Folder + "/Tree_" + name.Replace('.', '_') + ".mat";
                Material material = GetOrCreateMaterial(path, shader);
                Configure(material, shader,
                    leaves ? new Color(1f, .62f, .78f) : new Color(.22f, .14f, .10f),
                    leaves ? .25f : .15f, leaves ? new Color(.35f, .10f, .20f) : Color.black);
                importer.AddRemap(sources[name], material);
            }
            Configure(tube, shader, new Color(.05f, .06f, .11f), .55f, Color.black);
            importer.AddRemap(sources["地下チューブ"], tube);
            Material windows = GetOrCreateMaterial(Folder + "/WarmWindows.mat", shader);
            Configure(windows, shader, new Color(.95f, .80f, .55f), .35f, new Color(1.6f, 1.1f, .6f));
            foreach (string name in GlassNames) importer.AddRemap(sources[name], windows);
            AssetDatabase.SaveAssets();
            importer.SaveAndReimport();

            // Reacquire scene objects after reimport; never assign renderer materials here.
            manager = UniqueComponent<VManager>(scene);
            managerSettings = new SerializedObject(manager);
            Property(managerSettings, "speedMotionBlurLow").floatValue = 0f;
            Property(managerSettings, "speedMotionBlurHigh").floatValue = 0f;
            Property(managerSettings, "boostMotionBlur").floatValue = 0f;
            managerSettings.ApplyModifiedPropertiesWithoutUndo();
            RecordPrefabChanges(manager);
            environment = UniqueComponent<RaceEnvironmentController>(scene);
            environmentSettings = new SerializedObject(environment);
            Property(environmentSettings, "timeOfDay").intValue = (int)RaceEnvironmentController.TimeOfDay.Night;
            Property(environmentSettings, "weather").intValue = (int)RaceEnvironmentController.Weather.Sunny;
            environmentSettings.ApplyModifiedPropertiesWithoutUndo();
            RecordPrefabChanges(environment);
            BuildLanterns(scene, course);
            // The controller applies lighting/sky at runtime; do not persist its temporary sky material.
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Failed to save SampleScene.");
            AssetDatabase.SaveAssets();

            Debug.Log("[NightEnnichi] Remapped 15 trees / 8 source materials to pink emissive leaves and dark trunks by LOD0 triangle counts; LOD settings unchanged.");
            Debug.Log("[NightEnnichi] Updated TubeInterior to dark navy (smoothness 0.55) and remapped 地下チューブ to the same material.");
            Debug.Log("[NightEnnichi] Saved speedMotionBlurLow / speedMotionBlurHigh / boostMotionBlur = 0; other boost effects unchanged.");
            Debug.Log("[NightEnnichi] Saved RaceEnvironment: Night (1), Sunny (0); lighting applies on play.");
            Debug.Log("[NightEnnichi] Remapped all 6 glass sources to opaque warm emissive windows; saved assets and SampleScene.");
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private struct LanternExclusion
    {
        public float progress, radius;
    }

    private static void BuildLanterns(Scene scene, RaceCourse course)
    {
        // Only rebuild this owned subtree. Generated road/barrier objects are read only.
        GameObject owner = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != "NightEnnichi") continue;
            Require(owner == null, "Multiple NightEnnichi roots.");
            owner = root;
        }
        if (owner == null)
        {
            owner = new GameObject("NightEnnichi");
            SceneManager.MoveGameObjectToScene(owner, scene);
        }
        var walls = new List<MeshFilter>();
        var exclusions = new List<LanternExclusion>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (RaceBarrierPath barrier in root.GetComponentsInChildren<RaceBarrierPath>(true))
            {
                if (!barrier.isActiveAndEnabled) continue;
                foreach (MeshFilter filter in barrier.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && filter.sharedMesh.name == "RaceBarrierVisualMesh") walls.Add(filter);
            }
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
            {
                if (item.IsChildOf(owner.transform)) continue;
                string name = item.name.ToLowerInvariant();
                if (item.GetComponent<AccelerationPad>() == null && item.GetComponent<GoalSensor>() == null &&
                    !name.Contains("gate") && !name.Contains("startline") && !name.Contains("jump") && !name.Contains("ramp") &&
                    !name.Contains("ジャンプ") && !name.Contains("ゲート")) continue;
                Bounds bounds = new Bounds(item.position, Vector3.zero);
                foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(renderer.bounds);
                foreach (Collider collider in item.GetComponentsInChildren<Collider>(true))
                    if (collider.enabled && collider.gameObject.activeInHierarchy) bounds.Encapsulate(collider.bounds);
                float progress = course.GetProgressDistance(bounds.center);
                if (!course.TryGetSampleAtProgress(progress, out var sample)) continue;
                Vector3 e = bounds.extents, f = sample.forward;
                exclusions.Add(new LanternExclusion { progress = progress,
                    radius = 15f + Mathf.Abs(f.x) * e.x + Mathf.Abs(f.y) * e.y + Mathf.Abs(f.z) * e.z });
            }
        }
        Require(walls.Count > 0, "No generated barrier visuals; cannot locate lanterns outside the walls.");
        var courseSettings = new SerializedObject(course);
        var tube = Property(courseSettings, "road").FindPropertyRelative("tubeInterior").objectReferenceValue as MeshCollider;
        Transform old = owner.transform.Find("Lanterns");
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        var parent = new GameObject("Lanterns");
        parent.transform.SetParent(owner.transform, false);
        int count = 0, skipped = 0;
        float first = -1f, last = -1f, minOffset = float.PositiveInfinity, maxOffset = 0f;
        for (float distance = 20f; distance < course.TotalLength; distance += 40f)
        {
            if (!course.TryGetSampleAtProgress(distance, out var sample)) continue;
            bool excluded = RaceCourseBarriers.IsInsideTube(sample.position, sample.right, tube);
            // Inspect the full safety interval so a short jump cannot fall between probes.
            for (float delta = -15f; delta <= 15f && !excluded; delta += 1f)
                excluded = !course.HasRoadAtProgress(distance + delta);
            foreach (var zone in exclusions)
            {
                float delta = Mathf.Abs(distance - zone.progress);
                if (course.ClosedLoop) delta = Mathf.Min(delta, course.TotalLength - delta);
                if (delta <= zone.radius) { excluded = true; break; }
            }
            if (excluded) { skipped++; continue; }
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 outward = Vector3.ProjectOnPlane(sample.right * side, Vector3.up).normalized;
                Vector3 edge = side < 0 ? sample.leftEdge : sample.rightEdge;
                if (!TryBarrierOutside(edge, sample.forward, outward, walls, out float outside)) continue;
                Vector3 position = edge + outward * (outside + 1.8f) + Vector3.up * 0.15f;
                if (course.IsPointInsideTube(position)) continue;
                var lantern = new GameObject("Andon " + count.ToString("D3"));
                // Configure before enabling ExecuteAlways, so no transient realtime light is created.
                lantern.SetActive(false);
                lantern.transform.SetParent(parent.transform, false);
                lantern.transform.SetPositionAndRotation(position, Quaternion.LookRotation(sample.forward, Vector3.up));
                var andon = lantern.AddComponent<NeonAndonLight>();
                var settings = new SerializedObject(andon);
                Property(settings, "createRealtimeLight").boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();
                lantern.SetActive(true);
                count++;
                if (first < 0f) first = distance;
                last = distance;
                minOffset = Mathf.Min(minOffset, outside + 1.8f);
                maxOffset = Mathf.Max(maxOffset, outside + 1.8f);
            }
        }
        Debug.Log($"[NightEnnichi] Lanterns: {count}, progress {first:F1}..{last:F1} / {course.TotalLength:F1} m, " +
            $"40 m spacing, both sides, 1.8 m outside barrier envelope; edge offsets " +
            $"{(count > 0 ? minOffset : 0f):F1}..{maxOffset:F1} m; excluded stations {skipped}; realtime lights disabled.");
    }

    private static bool TryBarrierOutside(Vector3 edge, Vector3 forward, Vector3 outward,
        List<MeshFilter> walls, out float outside)
    {
        outside = 0f;
        bool found = false;
        Vector3 normal = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        foreach (MeshFilter filter in walls)
        {
            var barrier = filter.GetComponentInParent<RaceBarrierPath>();
            var settings = new SerializedObject(barrier);
            float thickness = Property(settings, "colliderThickness").floatValue *
                Mathf.Max(Mathf.Abs(barrier.transform.lossyScale.x), Mathf.Abs(barrier.transform.lossyScale.z));
            // Slice the actual curved barrier mesh at this station. Include its maximum
            // overhang, not just the wall foot or top, then allow for collider thickness.
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = filter.transform.TransformPoint(vertices[i]);
            for (int i = 0; i < indices.Length; i += 3)
            for (int j = 0; j < 3; j++)
            {
                Vector3 a = vertices[indices[i + j]], b = vertices[indices[i + (j + 1) % 3]];
                float da = Vector3.Dot(a - edge, normal), db = Vector3.Dot(b - edge, normal);
                if (da * db > 0f || Mathf.Abs(da - db) < 0.00001f) continue;
                Vector3 point = Vector3.Lerp(a, b, da / (da - db));
                float lateral = Vector3.Dot(point - edge, outward);
                // Reject opposite walls, neighbouring course legs and vertically separate roads.
                if (lateral < -0.25f || lateral > 60f || Mathf.Abs(point.y - edge.y) > 40f) continue;
                outside = Mathf.Max(outside, lateral + thickness);
                found = true;
            }
        }
        return found;
    }

    private static string SourceName(Material material,
        Dictionary<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> external)
    {
        Require(material != null, "Tree material is missing.");
        string source = null;
        foreach (var entry in external)
        {
            if (entry.Key.type != typeof(Material) || entry.Value != material) continue;
            Require(source == null, "Ambiguous external tree material: " + material.name);
            source = entry.Key.name;
        }
        if (source != null) return source;
        Require(AssetDatabase.GetAssetPath(material) == ModelPath, "Cannot resolve tree material: " + material.name);
        return material.name;
    }

    private static void RecordRole(Dictionary<string, bool> roles, string name, bool leaves)
    {
        Require(!roles.TryGetValue(name, out bool previous) || previous == leaves,
            "Conflicting leaves/trunk classification for " + name);
        roles[name] = leaves;
    }

    private static T UniqueComponent<T>(Scene scene) where T : Component
    {
        T result = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (T component in root.GetComponentsInChildren<T>(true))
        {
            Require(result == null, "Expected exactly one " + typeof(T).Name + " in SampleScene.");
            result = component;
        }
        Require(result != null, "Missing " + typeof(T).Name + " in SampleScene.");
        return result;
    }

    private static SerializedProperty Property(SerializedObject target, string name)
    {
        SerializedProperty property = target.FindProperty(name);
        Require(property != null, "Missing serialized property: " + name);
        return property;
    }

    private static void RecordPrefabChanges(Component component)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(component))
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        EditorUtility.SetDirty(component);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1))),
            "Cannot create folder: " + path);
    }

    private static Material GetOrCreateMaterial(string path, Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Require(AssetDatabase.LoadMainAssetAtPath(path) == null, "Material path is occupied: " + path);
        material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void Configure(Material material, Shader shader, Color color, float smoothness, Color emission)
    {
        material.shader = shader;
        material.SetColor("_BaseColor", color);
        material.SetTexture("_BaseMap", null);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.One);
        material.SetFloat("_DstBlend", (float)BlendMode.Zero);
        material.SetFloat("_ZWrite", 1f);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)RenderQueue.Geometry;
        material.SetTexture("_EmissionMap", null);
        material.SetColor("_EmissionColor", emission);
        bool emissive = emission.maxColorComponent > 0f;
        if (emissive) material.EnableKeyword("_EMISSION");
        else material.DisableKeyword("_EMISSION");
        material.globalIlluminationFlags = emissive
            ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        EditorUtility.SetDirty(material);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[NightEnnichi] " + message);
    }
}
