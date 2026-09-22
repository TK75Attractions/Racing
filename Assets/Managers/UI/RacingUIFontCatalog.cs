using TMPro;
using UnityEngine;

public enum FontRole { Japanese, English, InstrumentDigits }

/// <summary>ゲーム全体のフォント用途を固定します。DSEG7は車載計器の数字だけに使用します。</summary>
[CreateAssetMenu(menuName = "Racing/UI Font Catalog", fileName = "RacingUIFontCatalog")]
public sealed class RacingUIFontCatalog : ScriptableObject
{
    [SerializeField] private TMP_FontAsset mplus;
    [SerializeField] private TMP_FontAsset video;
    [SerializeField] private TMP_FontAsset dseg7;
    private static RacingUIFontCatalog instance;
    private Material japaneseUIMaterial;

    public static Material GetJapaneseUIMaterial()
    {
        TMP_FontAsset font = Get(FontRole.Japanese);
        if (font == null) return null;
        if (instance.japaneseUIMaterial == null)
        {
            // The supplied variable-font atlas is baked from its Thin master.
            // A shared SDF weight keeps small Japanese labels legible without bitmap scaling.
            instance.japaneseUIMaterial = new Material(font.material) { name = "MPLUS UI Medium" };
            instance.japaneseUIMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, 0.2f);
        }
        return instance.japaneseUIMaterial;
    }

    private void OnDisable()
    {
        if (japaneseUIMaterial == null) return;
        if (Application.isPlaying) Destroy(japaneseUIMaterial); else DestroyImmediate(japaneseUIMaterial);
        japaneseUIMaterial = null;
    }

    public static TMP_FontAsset Get(FontRole role)
    {
        if (instance == null) instance = Resources.Load<RacingUIFontCatalog>("UI/RacingUIFontCatalog");
        if (instance == null) return null;
        return role switch
        {
            FontRole.Japanese => instance.mplus,
            FontRole.English => instance.video,
            FontRole.InstrumentDigits => instance.dseg7,
            _ => instance.mplus
        };
    }
}
