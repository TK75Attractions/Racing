using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ミニマップのコース帯を1枚のUIメッシュとして生成します。
/// 頂点は <see cref="SetBand"/> を呼んだときだけ組み立て直すため、毎フレームの負荷はありません。
/// </summary>
[DisallowMultipleComponent]
public sealed class MiniMapTrackGraphic : MaskableGraphic
{
    [SerializeField, Range(0.2f, 3f)] private float widthScale = 1f;

    private readonly List<Vector2> innerLocal = new List<Vector2>();
    private readonly List<Vector2> outerLocal = new List<Vector2>();

    /// <summary>コース幅の倍率です。1より大きくすると縁取り用の一回り太い帯になります。</summary>
    public float WidthScale
    {
        get => widthScale;
        set
        {
            if (Mathf.Approximately(widthScale, value)) return;
            widthScale = value;
            SetVerticesDirty();
        }
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    /// <summary>ミニマップローカル座標の内側・外側の縁を設定します。</summary>
    public void SetBand(List<Vector2> inner, List<Vector2> outer)
    {
        innerLocal.Clear();
        outerLocal.Clear();

        if (inner != null && outer != null)
        {
            int count = Mathf.Min(inner.Count, outer.Count);
            for (int index = 0; index < count; index++)
            {
                innerLocal.Add(inner[index]);
                outerLocal.Add(outer[index]);
            }
        }

        SetVerticesDirty();
    }

    /// <summary>帯の頂点を破棄します。</summary>
    public void ClearBand()
    {
        innerLocal.Clear();
        outerLocal.Clear();
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        int pointCount = Mathf.Min(innerLocal.Count, outerLocal.Count);
        if (pointCount < 2) return;

        Color32 vertexColor = color;
        for (int index = 0; index < pointCount; index++)
        {
            GetScaledEdges(index, out Vector2 inner, out Vector2 outer);
            vh.AddVert(inner, vertexColor, Vector2.zero);
            vh.AddVert(outer, vertexColor, Vector2.zero);
        }

        // RaceCourse は最終ウェイポイントから先頭へ戻るサンプルまで生成するので、
        // ここでは隣り合う区間を素直に繋ぐだけで閉路になります。
        for (int index = 0; index < pointCount - 1; index++)
        {
            int baseIndex = index * 2;
            vh.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 3);
            vh.AddTriangle(baseIndex, baseIndex + 3, baseIndex + 2);
        }
    }

    private void GetScaledEdges(int index, out Vector2 inner, out Vector2 outer)
    {
        inner = innerLocal[index];
        outer = outerLocal[index];

        if (Mathf.Approximately(widthScale, 1f)) return;

        Vector2 mid = (inner + outer) * 0.5f;
        inner = mid + ((inner - mid) * widthScale);
        outer = mid + ((outer - mid) * widthScale);
    }
}
