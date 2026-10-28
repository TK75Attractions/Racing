using UnityEngine;

/// <summary>Places the existing acceleration pads on the actual tube floor. Legacy type name keeps settings compatible.</summary>
public sealed class TubeChargeLineLayout : MonoBehaviour
{
    public bool Available { get; private set; }
    public float EntryProgress { get; private set; }
    public float LaneOffset { get; private set; }
    public const float LaneTolerance = .85f;
    private RaceCourse course;
    private float sign;
    private GameObject visuals;
    public readonly System.Collections.Generic.List<AccelerationPad> Pads = new System.Collections.Generic.List<AccelerationPad>();
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
            for (float d = -12f; d <= 85f; d += 3f)
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
        if (!Available) Debug.LogWarning("Tube acceleration pads: no safe flat run-out; pads were not created.");
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
        if(visuals!=null){if(Application.isPlaying)Destroy(visuals);else DestroyImmediate(visuals);}
        Pads.Clear();visuals=new GameObject("Tube acceleration pads");visuals.transform.SetParent(transform,false);visuals.hideFlags=HideFlags.DontSave;
        course.TryGetSampleAtProgress(EntryProgress+sign*4f,out var sample);
        foreach(int side in new[]{-1,1})
        {
            var host=new GameObject("Acceleration pad "+side);host.transform.SetParent(visuals.transform,false);
            Vector3 position=sample.position+sample.right*(side*LaneOffset);
            if(Physics.Raycast(position+sample.up*2f,-sample.up,out var hit,3.5f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))position=hit.point;
            host.transform.SetPositionAndRotation(position+sample.up*.012f,Quaternion.LookRotation(sample.forward*sign,sample.up));
            var pad=host.AddComponent<AccelerationPad>();pad.ConfigureTubePad(new Vector3(3.6f,.035f,8f));Pads.Add(pad);
        }
    }
}
