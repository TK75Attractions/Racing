using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Builder-baked tube intervals; ambient/fog changes are scoped to each camera render.</summary>
[DisallowMultipleComponent]
public sealed class RaceTubeEnvironmentTransition : MonoBehaviour
{
    [Serializable] public struct Interval { public float start, length; }
    [SerializeField] private RaceCourse course;
    [SerializeField] private Interval[] intervals = Array.Empty<Interval>();
    [SerializeField, Min(1f)] private float blendHalfDistance = 12f;
    public IReadOnlyList<Interval> Intervals => intervals;
    private Camera baseCamera;
    private float baseWeight;
    private readonly Stack<State> states = new Stack<State>(4);
    private struct State
    {
        public Camera camera;
        public Color ambient, fog;
        public float density;
        public SphericalHarmonicsL2 probe;
    }

    // Called only by the editor builder, never performs tube physics queries per frame.
    public void Bake(RaceCourse source)
    {
        course = source;
        var result = new List<Interval>();
        float length = course.TotalLength;
        int steps = Mathf.Max(1, Mathf.CeilToInt(length));
        float step = length / steps;
        bool Inside(float progress) => course.TryGetSampleAtProgress(progress, out var sample) &&
            course.IsPointInsideTube(sample.position + sample.up);
        float origin = 0f;
        int search = 0;
        while (search < steps && Inside(origin)) { origin += step; search++; }
        if (search == steps) { intervals = Array.Empty<Interval>(); return; }
        bool previous = false;
        float start = 0f;
        for (int i = 1; i <= steps; i++)
        {
            float distance = origin + i * step;
            bool inside = Inside(distance);
            if (inside != previous)
            {
                float low = distance-step, high = distance;
                for (int n = 0; n < 8; n++)
                {
                    float mid = (low+high)*.5f;
                    if (Inside(mid) == previous) low = mid; else high = mid;
                }
                float boundary = (low+high)*.5f;
                if (inside) start = boundary;
                else result.Add(new Interval { start = Mathf.Repeat(start, length), length = boundary-start });
            }
            previous = inside;
        }
        intervals = result.ToArray();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        RenderPipelineManager.beginCameraRendering += Begin;
        RenderPipelineManager.endCameraRendering += End;
    }
    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= Begin;
        RenderPipelineManager.endCameraRendering -= End;
        while (states.Count > 0) Restore(states.Pop());
        baseCamera = null;
    }
    private float Weight(Vector3 position)
    {
        if (course == null || !course.HasValidPath || intervals.Length == 0) return 0f;
        float length = course.TotalLength, progress = course.GetProgressDistance(position);
        float signed = float.NegativeInfinity;
        foreach (Interval interval in intervals)
        {
            float along = Mathf.Repeat(progress-interval.start, length);
            float distance = along <= interval.length ? Mathf.Min(along, interval.length-along) :
                -Mathf.Min(along-interval.length, length-along);
            signed = Mathf.Max(signed, distance);
        }
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-blendHalfDistance, blendHalfDistance, signed));
    }
    private void Begin(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType != CameraType.Game || camera.gameObject.scene != gameObject.scene) return;
        float weight;
        bool overlay = camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) && data.renderType == CameraRenderType.Overlay;
        if (overlay && baseCamera != null && baseCamera.TryGetComponent<UniversalAdditionalCameraData>(out var baseData) &&
            baseData.cameraStack != null && baseData.cameraStack.Contains(camera)) weight = baseWeight;
        else
        {
            weight = Weight(camera.transform.position);
            if (!overlay) { baseCamera = camera; baseWeight = weight; }
        }
        var state = new State { camera = camera, ambient = RenderSettings.ambientLight, fog = RenderSettings.fogColor,
            density = RenderSettings.fogDensity, probe = RenderSettings.ambientProbe };
        states.Push(state);
        // Keep the daytime adjustment modest and preserve the exterior navy/violet night palette.
        Color ambient = Color.Lerp(state.ambient, Color.Lerp(state.ambient, new Color(.10f,.14f,.23f), .30f), weight);
        RenderSettings.ambientLight = ambient;
        var probe = state.probe;
        for (int channel = 0; channel < 3; channel++)
            for (int coefficient = 0; coefficient < 9; coefficient++)
                probe[channel, coefficient] *= ambient[channel] / Mathf.Max(.001f, state.ambient[channel]);
        RenderSettings.ambientProbe = probe;
        RenderSettings.fogColor = Color.Lerp(state.fog, new Color(.065f,.085f,.17f), weight * .35f);
        RenderSettings.fogDensity = state.density * Mathf.Lerp(1f, .7f, weight);
    }
    private void End(ScriptableRenderContext context, Camera camera)
    {
        if (states.Count > 0 && states.Peek().camera == camera) Restore(states.Pop());
    }
    private static void Restore(State state)
    {
        RenderSettings.ambientLight = state.ambient;
        RenderSettings.ambientProbe = state.probe;
        RenderSettings.fogColor = state.fog;
        RenderSettings.fogDensity = state.density;
    }
}
