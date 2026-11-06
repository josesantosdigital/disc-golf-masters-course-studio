using BepInEx;
using UnityEngine;

namespace DgmCustomCourses;

// Opt-in, one-time captures of the source texture selected for an imported foliage material.
// They help distinguish a missing alpha mask from an incompatible shader variant.
internal static class FoliageDiagnostics
{
    public static void Capture(string courseName, Material material, Texture texture)
    {
        try
        {
            var image = TexturePngEncoder.Encode(texture, 2048);
            if (image == null)
                return;
            var directory = Path.Combine(Paths.PluginPath, "CustomCourseLoader", "Diagnostics", "Foliage", SafeName(courseName));
            Directory.CreateDirectory(directory);
            var name = SafeName(material.name) + "-" + SafeName(texture.name) + ".png";
            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, image);
            Plugin.LogSource.LogInfo($"Captured foliage texture for course '{courseName}': texture='{texture.name}' ({texture.width}x{texture.height}), material='{material.name}', path='{path}'.");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not capture foliage texture for course '{courseName}' (texture='{texture?.name ?? "<null>"}'): {ex.Message}");
        }
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (name ?? "unnamed").Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
