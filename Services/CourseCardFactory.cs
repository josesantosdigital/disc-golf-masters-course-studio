using System.Buffers.Binary;
using System.IO.Compression;
using BepInEx.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DgmCustomCourses;

internal sealed class CourseCardFactory
{
    private readonly ManualLogSource _log;
    private readonly Dictionary<string, Texture2D> _courseCardTextures = new(StringComparer.OrdinalIgnoreCase);

    public CourseCardFactory(ManualLogSource log)
    {
        _log = log;
    }
    public Texture2D LoadCourseCard(CourseDefinition definition, string imageName)
    {
        var cacheKey = definition.Id;
        if (_courseCardTextures.TryGetValue(cacheKey, out var existing))
            return existing;

        try
        {
            var previewPath = CourseSceneCardCapture.GetPath(definition);
            if (File.Exists(previewPath))
            {
                try
                {
                    var preview = LoadPngCourseCard(previewPath, definition.Id);
                    _courseCardTextures[cacheKey] = preview;
                    _log.LogInfo($"Loaded rendered course preview for '{definition.Name}'.");
                    return preview;
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"Could not read rendered course preview for '{definition.Name}'; using its existing card: {ex.Message}");
                }
            }
            var imagePath = string.IsNullOrWhiteSpace(imageName)
                ? ""
                : Path.Combine(definition.SourceDirectory, imageName);
            Texture2D texture;
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                texture = LoadPngCourseCard(imagePath, definition.Id);
                _log.LogInfo($"Loaded course card image for '{definition.Name}'.");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(imagePath))
                    _log.LogWarning($"Course card image '{imageName}' was not found; generating a fallback card.");
                texture = CreateCourseCard(definition);
            }
            _courseCardTextures[cacheKey] = texture;
            return texture;
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Could not generate a course card for '{definition.Name}': {ex}");
            return null;
        }
    }

    internal void RememberRenderedCard(CourseDefinition definition, Texture2D texture) => _courseCardTextures[definition.Id] = texture;

    internal static Texture2D LoadPngCourseCard(string imagePath, string courseId, int maxDimension = 960)
    {
        var file = File.ReadAllBytes(imagePath);
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (file.Length < 33 || !file.Take(signature.Length).SequenceEqual(signature))
            throw new InvalidDataException("The course card is not a valid PNG file.");

        var width = 0;
        var height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        byte interlace = 0;
        byte[] palette = null;
        byte[] transparency = null;
        using var compressed = new MemoryStream();

        var offset = 8;
        while (offset + 12 <= file.Length)
        {
            var chunkLength = ReadPngInt32(file, offset);
            if (chunkLength < 0 || (long)offset + chunkLength + 12 > file.Length)
                throw new InvalidDataException("A PNG chunk has an invalid length.");

            var chunkType = System.Text.Encoding.ASCII.GetString(file, offset + 4, 4);
            var dataOffset = offset + 8;
            switch (chunkType)
            {
                case "IHDR":
                    width = ReadPngInt32(file, dataOffset);
                    height = ReadPngInt32(file, dataOffset + 4);
                    bitDepth = file[dataOffset + 8];
                    colorType = file[dataOffset + 9];
                    interlace = file[dataOffset + 12];
                    break;
                case "PLTE":
                    palette = file.Skip(dataOffset).Take(chunkLength).ToArray();
                    break;
                case "tRNS":
                    transparency = file.Skip(dataOffset).Take(chunkLength).ToArray();
                    break;
                case "IDAT":
                    compressed.Write(file, dataOffset, chunkLength);
                    break;
                case "IEND":
                    offset = file.Length;
                    continue;
            }
            offset += chunkLength + 12;
        }

        if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 64_000_000)
            throw new InvalidDataException("The course card dimensions are outside the supported range.");
        if (bitDepth != 8 || interlace != 0)
            throw new InvalidDataException("Course card PNGs must use 8-bit, non-interlaced pixels.");

        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException($"PNG color type {colorType} is unsupported.")
        };
        if (colorType == 3 && (palette == null || palette.Length % 3 != 0))
            throw new InvalidDataException("The indexed PNG has no valid color palette.");

        var rowBytes = checked(width * channels);
        var encodedBytes = new byte[checked((rowBytes + 1) * height)];
        compressed.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress, true))
        {
            var read = 0;
            while (read < encodedBytes.Length)
            {
                var amount = zlib.Read(encodedBytes, read, encodedBytes.Length - read);
                if (amount == 0)
                    break;
                read += amount;
            }
            if (read != encodedBytes.Length || zlib.ReadByte() != -1)
                throw new InvalidDataException("The PNG pixel data length does not match its dimensions.");
        }

        var pixels = new byte[checked(rowBytes * height)];
        for (var y = 0; y < height; y++)
        {
            var inputOffset = y * (rowBytes + 1);
            var filter = encodedBytes[inputOffset++];
            var outputOffset = y * rowBytes;
            for (var x = 0; x < rowBytes; x++)
            {
                var raw = encodedBytes[inputOffset + x];
                var left = x >= channels ? pixels[outputOffset + x - channels] : 0;
                var up = y > 0 ? pixels[outputOffset + x - rowBytes] : 0;
                var upperLeft = y > 0 && x >= channels ? pixels[outputOffset + x - rowBytes - channels] : 0;
                var predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => PaethPredictor(left, up, upperLeft),
                    _ => throw new InvalidDataException($"PNG filter type {filter} is unsupported.")
                };
                pixels[outputOffset + x] = unchecked((byte)(raw + predictor));
            }
        }

        var scale = maxDimension > 0 ? Math.Min(1d, (double)maxDimension / Math.Max(width, height)) : 0d;
        var targetWidth = maxDimension > 0 ? Math.Max(1, (int)Math.Round(width * scale)) : 96;
        var targetHeight = maxDimension > 0 ? Math.Max(1, (int)Math.Round(height * scale)) : 54;
        var output = new Il2CppStructArray<Color32>(targetWidth * targetHeight);
        for (var y = 0; y < targetHeight; y++)
        for (var x = 0; x < targetWidth; x++)
        {
            var sourceX = Math.Min(width - 1, x * width / targetWidth);
            var sourceY = Math.Min(height - 1, (targetHeight - 1 - y) * height / targetHeight);
            var pixelOffset = sourceY * rowBytes + sourceX * channels;
            byte red;
            byte green;
            byte blue;
            byte alpha;
            switch (colorType)
            {
                case 0:
                    red = green = blue = pixels[pixelOffset];
                    alpha = 255;
                    break;
                case 2:
                    red = pixels[pixelOffset];
                    green = pixels[pixelOffset + 1];
                    blue = pixels[pixelOffset + 2];
                    alpha = 255;
                    break;
                case 3:
                    var paletteOffset = pixels[pixelOffset] * 3;
                    if (paletteOffset + 2 >= palette.Length)
                        throw new InvalidDataException("A PNG pixel refers outside its color palette.");
                    red = palette[paletteOffset];
                    green = palette[paletteOffset + 1];
                    blue = palette[paletteOffset + 2];
                    alpha = transparency != null && pixels[pixelOffset] < transparency.Length ? transparency[pixels[pixelOffset]] : (byte)255;
                    break;
                case 4:
                    red = green = blue = pixels[pixelOffset];
                    alpha = pixels[pixelOffset + 1];
                    break;
                default:
                    red = pixels[pixelOffset];
                    green = pixels[pixelOffset + 1];
                    blue = pixels[pixelOffset + 2];
                    alpha = pixels[pixelOffset + 3];
                    break;
            }
            output[y * targetWidth + x] = new Color32(red, green, blue, alpha);
        }
        var texture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false)
        {
            name = $"DGM_CourseCard_{courseId}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(output);
        texture.Apply(false, false);
        return texture;
    }

    private static int ReadPngInt32(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));

    private static int PaethPredictor(int left, int up, int upperLeft)
    {
        var estimate = left + up - upperLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upperLeftDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= upDistance && leftDistance <= upperLeftDistance ? left : upDistance <= upperLeftDistance ? up : upperLeft;
    }

    private static Texture2D CreateCourseCard(CourseDefinition definition)
    {
        const int width = 96;
        const int height = 54;
        var id = definition.Id.ToLowerInvariant();
        var seed = id.Aggregate(17, (value, character) => unchecked(value * 31 + character)) & int.MaxValue;
        var urban = id.Contains("hood", StringComparison.Ordinal) || id.Contains("city", StringComparison.Ordinal);
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = $"DGM_CourseCard_{definition.Id}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        for (var x = 0; x < width; x++)
        {
            var ridge = 32 + (int)(Math.Sin((x + seed % 23) * 0.16) * 5 + Math.Sin((x + seed % 13) * 0.07) * 3);
            for (var y = 0; y < height; y++)
            {
                Color color;
                if (urban)
                {
                    if (y >= 31)
                    {
                        var t = (y - 31) / (float)(height - 31);
                        color = Color.Lerp(new Color(0.16f, 0.20f, 0.34f), new Color(0.94f, 0.43f, 0.20f), t);
                    }
                    else if (y >= 13)
                        color = new Color(0.16f, 0.18f, 0.23f);
                    else
                        color = new Color(0.27f, 0.25f, 0.23f);
                }
                else if (y >= ridge)
                {
                    var t = (y - ridge) / (float)(height - ridge);
                    color = Color.Lerp(new Color(0.30f, 0.59f, 0.80f), new Color(0.76f, 0.88f, 0.94f), t);
                }
                else if (y >= 18)
                    color = new Color(0.16f, 0.36f, 0.22f);
                else
                {
                    color = new Color(0.19f, 0.43f, 0.23f);
                    var creekCenter = 8 + (int)(Math.Sin((x + seed % 19) * 0.12) * 3);
                    if (x > 5 && x < 90 && Math.Abs(y - creekCenter) <= 2)
                        color = new Color(0.10f, 0.63f, 0.78f);
                }

                texture.SetPixel(x, y, color);
            }
        }

        if (urban)
        {
            for (var building = 0; building < 8; building++)
            {
                var buildingWidth = 5 + building % 3;
                var left = building * 13 - 2;
                var top = 25 + (building * 17 + seed % 19) % 23;
                for (var x = Math.Max(0, left); x < Math.Min(width, left + buildingWidth); x++)
                for (var y = 14; y <= top; y++)
                {
                    var facade = (building % 2 == 0) ? new Color(0.12f, 0.16f, 0.23f) : new Color(0.20f, 0.22f, 0.29f);
                    if (y > 17 && (x + seed) % 4 == 1 && y % 6 < 3)
                        facade = new Color(1.0f, 0.61f, 0.27f);
                    texture.SetPixel(x, y, facade);
                }
            }

            for (var x = 4; x < width; x += 22)
                for (var y = 4; y < 7; y++)
                    texture.SetPixel(x, y, new Color(0.96f, 0.76f, 0.30f));
        }
        else
        {
            for (var tree = 0; tree < 11; tree++)
            {
                var centerX = 4 + (seed / (tree + 3) + tree * 29) % (width - 8);
                var baseY = 16 + (seed / (tree + 5) + tree * 11) % 12;
                var treeHeight = 7 + tree % 5;
                for (var dy = 0; dy < treeHeight; dy++)
                {
                    var halfWidth = dy / 2;
                    for (var dx = -halfWidth; dx <= halfWidth; dx++)
                    {
                        var x = centerX + dx;
                        var y = baseY + dy;
                        if (x >= 0 && x < width && y < height)
                            texture.SetPixel(x, y, new Color(0.07f, 0.23f + (tree % 3) * 0.04f, 0.11f));
                    }
                }
                texture.SetPixel(centerX, baseY - 2, new Color(0.30f, 0.19f, 0.10f));
                texture.SetPixel(centerX, baseY - 1, new Color(0.30f, 0.19f, 0.10f));
            }
        }

        var basketX = width - 13;
        for (var y = 5; y <= 23; y++)
            texture.SetPixel(basketX, y, new Color(0.99f, 0.79f, 0.21f));
        for (var dx = -3; dx <= 3; dx++)
        {
            texture.SetPixel(basketX + dx, 21, new Color(0.99f, 0.87f, 0.43f));
            texture.SetPixel(basketX + dx, 22, new Color(0.99f, 0.87f, 0.43f));
        }

        texture.Apply(false, false);
        return texture;
    }

}
