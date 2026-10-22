using UnityEngine;
using UnityEngine.UI;

/// <summary>Soft edge shading leaves the course visible behind readable menu controls.</summary>
public sealed class MenuSceneryScrim : MaskableGraphic
{
    private bool results;

    public static void Create(Transform parent, string name, bool isResult)
    {
        RectTransform rect = RacingUITheme.Rect(parent, name, Vector2.zero, Vector2.one);
        MenuSceneryScrim scrim = rect.GetComponent<MenuSceneryScrim>() ?? rect.gameObject.AddComponent<MenuSceneryScrim>();
        scrim.results = isResult;
        scrim.raycastTarget = false;
        scrim.SetVerticesDirty();
        rect.gameObject.SetActive(false);
    }

    public static void BuildTitleBrand(Transform parent)
    {
        RectTransform brand = RacingUITheme.Rect(parent, "SceneryTitleBrand", new Vector2(.035f, .59f), new Vector2(.635f, .94f));
        NeonUI.Text(brand, "Series", "CAMPUS MOTORSPORT", new Vector2(.008f, .90f), new Vector2(.95f, 1f), 19f, tint: NeonUI.Cyan);
        NeonUI.Text(brand, "Tsukukoma", "TSUKUKOMA", new Vector2(0f, .49f), new Vector2(1f, .91f), 96f, italic: true);
        var circuit = NeonUI.Text(brand, "Circuit", "CIRCUIT", new Vector2(.005f, .12f), new Vector2(.99f, .55f), 116f, italic: true, tint: NeonUI.Pink);
        NeonUI.GlowText(circuit, NeonUI.Pink);
        NeonUI.Text(brand, "Japanese", "ツクコマ・サーキット", new Vector2(.014f, 0f), new Vector2(.9f, .14f), 25f);
        brand.gameObject.SetActive(false);
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float[] columns = { 0f, .08f, .53f, .66f, .96f, 1f };
        float[] rows = { 0f, .12f, .54f, .78f, 1f };
        for (int y = 0; y < rows.Length; y++)
        for (int x = 0; x < columns.Length; x++)
        {
            float px = columns[x], py = rows[y];
            float edge = Mathf.Max(Mathf.Pow(Mathf.Abs(px * 2f - 1f), 4f), Mathf.Pow(Mathf.Abs(py * 2f - 1f), 4f));
            float shade = results ? .13f + edge * .29f : .06f + edge * .35f + Mathf.SmoothStep(0f, .45f, Mathf.InverseLerp(.53f, .73f, px));
            if (!results) shade += Mathf.InverseLerp(.60f, 1f, py) * .17f;
            mesh.AddVert(new Vector3(rect.xMin + px * rect.width, rect.yMin + py * rect.height), new Color(.006f, .016f, .035f, Mathf.Clamp01(shade)), Vector2.zero);
            if (x == 0 || y == 0) continue;
            int i = y * columns.Length + x;
            mesh.AddTriangle(i - columns.Length - 1, i - columns.Length, i);
            mesh.AddTriangle(i - columns.Length - 1, i, i - 1);
        }
    }
}
