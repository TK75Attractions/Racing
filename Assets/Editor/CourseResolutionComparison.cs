using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Resolution matrix for CourseVisualTour: matching shots, native pixels and labelled source links.</summary>
public static class CourseResolutionComparison
{
    [Serializable] public sealed class Image
    {
        public string file;
        public int width, height;
        public float worldRenderScale;
    }

    public static List<Vector2Int> ReadResolutions()
    {
        string value = Environment.GetEnvironmentVariable("RACING_VISUAL_RESOLUTIONS");
        if (string.IsNullOrWhiteSpace(value)) return new List<Vector2Int> { new Vector2Int(1600, 900) };
        var sizes = new List<Vector2Int>();
        foreach (string token in value.Split(','))
        {
            string[] pair = token.Trim().ToLowerInvariant().Split('x');
            if (pair.Length != 2 || !int.TryParse(pair[0], out int width) || !int.TryParse(pair[1], out int height) ||
                width < 640 || width > 3840 || height < 360 || height > 2160)
                throw new ArgumentException("RACING_VISUAL_RESOLUTIONS requires WxH (640..3840 x 360..2160), comma separated.");
            var size = new Vector2Int(width, height);
            if (sizes.Contains(size)) throw new ArgumentException("Duplicate visual resolution: " + token);
            sizes.Add(size);
        }
        if (sizes.Count > 3) throw new ArgumentException("At most three visual resolutions per run.");
        return sizes;
    }

    public static float ReadScale()
    {
        string value = Environment.GetEnvironmentVariable("RACING_VISUAL_RENDER_SCALE");
        if (string.IsNullOrWhiteSpace(value)) return 0f;
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale) ||
            float.IsNaN(scale) || scale < .5f || scale > 1f)
            throw new ArgumentException("RACING_VISUAL_RENDER_SCALE must be 0.5..1 (invariant decimal).");
        return scale;
    }

    public static void CreateSheet(string output, List<Vector2Int> sizes, List<List<Image>> rows)
    {
        if (rows.Count == 0) return;
        const int cellWidth = 384, overviewHeight = 216, crop = 128, header = 32;
        const int rowHeight = header + overviewHeight + crop * 2;
        int height = header + rowHeight * rows.Count;
        if (height > SystemInfo.maxTextureSize || sizes.Count * cellWidth > SystemInfo.maxTextureSize)
            throw new InvalidOperationException("Resolution sheet exceeds this GPU's maximum texture size.");
        var sheet = new Texture2D(sizes.Count * cellWidth, height, TextureFormat.RGB24, false);
        var thumbnail = RenderTexture.GetTemporary(cellWidth, overviewHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var active = RenderTexture.active;
        var html = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>Course resolution comparison</title><style>body{background:#10151f;color:white;font:16px sans-serif}img{max-width:100%}td{vertical-align:top;padding:8px}a{color:#8eeaff}</style><h1>Resolution comparison</h1><p>PNG: row number matches the table below. Each cell: 384x216 overview; six 128x128 native-pixel crops (top left/centre/right, bottom left/centre/right). Open original PNGs at 100% for all text and edges.</p><img src=\"resolution-sheet.png\"><table>");
        try
        {
            sheet.SetPixels32(new Color32[sheet.width * sheet.height]);
            for (int column = 0; column < sizes.Count; column++)
                Digits(sheet, column * cellWidth + 8, sheet.height - 24, sizes[column].x + "x" + sizes[column].y);
            for (int row = 0; row < rows.Count; row++)
            {
                html.Append("<tr><th>").Append(row + 1).Append("</th>");
                int top = sheet.height - header - row * rowHeight;
                for (int column = 0; column < sizes.Count; column++)
                {
                    if (rows[row].Count <= column) throw new IOException("Incomplete resolution row " + (row + 1));
                    var entry = rows[row][column];
                    var source = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    try
                    {
                        if (!ImageConversion.LoadImage(source, File.ReadAllBytes(Path.Combine(output, entry.file))))
                            throw new IOException("Cannot load " + entry.file);
                        Digits(sheet, column * cellWidth + 8, top - 24, (row + 1).ToString(CultureInfo.InvariantCulture));
                        source.filterMode = FilterMode.Bilinear;
                        Graphics.Blit(source, thumbnail);
                        RenderTexture.active = thumbnail;
                        sheet.ReadPixels(new Rect(0, 0, cellWidth, overviewHeight), column * cellWidth, top - header - overviewHeight);
                        for (int band = 0; band < 2; band++)
                        for (int part = 0; part < 3; part++)
                        {
                            int x = part == 0 ? 0 : part == 1 ? (source.width - crop) / 2 : source.width - crop;
                            int y = band == 0 ? source.height - crop : 0;
                            sheet.SetPixels(column * cellWidth + part * crop, top - header - overviewHeight - (band + 1) * crop,
                                crop, crop, source.GetPixels(x, y, crop, crop));
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(source); }
                    string file = System.Security.SecurityElement.Escape(entry.file);
                    html.Append("<td><a href=\"").Append(file).Append("\">").Append(file).Append("</a><br>")
                        .Append(entry.width).Append('x').Append(entry.height).Append(" / world scale ")
                        .Append(entry.worldRenderScale.ToString("0.00", CultureInfo.InvariantCulture)).Append(" / UI 1.00</td>");
                }
                html.Append("</tr>");
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(output, "resolution-sheet.png"), sheet.EncodeToPNG());
            File.WriteAllText(Path.Combine(output, "resolution-index.html"), html.Append("</table>").ToString());
        }
        finally
        {
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(thumbnail);
            UnityEngine.Object.DestroyImmediate(sheet);
        }
    }

    // Tiny numeric labels avoid requiring a font atlas or an extra camera for the sheet itself.
    private static void Digits(Texture2D image, int x, int y, string value)
    {
        string[] glyphs = { "111101101101111", "010110010010111", "111001111100111", "111001111001111",
            "101101111001001", "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111" };
        foreach (char character in value)
        {
            string glyph = character == 'x' ? "000101010101000" : glyphs[character - '0'];
            for (int row = 0; row < 5; row++)
            for (int column = 0; column < 3; column++)
                if (glyph[row * 3 + column] == '1')
                    for (int dy = 0; dy < 3; dy++) for (int dx = 0; dx < 3; dx++)
                        image.SetPixel(x + column * 3 + dx, y + (4 - row) * 3 + dy, Color.white);
            x += 12;
        }
    }
}
