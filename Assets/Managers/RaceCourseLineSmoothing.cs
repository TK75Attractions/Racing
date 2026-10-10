using System.Collections.Generic;
using UnityEngine;

/// <summary>Distance-based averaging rounds the existing alignment without spline overshoot.</summary>
public static class RaceCourseLineSmoothing
{
    public static void Apply(List<Vector3> path, List<float> widths, List<int> segments,
        bool closed, float cornerDistance, float slopeDistance, float spacing)
    {
        if (path.Count < 2) return;
        Vector3 origin = path[0];
        Vector3[] source = path.ToArray();
        float[] sourceWidths = widths.ToArray();
        int[] sourceSegments = segments.ToArray();
        var distances = new float[source.Length];
        var integrals = new Vector3[source.Length];
        for (int i = 0; i < source.Length; i++) source[i] -= origin;
        for (int i = 1; i < source.Length; i++)
        {
            float length = Vector3.Distance(source[i - 1], source[i]);
            distances[i] = distances[i - 1] + length;
            integrals[i] = integrals[i - 1] + (source[i - 1] + source[i]) * (length * 0.5f);
        }
        float total = distances[distances.Length - 1];
        if (total <= Mathf.Epsilon) return;
        float horizontalRadius = Mathf.Clamp(cornerDistance, 0f, total * 0.25f);
        float verticalRadius = Mathf.Clamp(slopeDistance, 0f, total * 0.25f);
        int count = Mathf.Max(1, Mathf.CeilToInt(total / Mathf.Max(0.25f, spacing)));
        path.Clear(); widths.Clear(); segments.Clear();
        for (int i = 0; i <= count; i++)
        {
            float distance = total * i / count;
            int index = FindSegment(distances, distance);
            float length = distances[index + 1] - distances[index];
            float t = length > Mathf.Epsilon ? (distance - distances[index]) / length : 0f;
            Vector3 point = Vector3.Lerp(source[index], source[index + 1], t);
            Vector3 horizontal = Average(source, distances, integrals, distance, horizontalRadius, closed, point);
            Vector3 vertical = Average(source, distances, integrals, distance, verticalRadius, closed, point);
            path.Add(new Vector3(horizontal.x, vertical.y, horizontal.z) + origin);
            widths.Add(Mathf.Lerp(sourceWidths[index], sourceWidths[index + 1], t));
            // A sampled vertex at the end of a raw segment belongs to the following segment.
            segments.Add(sourceSegments[index + 1]);
        }
        if (closed)
        {
            path[path.Count - 1] = path[0];
            widths[widths.Count - 1] = widths[0];
        }
    }

    private static Vector3 Average(Vector3[] source, float[] distances, Vector3[] integrals,
        float distance, float radius, bool closed, Vector3 fallback)
    {
        if (!closed) radius = Mathf.Min(radius, Mathf.Min(distance, distances[distances.Length - 1] - distance));
        if (radius < 0.001f) return fallback;
        return (Integral(source, distances, integrals, distance + radius, closed) -
            Integral(source, distances, integrals, distance - radius, closed)) / (2f * radius);
    }

    private static Vector3 Integral(Vector3[] source, float[] distances, Vector3[] integrals,
        float distance, bool closed)
    {
        int last = source.Length - 1;
        float total = distances[last];
        Vector3 cycles = Vector3.zero;
        if (closed)
        {
            int cycle = Mathf.FloorToInt(distance / total);
            distance -= cycle * total;
            cycles = integrals[last] * cycle;
        }
        else if (distance < 0f)
        {
            int firstSegment = FindSegment(distances, 0f);
            Vector3 tangent = (source[firstSegment + 1] - source[firstSegment]).normalized;
            return source[0] * distance + tangent * (distance * distance * 0.5f);
        }
        else if (distance > total)
        {
            int lastSegment = FindSegment(distances, total);
            Vector3 tangent = (source[lastSegment + 1] - source[lastSegment]).normalized;
            float extra = distance - total;
            return integrals[last] + source[last] * extra + tangent * (extra * extra * 0.5f);
        }
        int index = FindSegment(distances, distance);
        float length = distances[index + 1] - distances[index];
        float partial = distance - distances[index];
        Vector3 end = Vector3.Lerp(source[index], source[index + 1], length > Mathf.Epsilon ? partial / length : 0f);
        return cycles + integrals[index] + (source[index] + end) * (partial * 0.5f);
    }

    private static int FindSegment(float[] distances, float distance)
    {
        int low = 0, high = distances.Length - 2;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (distances[middle + 1] <= distance) low = middle + 1;
            else high = middle;
        }
        while (low > 0 && distances[low + 1] - distances[low] <= Mathf.Epsilon) low--;
        return low;
    }
}
