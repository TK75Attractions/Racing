using UnityEngine;

/// <summary>One non-physical optional side-line section, selected only on the actual tube's lower road.</summary>
public sealed class TubeChargeLineLayout : MonoBehaviour
{
    public bool Available { get; private set; }
    public float EntryProgress { get; private set; }
    public float LaneOffset { get; private set; }
    public const float LaneTolerance = .85f;
    private RaceCourse course;
    private float sign;
    private GameObject visuals;
    private Material material;
    public bool VisualsVisible => visuals != null && visuals.activeSelf;
    public void Configure(RaceCourse path, float direction)
    {
        if (course == path && sign == direction) return;
        course = path; sign = direction; Available = false;
        ChargeCrystal[] crystals = FindObjectsByType<ChargeCrystal>(FindObjectsSortMode.None);
        for (float raw = 30f; raw < course.TotalLength - 30f; raw += 3f)
        {
            if (!course.TryGetSampleAtProgress(raw, out RaceCourse.CourseSample start) || start.width < 10f) continue;
            float offset = Mathf.Min(3.2f, start.width * .5f - 2.2f);
            bool safe = true;
            for (float d = -6f; d <= TubeLineChallenge.Length + 6f; d += 3f)
            {
                float progress = raw + sign * d;
                if (!course.HasRoadAtProgress(progress) || !course.TryGetSampleAtProgress(progress, out RaceCourse.CourseSample sample) ||
                    Mathf.Abs(sample.forward.y) > .12f || Vector3.Angle(start.forward, sample.forward) > 10f ||
                    sample.width < (offset + LaneTolerance + 1.2f) * 2f ||
                    !course.IsPointInsideTube(sample.position + sample.up) ||
                    !LaneHasGround(sample, offset) || !LaneHasGround(sample, -offset)) { safe = false; break; }
                foreach (ChargeCrystal crystal in crystals)
                    if (Vector3.Distance(crystal.transform.position, sample.position) < 14f) { safe = false; break; }
                if (!safe) break;
            }
            if (!safe) continue;
            EntryProgress = raw; LaneOffset = offset; Available = true; BuildVisuals(); break;
        }
        if (!Available) Debug.LogWarning("Tube charge line: no safe lower-road section; no reward line was created.");
        ApplySwitch();
    }
    private bool LaneHasGround(RaceCourse.CourseSample sample, float offset)
    {
        Vector3 position = sample.position + sample.right * offset;
        return course.IsPointInsideTube(position + sample.up) &&
            Physics.Raycast(position + sample.up * 2f, -sample.up, out RaycastHit hit, 3.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.rigidbody == null &&
            Vector3.Dot(hit.normal, sample.up) > .95f && Mathf.Abs(Vector3.Dot(hit.point - position, sample.up)) < 1f;
    }
    public float DistanceFromEntry(float raw)
    {
        float distance = (raw - EntryProgress) * sign;
        return course.ClosedLoop ? Mathf.Repeat(distance + course.TotalLength * .5f, course.TotalLength) - course.TotalLength * .5f : distance;
    }
    public void ApplySwitch()
    { if (visuals != null) visuals.SetActive(Available && RaceFeatureController.Enabled(RaceFeature.TubeChargeLines)); }
    private void BuildVisuals()
    {
        if (visuals != null) Destroy(visuals);
        visuals = new GameObject("Optional tube charge lines"); visuals.transform.SetParent(transform, false);
        visuals.hideFlags = HideFlags.DontSave;
        if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
        material = ItemVisualFactory.CreateEmissiveMaterial("Tube charge line", new Color(.25f, 1f, .75f), 2f, 1f);
        foreach (int side in new[] { -1, 1 })
        {
            for (int rail = -1; rail <= 1; rail += 2)
            {
                var points = new Vector3[10];
                for (int i = 0; i < points.Length; i++)
                {
                    course.TryGetSampleAtProgress(EntryProgress + sign * i * 2f, out RaceCourse.CourseSample sample);
                    points[i] = sample.position + sample.right * (side * LaneOffset + rail * LaneTolerance) + sample.up * .08f;
                }
                MakeLine("Charge lane rail", points, .12f);
            }
            for (float d = 0; d <= TubeLineChallenge.Length; d += 6f)
            {
                course.TryGetSampleAtProgress(EntryProgress + sign * d, out RaceCourse.CourseSample sample);
                Vector3 centre = sample.position + sample.right * side * LaneOffset + sample.up * .09f;
                Vector3 forward = sample.forward * sign;
                MakeLine("Charge direction", new[] { centre - forward * .7f - sample.right * .55f,
                    centre + forward * .45f, centre - forward * .7f + sample.right * .55f }, .16f);
            }
        }
    }
    private void MakeLine(string label, Vector3[] points, float width)
    {
        var child = new GameObject(label); child.transform.SetParent(visuals.transform, false);
        LineRenderer line = child.AddComponent<LineRenderer>(); line.sharedMaterial = material;
        line.useWorldSpace = true; line.positionCount = points.Length; line.SetPositions(points);
        line.widthMultiplier = width; line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
    }
    private void OnDestroy()
    {
        if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
    }
}
