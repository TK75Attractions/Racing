using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// コース全体のワールドXZ範囲を、ミニマップ矩形へアスペクト比を保ったまま収める座標変換です。
/// MonoBehaviour にしないことで、エディタ検証からも直接呼び出せます。
/// </summary>
public sealed class MiniMapProjector
{
    private Vector2 center;
    private float pixelsPerMeter;
    private float rotationDegrees;
    private float rotationSin;
    private float rotationCos = 1f;

    /// <summary>投影が構築済みかどうかです。</summary>
    public bool IsValid { get; private set; }

    /// <summary>ワールド1メートルあたりのUIピクセル数です。</summary>
    public float PixelsPerMeter => pixelsPerMeter;

    /// <summary>コースの内外の縁から投影を構築します。</summary>
    public bool Build(
        List<Vector3> innerPath,
        List<Vector3> outerPath,
        Vector2 rectSize,
        float padding,
        float mapRotationDegrees)
    {
        IsValid = false;
        SetRotation(mapRotationDegrees);

        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        int pointCount = 0;
        pointCount += Accumulate(innerPath, ref min, ref max);
        pointCount += Accumulate(outerPath, ref min, ref max);

        if (pointCount < 2) return false;

        Vector2 size = max - min;
        if (size.x <= Mathf.Epsilon || size.y <= Mathf.Epsilon) return false;

        float usableWidth = Mathf.Max(1f, rectSize.x - (padding * 2f));
        float usableHeight = Mathf.Max(1f, rectSize.y - (padding * 2f));

        // 縦横で同じ倍率を使い、コース形状を歪ませません。
        pixelsPerMeter = Mathf.Min(usableWidth / size.x, usableHeight / size.y);
        center = (min + max) * 0.5f;
        IsValid = true;
        return true;
    }

    /// <summary>ワールド座標を、ミニマップ矩形の中心を原点とするローカル座標へ変換します。</summary>
    public Vector2 ToLocal(Vector3 worldPosition)
    {
        if (!IsValid) return Vector2.zero;
        Vector2 rotated = Rotate(new Vector2(worldPosition.x, worldPosition.z));
        return (rotated - center) * pixelsPerMeter;
    }

    /// <summary>
    /// 車のYaw角を、マーカーのZ回転へ変換します。
    /// ワールドのYawは上から見て時計回りが正、UIの回転は反時計回りが正なので符号を反転します。
    /// </summary>
    public float ToLocalAngle(float worldYawDegrees)
    {
        return -(worldYawDegrees + rotationDegrees);
    }

    /// <summary>リストの全点をまとめてローカル座標へ変換します。</summary>
    public void ToLocalRange(List<Vector3> source, List<Vector2> destination)
    {
        if (source == null || destination == null) return;
        destination.Clear();
        for (int index = 0; index < source.Count; index++)
        {
            destination.Add(ToLocal(source[index]));
        }
    }

    private void SetRotation(float degrees)
    {
        rotationDegrees = degrees;
        float radians = -degrees * Mathf.Deg2Rad;
        rotationSin = Mathf.Sin(radians);
        rotationCos = Mathf.Cos(radians);
    }

    private Vector2 Rotate(Vector2 point)
    {
        return new Vector2(
            (point.x * rotationCos) - (point.y * rotationSin),
            (point.x * rotationSin) + (point.y * rotationCos));
    }

    private int Accumulate(List<Vector3> path, ref Vector2 min, ref Vector2 max)
    {
        if (path == null) return 0;

        for (int index = 0; index < path.Count; index++)
        {
            Vector2 point = Rotate(new Vector2(path[index].x, path[index].z));
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return path.Count;
    }
}
