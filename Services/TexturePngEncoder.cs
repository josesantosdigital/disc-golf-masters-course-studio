using System.IO.Compression;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DgmCustomCourses;

internal static class TexturePngEncoder
{
    private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static byte[] Encode(Texture source, int maxDimension = 1280)
    {
        if (source == null || source.width < 1 || source.height < 1)
            return null;

        var scale = Math.Min(1f, maxDimension / (float)Math.Max(source.width, source.height));
        var width = Math.Max(1, Mathf.RoundToInt(source.width * scale));
        var height = Math.Max(1, Mathf.RoundToInt(source.height * scale));
        RenderTexture target = null;
        Texture2D readableCopy = null;
        var previousTarget = RenderTexture.active;
        try
        {
            target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            readableCopy = new Texture2D(width, height, TextureFormat.RGBA32, false);
            readableCopy.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            readableCopy.Apply(false, false);
            return EncodeRgba(width, height, readableCopy.GetPixels32());
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (target != null)
                RenderTexture.ReleaseTemporary(target);
            if (readableCopy != null)
                UnityEngine.Object.Destroy(readableCopy);
        }
    }

    private static byte[] EncodeRgba(int width, int height, Il2CppStructArray<Color32> pixels)
    {
        if (pixels == null || pixels.Length != width * height)
            throw new InvalidDataException("The readable preview texture returned an unexpected pixel count.");

        using var raw = new MemoryStream((width * 4 + 1) * height);
        for (var y = height - 1; y >= 0; y--)
        {
            raw.WriteByte(0);
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[y * width + x];
                raw.WriteByte(pixel.r);
                raw.WriteByte(pixel.g);
                raw.WriteByte(pixel.b);
                raw.WriteByte(pixel.a);
            }
        }

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, System.IO.Compression.CompressionLevel.Fastest, true))
            {
                raw.Position = 0;
                raw.CopyTo(zlib);
            }
            compressed = compressedStream.ToArray();
        }

        using var png = new MemoryStream(compressed.Length + 64);
        png.Write(PngSignature, 0, PngSignature.Length);

        var header = new byte[13];
        WriteUInt32(header, 0, (uint)width);
        WriteUInt32(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed);
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        WriteUInt32(output, (uint)data.Length);
        output.Write(typeBytes, 0, typeBytes.Length);
        output.Write(data, 0, data.Length);

        var crc = 0xffffffffu;
        UpdateCrc(ref crc, typeBytes);
        UpdateCrc(ref crc, data);
        WriteUInt32(output, ~crc);
    }

    private static void UpdateCrc(ref uint crc, byte[] bytes)
    {
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
        }
    }

    private static void WriteUInt32(byte[] output, int offset, uint value)
    {
        output[offset] = (byte)(value >> 24);
        output[offset + 1] = (byte)(value >> 16);
        output[offset + 2] = (byte)(value >> 8);
        output[offset + 3] = (byte)value;
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        output.WriteByte((byte)(value >> 24));
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }
}
