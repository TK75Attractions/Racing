using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>High-contrast live identifier, shown only for current race footage.</summary>
public sealed class LiveBroadcastBadge : MonoBehaviour
{
    private Graphic dot;
    public static LiveBroadcastBadge Create(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var panel = ArcadeUI.Panel(parent, name, min, max, ArcadeUI.Hex("e43e50"));
        var badge = panel.GetComponent<LiveBroadcastBadge>() ?? panel.gameObject.AddComponent<LiveBroadcastBadge>();
        var marker = RacingUITheme.Rect(panel,"OnAir",new Vector2(.04f,.37f),new Vector2(.12f,.63f));
        badge.dot = marker.GetComponent<ArcadeLiveDot>() ?? marker.gameObject.AddComponent<ArcadeLiveDot>();
        badge.dot.color = Color.white;
        ArcadeUI.Label(panel,"Label","LIVE",new Vector2(.16f,.23f),new Vector2(.95f,.96f),76,Color.white,true);
        ArcadeUI.Label(panel,"Description","ただいまレース中",new Vector2(.17f,.025f),new Vector2(.95f,.27f),20,Color.white);
        return badge;
    }
    private void Update()
    {
        // Keep LIVE legible throughout; only the small on-air light gently changes brightness.
        if (dot != null && !UIMotion.IsPaused)
        { var tint=Color.white; tint.a=.65f+.35f*(.5f+.5f*Mathf.Sin((float)UIMotion.Realtime*3f)); dot.color=tint; }
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class ArcadeLiveDot : MaskableGraphic
{
    protected override void OnEnable() { base.OnEnable(); raycastTarget=false; }
    protected override void OnPopulateMesh(VertexHelper vh)
    { vh.Clear(); var r=rectTransform.rect; ArcadeSurfaceGraphic.Disc(vh,r.center,Mathf.Min(r.width,r.height)*.4f,color); }
}
