using System.Text.Json;
using Dgm;
using UnityEngine;

namespace DgmCustomCourses;

internal static class CourseCatalogExporter
{
    private static string _lastSignature = "";
    private static bool _textureExportWarningLogged;

    public static void ExportBuiltInCourses(Il2CppSystem.Collections.Generic.List<CourseLayout> layouts)
    {
        if (layouts == null || layouts.Count == 0)
            return;

        var builtIn = new List<CourseLayout>();
        var signatureParts = new List<string>();
        for (var i = 0; i < layouts.Count; i++)
        {
            var layout = layouts[i];
            if (layout == null || !Enum.IsDefined(typeof(CourseLayout.CourseID), layout.courseID))
                continue;
            builtIn.Add(layout);
            var mapTexture = layout.layoutPreviewImageTexture ?? layout.GetLayoutPreviewImage()?.texture;
            var courseTexture = layout.coursePreviewImageTexture ?? layout.GetCoursePreviewImage()?.texture;
            signatureParts.Add($"{layout.courseID}:{layout.layoutID}:{layout.displayName}:{layout.coursePar}:{layout.courseLength}:{mapTexture?.GetInstanceID()}:{courseTexture?.GetInstanceID()}");
        }

        var signature = string.Join("|", signatureParts.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        if (signature.Length == 0 || string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            return;

        try
        {
            var editorDirectory = Path.Combine(BepInEx.Paths.PluginPath, "CustomCourseLoader", "CourseEditor");
            var mapDirectory = Path.Combine(editorDirectory, "assets", "maps");
            Directory.CreateDirectory(mapDirectory);

            var courses = builtIn
                .GroupBy(layout => layout.courseID.ToString(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    id = group.Key,
                    name = GetCourseDisplayName(group.Key),
                    layouts = group.Select(layout => new
                    {
                        id = layout.layoutID ?? "",
                        name = string.IsNullOrWhiteSpace(layout.displayName) ? layout.layoutID ?? "Layout" : layout.displayName,
                        sceneName = layout.sceneName ?? "",
                        par = layout.coursePar,
                        lengthMeters = layout.courseLength,
                        difficulty = "Standard",
                        mapImage = SaveTexture(layout.layoutPreviewImageTexture ?? layout.GetLayoutPreviewImage()?.texture, mapDirectory, group.Key, layout.layoutID, "layout"),
                        cardImage = SaveTexture(layout.coursePreviewImageTexture ?? layout.GetCoursePreviewImage()?.texture, mapDirectory, group.Key, layout.layoutID, "course"),
                        holes = GetHoles(layout.holePinVariants)
                    }).ToArray()
                })
                .ToArray();

            var json = JsonSerializer.Serialize(new { version = 1, courses }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(editorDirectory, "catalog.js"), $"window.DGM_BUILTIN_COURSES = {json};\n");
            _lastSignature = signature;
            Plugin.LogSource.LogInfo($"Exported {courses.Length} built-in course maps for the Course Editor.");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not export built-in course maps for the Course Editor: {ex}");
        }
    }

    private static string GetCourseDisplayName(string courseId) => courseId switch
    {
        "CanonPark" => "Canon Park",
        "MinokaSprings" => "Minoka Springs",
        "BaldurDgc" => "Baldur Disc Golf Club",
        "MapleHill" => "Maple Hill",
        "ChampionsLanding" => "Champions Landing",
        "WinthropGold" => "Winthrop Gold",
        "NokiaDiscGolfPark" => "Nokia Disc Golf Park",
        "Terminalen" => "Terminalen",
        _ => courseId
    };

    private static object[] GetHoles(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<CourseManager.PinVariantListEntry> entries)
    {
        if (entries == null)
            return Array.Empty<object>();

        var holes = new List<object>();
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry == null)
                continue;
            holes.Add(new
            {
                number = i + 1,
                name = entry.name,
                par = entry.par,
                lengthMeters = entry.holeLength,
                pinVariant = entry.pinVariant.ToString(),
                teeVariant = entry.teeVariant.ToString()
            });
        }
        return holes.ToArray();
    }

    private static string SaveTexture(Texture texture, string mapDirectory, string courseId, string layoutId, string type)
    {
        if (texture == null)
            return "";

        try
        {
            var stem = string.Join("-", new[] { courseId, layoutId, type }.Select(SafePart).Where(value => value.Length > 0));
            var fileName = $"{stem}.png";
            var path = Path.Combine(mapDirectory, fileName);
            var encoded = TexturePngEncoder.Encode(texture);
            if (encoded == null || encoded.Length == 0)
                return "";

            var bytes = new byte[encoded.Length];
            for (var i = 0; i < encoded.Length; i++)
                bytes[i] = encoded[i];
            File.WriteAllBytes(path, bytes);
            return $"assets/maps/{fileName}";
        }
        catch (Exception ex)
        {
            if (!_textureExportWarningLogged)
            {
                _textureExportWarningLogged = true;
                Plugin.LogSource.LogWarning($"Course Studio couldn't export one or more built-in preview textures: {ex.Message}. Course and hole metadata will still be available.");
            }
            return "";
        }
    }

    private static string SafePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var chars = value.ToLowerInvariant().Select(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' ? character : '-').ToArray();
        var result = new string(chars).Trim('-');
        while (result.Contains("--", StringComparison.Ordinal))
            result = result.Replace("--", "-", StringComparison.Ordinal);
        return result;
    }
}
