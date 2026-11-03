using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Course-relative profile intervals, resolved again whenever the road is rebuilt.</summary>
[DisallowMultipleComponent]
public sealed class RaceBarrierProfiles : MonoBehaviour
{
    public enum Side { Left, Right, Both }

    [Serializable]
    public sealed class Section
    {
        public Side side = Side.Left;
        public Transform landmark;
        public Vector3 localStart, localEnd;
        [Min(1f)] public float entryMeters = 30f;
        [Min(1f)] public float exitMeters = 20f;
        public bool followEntryCorner;
        [Min(10f)] public float cornerSearchMeters = 300f;
    }

    // Course samples can run opposite to race travel (SampleScene uses -1).
    public bool reverseRaceDirection;
    public List<Section> sections = new List<Section>();

    public struct ResolvedSection
    {
        public Side side;
        public float start, length, entry, exit, total;
        public bool reverse, loop;

        public float Weight(float rawProgress, int rawSide)
        {
            int raceSide = reverse ? -rawSide : rawSide;
            if (side != Side.Both && (side == Side.Left ? raceSide != -1 : raceSide != 1)) return 0f;
            float distance = (reverse ? -rawProgress : rawProgress) - start;
            if (loop) distance = Mathf.Repeat(distance + entry, total) - entry;
            if (distance < -entry || distance > length + exit) return 0f;
            float t = distance < 0f ? Mathf.InverseLerp(-entry, 0f, distance) :
                distance > length ? 1f - Mathf.InverseLerp(length, length + exit, distance) : 1f;
            return t * t * (3f - 2f * t);
        }

        public void AddBoundaries(List<float> distances)
        {
            foreach (float value in new[] { start - entry, start, start + length, start + length + exit })
            {
                float raw = reverse ? -value : value;
                distances.Add(loop ? Mathf.Repeat(raw, total) : Mathf.Clamp(raw, 0f, total));
            }
        }
    }

    public List<ResolvedSection> Resolve(IReadOnlyList<Vector3> centers, bool loop)
    {
        var result = new List<ResolvedSection>();
        if (!isActiveAndEnabled || centers.Count < 2) return result;
        var distances = new float[centers.Count];
        for (int i = 1; i < centers.Count; i++) distances[i] = distances[i - 1] + Vector3.Distance(centers[i - 1], centers[i]);
        float total = distances[distances.Length - 1];
        if (total < .01f) return result;
        float direction = reverseRaceDirection ? -1f : 1f;
        foreach (Section section in sections)
        {
            if (section == null || section.landmark == null) continue;
            float rawStart = Project(section.landmark.TransformPoint(section.localStart), centers, distances);
            float rawEnd = Project(section.landmark.TransformPoint(section.localEnd), centers, distances);
            float start = direction * rawStart;
            float length = direction * (rawEnd - rawStart);
            if (loop) length = Mathf.Repeat(length, total);
            if (length <= .01f || length > total * .5f) continue;
            float entry = Mathf.Max(1f, section.entryMeters);
            if (section.followEntryCorner)
            {
                Vector3 heading = Heading(rawStart, centers, distances, loop) * direction;
                float cornerEnd = -1f, cornerStart = -1f;
                // Search the preceding route, including long straights between the
                // wall landmark and the corner. A fraction of total length can
                // exclude that corner when the start straight moves north.
                float available = loop ? total - length : reverseRaceDirection ? total - rawStart : rawStart;
                for (float back = 1f; back <= Mathf.Min(section.cornerSearchMeters, available); back += .5f)
                {
                    Vector3 previous = Heading(rawStart - direction * back, centers, distances, loop) * direction;
                    float angle = Vector3.Angle(heading, previous);
                    if (cornerEnd < 0f && angle > 3f) cornerEnd = Mathf.Max(0f, back - 1f);
                    if (cornerEnd >= 0f && angle >= 87f) { cornerStart = back; break; }
                }
                if (cornerEnd >= 0f && cornerStart > cornerEnd)
                {
                    start -= cornerEnd;
                    length += cornerEnd;
                    entry = Mathf.Max(1f, cornerStart - cornerEnd);
                }
            }
            // Leave a default-arc interval even on a closed course.
            float exit = Mathf.Max(1f, section.exitMeters);
            if (length + entry + exit >= total) continue;
            result.Add(new ResolvedSection { side = section.side, start = start, length = length,
                entry = entry, exit = exit, total = total, reverse = reverseRaceDirection, loop = loop });
        }
        return result;
    }

    private static float Project(Vector3 point, IReadOnlyList<Vector3> centers, float[] distances)
    {
        float best = float.PositiveInfinity, progress = 0f;
        for (int i = 1; i < centers.Count; i++)
        {
            Vector3 delta = centers[i] - centers[i - 1];
            float t = delta.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(point - centers[i - 1], delta) / delta.sqrMagnitude) : 0f;
            float error = (point - Vector3.Lerp(centers[i - 1], centers[i], t)).sqrMagnitude;
            if (error >= best) continue;
            best = error;
            progress = Mathf.Lerp(distances[i - 1], distances[i], t);
        }
        return progress;
    }

    private static Vector3 Heading(float raw, IReadOnlyList<Vector3> centers, float[] distances, bool loop)
    {
        // A 5 m chord suppresses noise in densely sampled course curves.
        return Vector3.ProjectOnPlane(Point(raw + 2.5f, centers, distances, loop) -
            Point(raw - 2.5f, centers, distances, loop), Vector3.up).normalized;
    }

    private static Vector3 Point(float raw, IReadOnlyList<Vector3> centers, float[] distances, bool loop)
    {
        float total = distances[distances.Length - 1];
        raw = loop ? Mathf.Repeat(raw, total) : Mathf.Clamp(raw, 0f, total);
        int low = 1, high = distances.Length - 1;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (distances[middle] < raw) low = middle + 1; else high = middle;
        }
        return Vector3.Lerp(centers[low - 1], centers[low], Mathf.InverseLerp(distances[low - 1], distances[low], raw));
    }
}
