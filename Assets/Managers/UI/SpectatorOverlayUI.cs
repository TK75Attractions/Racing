using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ゴール済みプレイヤーの画面に、観戦対象を示す枠とラベルを重ねます。</summary>
[DisallowMultipleComponent]
public sealed class SpectatorOverlayUI : MonoBehaviour
{
    private TMP_Text playerLabel;
    private Image playerAccent;
    private readonly Image[] frame = new Image[4];

    public static SpectatorOverlayUI Create(Transform canvasRoot, TMP_FontAsset font)
    {
        Transform existing = canvasRoot.Find("SpectatorOverlay");
        GameObject root = existing != null
            ? existing.gameObject
            : new GameObject("SpectatorOverlay", typeof(RectTransform));
        root.layer = canvasRoot.gameObject.layer;
        root.transform.SetParent(canvasRoot, false);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin=Vector2.zero;rootRect.anchorMax=Vector2.one;
        rootRect.offsetMin=rootRect.offsetMax=Vector2.zero;

        SpectatorOverlayUI view = root.GetComponent<SpectatorOverlayUI>();
        if (view == null) view = root.AddComponent<SpectatorOverlayUI>();
        view.Build(font);
        root.SetActive(false);
        return view;
    }

    public void Show(int watchedPlayerIndex)
    {
        int watched = Mathf.Clamp(watchedPlayerIndex, 0, 1);
        playerLabel.text = $"P{watched + 1} のレース";
        Color color = ArcadeUI.For(watched).Primary;
        foreach (var edge in frame) edge.color = color;
        playerAccent.color = color;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void BuildFrameEdge(int index, string name, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size)
    {
        var rect = RacingUITheme.Rect(transform, name, min, max);
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        frame[index] = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
        frame[index].raycastTarget = false;
    }

    private void Build(TMP_FontAsset font)
    {
        var p=ArcadeUI.For(transform);
        BuildFrameEdge(0,"FrameLeft",new Vector2(0,0),new Vector2(0,1),new Vector2(0,.5f),new Vector2(12,0));
        BuildFrameEdge(1,"FrameRight",new Vector2(1,0),new Vector2(1,1),new Vector2(1,.5f),new Vector2(12,0));
        BuildFrameEdge(2,"FrameTop",new Vector2(0,1),new Vector2(1,1),new Vector2(.5f,1),new Vector2(0,12));
        BuildFrameEdge(3,"FrameBottom",new Vector2(0,0),new Vector2(1,0),new Vector2(.5f,0),new Vector2(0,12));
        var plate=ArcadeUI.Panel(transform,"PlayerPlate",new Vector2(.49f,.855f),new Vector2(.565f,.955f),p.Ink);
        LiveBroadcastBadge.Create(transform,"SpectatorLive",new Vector2(.365f,.845f),new Vector2(.48f,.97f),showDescription:false);
        RacingUITheme.Rule(plate,"LiveAccent",new Vector2(.025f,.22f),new Vector2(.035f,.78f),p.Primary);
        playerAccent=plate.Find("LiveAccent").GetComponent<Image>();
        ArcadeUI.Label(plate,"Caption","かんせん",new Vector2(.08f,.62f),new Vector2(.95f,.90f),17,p.Soft,true);
        playerLabel=ArcadeUI.Label(plate,"PlayerLabel","P2 のレース",new Vector2(.08f,.15f),new Vector2(.95f,.60f),28,p.Paper);
        var entrance=GetComponent<UIRevealSequence>();if(entrance==null)entrance=gameObject.AddComponent<UIRevealSequence>();entrance.Configure(transform,new[]{"PlayerPlate"},new Vector2(18,0),0,.24f);
    }
}
