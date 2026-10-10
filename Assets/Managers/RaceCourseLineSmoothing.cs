using System.Collections.Generic;
using UnityEngine;

/// <summary>Distance-based averaging rounds the existing alignment without spline overshoot.</summary>
public static class RaceCourseLineSmoothing
{
    // Prefix integrals become large on a full race course. Float cancellation near a
    // jump edge's shrinking averaging window otherwise creates bumps/backtracking.
    private struct IntegralVector
    {
        public double x, y, z;
        public IntegralVector(Vector3 value) { x = value.x; y = value.y; z = value.z; }
        public static IntegralVector operator +(IntegralVector a, IntegralVector b) =>
            new IntegralVector { x = a.x + b.x, y = a.y + b.y, z = a.z + b.z };
        public static IntegralVector operator -(IntegralVector a, IntegralVector b) =>
            new IntegralVector { x = a.x - b.x, y = a.y - b.y, z = a.z - b.z };
        public static IntegralVector operator *(IntegralVector a, double scale) =>
            new IntegralVector { x = a.x * scale, y = a.y * scale, z = a.z * scale };
        public Vector3 Position => new Vector3((float)x, (float)y, (float)z);
    }

    public static void Apply(List<Vector3> path, List<float> widths, List<int> segments,
        bool closed, float cornerDistance, float slopeDistance, float spacing, bool[] jumps)
    {
        if (path.Count < 2) return;
        Vector3 origin = path[0];
        Vector3[] source = path.ToArray();
        float[] sourceWidths = widths.ToArray();
        int[] sourceSegments = segments.ToArray();
        var distances = new float[source.Length];
        var integrals = new IntegralVector[source.Length];
        for (int i = 0; i < source.Length; i++) source[i] -= origin;
        for (int i = 1; i < source.Length; i++)
        {
            float length = Vector3.Distance(source[i - 1], source[i]);
            distances[i] = distances[i - 1] + length;
            // Integrate over the same distance coordinates used for lookup, keeping
            // adjacent prefix intervals exactly continuous even after float distance rounding.
            double interval = (double)distances[i] - distances[i - 1];
            integrals[i] = integrals[i - 1] + (new IntegralVector(source[i - 1]) + new IntegralVector(source[i])) * (interval * 0.5);
        }
        float total = distances[distances.Length - 1];
        if (total <= Mathf.Epsilon) return;
        float horizontalRadius = Mathf.Clamp(cornerDistance, 0f, total * 0.25f);
        float verticalRadius = Mathf.Clamp(slopeDistance, 0f, total * 0.25f);
        int count = Mathf.Max(1, Mathf.CeilToInt(total / Mathf.Max(0.25f, spacing)));
        var sampleDistances = new List<float>(count + source.Length);
        for (int i = 0; i <= count; i++) sampleDistances.Add(total * i / count);
        var gapStarts = new List<float>();
        var gapEnds = new List<float>();
        bool inGap = false;
        for (int i = 0; i < source.Length - 1; i++)
        {
            bool gap = jumps[sourceSegments[i]];
            if (gap == inGap) continue;
            float boundary = distances[i];
            sampleDistances.RemoveAll(value => Mathf.Abs(value - boundary) < 0.0001f);
            sampleDistances.Add(boundary);
            if (gap) gapStarts.Add(distances[i]);
            else gapEnds.Add(distances[i]);
            inGap = gap;
        }
        if (inGap) gapEnds.Add(total);
        sampleDistances.Sort();
        path.Clear(); widths.Clear(); segments.Clear();
        float previousDistance = float.NegativeInfinity;
        foreach (float distance in sampleDistances)
        {
            if (path.Count > 0 && distance - previousDistance < 0.0001f) continue;
            int index = FindSegment(distances, distance);
            float length = distances[index + 1] - distances[index];
            float t = length > Mathf.Epsilon ? (distance - distances[index]) / length : 0f;
            Vector3 point = Vector3.Lerp(source[index], source[index + 1], t);
            float radiusLimit = total;
            bool gap = jumps[sourceSegments[index]];
            for (int i = 0; i < gapStarts.Count; i++)
            {
                // Road smoothing must stop at the takeoff/landing edge, including across the lap seam.
                float startDelta = Mathf.Abs(distance - gapStarts[i]);
                float endDelta = Mathf.Abs(distance - gapEnds[i]);
                if (closed)
                {
                    startDelta = Mathf.Min(startDelta, total - startDelta);
                    endDelta = Mathf.Min(endDelta, total - endDelta);
                }
                radiusLimit = Mathf.Min(radiusLimit, Mathf.Min(startDelta, endDelta));
            }
            Vector3 horizontal = gap ? point : Average(source, distances, integrals, distance, Mathf.Min(horizontalRadius, radiusLimit), closed, point);
            Vector3 vertical = gap ? point : Average(source, distances, integrals, distance, Mathf.Min(verticalRadius, radiusLimit), closed, point);
            path.Add(new Vector3(horizontal.x, vertical.y, horizontal.z) + origin);
            widths.Add(Mathf.Lerp(sourceWidths[index], sourceWidths[index + 1], t));
            segments.Add(sourceSegments[index]);
            previousDistance = distance;
        }
        if (closed)
        {
            path[path.Count - 1] = path[0];
            widths[widths.Count - 1] = widths[0];
        }
    }

    private static Vector3 Average(Vector3[] source, float[] distances, IntegralVector[] integrals,
        float distance, float radius, bool closed, Vector3 fallback)
    {
        if (!closed) radius = Mathf.Min(radius, Mathf.Min(distance, distances[distances.Length - 1] - distance));
        if (radius < 0.001f) return fallback;
        return ((Integral(source, distances, integrals, distance + radius, closed) -
            Integral(source, distances, integrals, distance - radius, closed)) * (1.0 / (2.0 * radius))).Position;
    }

    private static IntegralVector Integral(Vector3[] source, float[] distances, IntegralVector[] integrals,
        float distance, bool closed)
    {
        int last = source.Length - 1;
        float total = distances[last];
        IntegralVector cycles = default;
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
            return new IntegralVector(source[0]) * distance + new IntegralVector(tangent) * ((double)distance * distance * 0.5);
        }
        else if (distance > total)
        {
            int lastSegment = FindSegment(distances, total);
            Vector3 tangent = (source[lastSegment + 1] - source[lastSegment]).normalized;
            float extra = distance - total;
            return integrals[last] + new IntegralVector(source[last]) * extra + new IntegralVector(tangent) * ((double)extra * extra * 0.5);
        }
        int index = FindSegment(distances, distance);
        double length = (double)distances[index + 1] - distances[index];
        double partial = (double)distance - distances[index];
        IntegralVector start = new IntegralVector(source[index]);
        IntegralVector end = start + (new IntegralVector(source[index + 1]) - start) * (length > Mathf.Epsilon ? partial / length : 0.0);
        return cycles + integrals[index] + (start + end) * (partial * 0.5);
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
