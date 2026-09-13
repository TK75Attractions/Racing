# Deterministic, seamlessly tiling 672x672 alpha mask for the race barrier.
# Every tile contains 14 columns by 12 vertical periods of flat-top hexagons.
$source = @'
using System;
using System.IO;
using System.IO.Compression;
using System.Text;

public static class RaceBarrierHexTexture
{
    public static void Generate(string path)
    {
        const int size = 672;
        const int columnPitch = 48;
        const int rowPitch = 56;
        const int alternateOffset = 28;
        const double diagonalLength = 32.2490309931942; // sqrt(28^2 + 16^2)

        byte[] pixels = new byte[size * (size * 4 + 1)];
        for (int y = 0; y < size; y++)
        {
            int rowStart = y * (size * 4 + 1);
            pixels[rowStart] = 0; // PNG filter type: None
            for (int x = 0; x < size; x++)
            {
                double px = x + 0.5;
                double py = y + 0.5;
                double nearestDistanceSquared = double.MaxValue;
                double nearestX = 0;
                double nearestY = 0;
                int baseColumn = (int)Math.Floor(px / columnPitch);

                for (int column = baseColumn - 1; column <= baseColumn + 2; column++)
                {
                    double cx = column * columnPitch;
                    int baseRow = (int)Math.Floor((py - (column & 1) * alternateOffset) / rowPitch);
                    for (int row = baseRow - 1; row <= baseRow + 2; row++)
                    {
                        double cy = row * rowPitch + (column & 1) * alternateOffset;
                        double dx = px - cx;
                        double dy = py - cy;
                        double distanceSquared = dx * dx + dy * dy;
                        if (distanceSquared < nearestDistanceSquared)
                        {
                            nearestDistanceSquared = distanceSquared;
                            nearestX = Math.Abs(dx);
                            nearestY = Math.Abs(dy);
                        }
                    }
                }

                // Flat-top hexagon: (32,0), (16,28), (-16,28), ...
                double horizontalEdge = 28.0 - nearestY;
                double diagonalEdge = (896.0 - 28.0 * nearestX - 16.0 * nearestY) / diagonalLength;
                double edgeDistance = Math.Min(horizontalEdge, diagonalEdge);
                double mask = Math.Max(0.0, Math.Min(1.0, (2.25 - edgeDistance) / 1.25));
                int pixelStart = rowStart + 1 + x * 4;
                pixels[pixelStart] = 255;
                pixels[pixelStart + 1] = 255;
                pixels[pixelStart + 2] = 255;
                pixels[pixelStart + 3] = (byte)Math.Round(mask * 255.0);
            }
        }

        using (MemoryStream compressed = new MemoryStream())
        {
            using (ZLibStream zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                zlib.Write(pixels, 0, pixels.Length);

            using (FileStream file = File.Create(path))
            {
                file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                byte[] header = new byte[13];
                WriteBigEndian(header, 0, size);
                WriteBigEndian(header, 4, size);
                header[8] = 8; // bit depth
                header[9] = 6; // RGBA
                WriteChunk(file, "IHDR", header);
                WriteChunk(file, "IDAT", compressed.ToArray());
                WriteChunk(file, "IEND", Array.Empty<byte>());
            }
        }
    }

    private static void WriteBigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream stream, string name, byte[] data)
    {
        byte[] length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        stream.Write(length, 0, 4);
        byte[] type = Encoding.ASCII.GetBytes(name);
        stream.Write(type, 0, 4);
        stream.Write(data, 0, data.Length);

        uint crc = 0xFFFFFFFFu;
        foreach (byte value in type)
            crc = UpdateCrc(crc, value);
        foreach (byte value in data)
            crc = UpdateCrc(crc, value);
        byte[] checksum = new byte[4];
        WriteBigEndian(checksum, 0, unchecked((int)(crc ^ 0xFFFFFFFFu)));
        stream.Write(checksum, 0, 4);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        return crc;
    }
}
'@

Add-Type -TypeDefinition $source
$outputPath = Join-Path $PSScriptRoot '..\Assets\Resources\RaceBarrier\T_HexGrid.png'
[RaceBarrierHexTexture]::Generate([System.IO.Path]::GetFullPath($outputPath))
