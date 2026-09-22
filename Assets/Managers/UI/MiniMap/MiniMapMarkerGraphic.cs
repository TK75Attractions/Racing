using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ミニマップ上の車を表す矢印です。外部スプライトを使わずに頂点だけで描きます。
/// 進行方向が分かるよう、+Y方向を車の前方として組み立てます。
/// </summary>
[DisallowMultipleComponent]
public sealed class MiniMapMarkerGraphic : MaskableGraphic
{
    [SerializeField, Range(0f, 0.5f)] private float tailNotch = 0.22f;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();
        float halfWidth = rect.width * 0.5f;
        float halfHeight = rect.height * 0.5f;
        Vector2 center = rect.center;

        Color32 vertexColor = color;
        Vector2 tip = center + new Vector2(0f, halfHeight);
        Vector2 left = center + new Vector2(-halfWidth, -halfHeight);
        Vector2 tail = center + new Vector2(0f, -halfHeight + (rect.height * tailNotch));
        Vector2 right = center + new Vector2(halfWidth, -halfHeight);

        vh.AddVert(tip, vertexColor, Vector2.zero);
        vh.AddVert(left, vertexColor, Vector2.zero);
        vh.AddVert(tail, vertexColor, Vector2.zero);
        vh.AddVert(right, vertexColor, Vector2.zero);

        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(0, 2, 3);
    }
}
