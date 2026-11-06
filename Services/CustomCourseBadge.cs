using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DgmCustomCourses;

// A small disc-and-basket emblem, shared by custom course entries.
internal static class CustomCourseBadge
{
    private static Sprite _sprite;

    internal static Sprite GetSprite()
    {
        if (_sprite != null) return _sprite;
        const int size = 128;
        var pixels = new Il2CppStructArray<Color32>(size * size);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = x - 63.5f;
            var dy = y - 63.5f;
            var radius = MathF.Sqrt(dx * dx + dy * dy);
            var color = radius > 62f ? new Color32(0, 0, 0, 0)
                : radius > 58f ? new Color32(245, 247, 235, 255)
                : radius > 55f ? new Color32(44, 109, 92, 255)
                : new Color32(15, 56, 52, 255);

            // Golden flying disc and a white basket silhouette.
            if ((dx * dx) / (34f * 34f) + ((y - 91f) * (y - 91f)) / 20f <= 1f)
                color = new Color32(245, 182, 65, 255);
            if ((y >= 75 && y <= 79 && MathF.Abs(dx) <= 23) ||
                (y >= 51 && y <= 73 && (MathF.Abs(dx) <= 1.5f || MathF.Abs(dx - 10f) <= 1.2f || MathF.Abs(dx + 10f) <= 1.2f)) ||
                (y >= 43 && y <= 50 && MathF.Abs(dx) <= 23 - (50 - y)) ||
                (y >= 29 && y < 43 && MathF.Abs(dx) <= 2) ||
                (y >= 27 && y <= 29 && MathF.Abs(dx) <= 12))
                color = new Color32(248, 249, 241, 255);
            pixels[y * size + x] = color;
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "DGM_CustomCourse_Badge",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        _sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _sprite;
    }
}
