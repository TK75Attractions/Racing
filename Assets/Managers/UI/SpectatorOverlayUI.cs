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
        playerLabel.text = $"プレイヤー {Mathf.Clamp(watchedPlayerIndex, 0, 1) + 1} を観戦中";
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
        var plate=ArcadeUI.Panel(transform,"PlayerPlate",new Vector2(.31f,.855f),new Vector2(.56f,.955f),p.Ink);
        RacingUITheme.Rule(plate,"LiveAccent",new Vector2(.025f,.22f),new Vector2(.035f,.78f),p.Primary);
        ArcadeUI.Label(plate,"Caption","LIVE / SPECTATOR",new Vector2(.08f,.60f),new Vector2(.95f,.90f),19,p.Soft,true);
        playerLabel=ArcadeUI.Label(plate,"PlayerLabel","プレイヤー 2 を観戦中",new Vector2(.08f,.15f),new Vector2(.95f,.60f),28,p.Paper);
        var entrance=GetComponent<UIRevealSequence>();if(entrance==null)entrance=gameObject.AddComponent<UIRevealSequence>();entrance.Configure(transform,new[]{"PlayerPlate"},new Vector2(18,0),0,.24f);
    }
}
