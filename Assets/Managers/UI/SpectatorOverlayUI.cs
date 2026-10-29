using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ゴール済みプレイヤーの画面に、観戦対象を示す枠とラベルを重ねます。</summary>
[DisallowMultipleComponent]
public sealed class SpectatorOverlayUI : MonoBehaviour
{
    private TMP_Text playerLabel;

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
        playerLabel.text = $"P{Mathf.Clamp(watchedPlayerIndex, 0, 1) + 1} のレース";
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void Build(TMP_FontAsset font)
    {
        var p=ArcadeUI.For(transform);
        var plate=ArcadeUI.Panel(transform,"PlayerPlate",new Vector2(.49f,.855f),new Vector2(.565f,.955f),p.Ink);
        LiveBroadcastBadge.Create(transform,"SpectatorLive",new Vector2(.365f,.845f),new Vector2(.48f,.97f));
        RacingUITheme.Rule(plate,"LiveAccent",new Vector2(.025f,.22f),new Vector2(.035f,.78f),p.Primary);
        ArcadeUI.Label(plate,"Caption","かんせん",new Vector2(.08f,.62f),new Vector2(.95f,.90f),17,p.Soft,true);
        playerLabel=ArcadeUI.Label(plate,"PlayerLabel","P2 のレース",new Vector2(.08f,.15f),new Vector2(.95f,.60f),28,p.Paper);
        var entrance=GetComponent<UIRevealSequence>();if(entrance==null)entrance=gameObject.AddComponent<UIRevealSequence>();entrance.Configure(transform,new[]{"PlayerPlate"},new Vector2(18,0),0,.24f);
    }
}
