using System.Text.Json;
using System.Text.Json.Serialization;

namespace DgmCustomCourses;

internal sealed class CourseDefinition
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("baseCourse")]
    public string BaseCourse { get; init; } = "CanonPark";

    [JsonPropertyName("baseLayout")]
    public string BaseLayout { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("courseCardImage")]
    public string CourseCardImage { get; init; } = "";

    [JsonPropertyName("autoLayout")]
    public string AutoLayout { get; init; } = "";

    [JsonPropertyName("waterLandingClearanceMeters")]
    public float WaterLandingClearanceMeters { get; init; } = 4f;

    [JsonPropertyName("holes")]
    public List<HoleDefinition> Holes { get; init; } = new();

    [JsonPropertyName("mapScene")]
    public MapSceneDefinition MapScene { get; init; }

    [JsonPropertyName("mapWorldBounds")]
    public MapWorldBoundsDefinition MapWorldBounds { get; init; }

    [JsonPropertyName("mapLighting")]
    public MapLightingDefinition MapLighting { get; init; } = new();

    [JsonIgnore]
    public string SourceDirectory { get; private set; } = "";

    [JsonIgnore]
    public string PreviewFingerprint { get; private set; } = "";

    public static CourseDefinition Read(string path)
    {
        var source = File.ReadAllText(path);
        var definition = JsonSerializer.Deserialize<CourseDefinition>(source, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("The course file was empty.");

        definition.SourceDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        definition.Validate(path);
        definition.PreviewFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))).Substring(0, 16).ToLowerInvariant();
        return definition;
    }

    private void Validate(string path)
    {
        if (SchemaVersion is not (1 or 2))
            throw new InvalidDataException($"Unsupported schemaVersion {SchemaVersion}; this loader supports 1 and 2.");
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 48 || Id.Any(c => !((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c is '-' or '_')))
            throw new InvalidDataException("id must contain 1-48 ASCII letters, digits, hyphens, or underscores.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 64)
            throw new InvalidDataException("name must contain 1-64 characters.");
        if (Holes.Count is < 1 or > 18)
            throw new InvalidDataException("holes must contain between 1 and 18 entries.");
        if (string.IsNullOrWhiteSpace(BaseCourse))
            throw new InvalidDataException("baseCourse is required.");
        if (!string.IsNullOrWhiteSpace(AutoLayout) && !string.Equals(AutoLayout, "long", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("autoLayout must be empty or 'long'.");
        if (!string.IsNullOrWhiteSpace(AutoLayout) && (MapScene == null || Holes.Any(hole => hole.MapTeePosition != null || hole.MapBasketPosition != null)))
            throw new InvalidDataException("autoLayout requires an imported map with no explicit tee or basket positions.");
        if (!float.IsFinite(WaterLandingClearanceMeters) || WaterLandingClearanceMeters is < 0f or > 25f)
            throw new InvalidDataException("waterLandingClearanceMeters must be between 0 and 25 meters.");

        ValidateImageName(CourseCardImage, "courseCardImage");
        for (var i = 0; i < Holes.Count; i++)
        {
            var hole = Holes[i];
            if (hole.Par is < 1 or > 8)
                throw new InvalidDataException($"holes[{i}].par must be between 1 and 8.");
            if (hole.LengthMeters is < 1 or > 2000)
                throw new InvalidDataException($"holes[{i}].lengthMeters must be between 1 and 2000.");
            if (hole.ShotLimit is < 0 or > 20)
                throw new InvalidDataException($"holes[{i}].shotLimit must be 0 (no limit) or between 1 and 20.");

            hole.TeeOffset.Validate($"holes[{i}].teeOffset");
            hole.BasketOffset.Validate($"holes[{i}].basketOffset");
            if (!float.IsFinite(hole.TeeYawDegrees))
                throw new InvalidDataException($"holes[{i}].teeYawDegrees must be finite.");
            if (!float.IsFinite(hole.BasketYawDegrees))
                throw new InvalidDataException($"holes[{i}].basketYawDegrees must be finite.");
            ValidateImageName(hole.CardImage, $"holes[{i}].cardImage");
            if (hole.ObZones.Count > 12)
                throw new InvalidDataException($"holes[{i}].obZones may contain no more than 12 zones.");

            for (var zoneIndex = 0; zoneIndex < hole.ObZones.Count; zoneIndex++)
            {
                var zone = hole.ObZones[zoneIndex];
                if (zone.Points.Count is < 3 or > 64)
                    throw new InvalidDataException($"holes[{i}].obZones[{zoneIndex}].points must contain between 3 and 64 points.");
                foreach (var point in zone.Points)
                    point.Validate($"holes[{i}].obZones[{zoneIndex}].points");
            }

            hole.MapTeePosition?.Validate($"holes[{i}].mapTeePosition");
            hole.MapBasketPosition?.Validate($"holes[{i}].mapBasketPosition");
            if ((hole.MapTeePosition == null) != (hole.MapBasketPosition == null))
                throw new InvalidDataException($"holes[{i}] must provide both mapTeePosition and mapBasketPosition.");
        }

        if (MapScene != null)
        {
            MapScene.Validate();
            MapWorldBounds?.Validate();
            (MapLighting ?? new MapLightingDefinition()).Validate();
            var anyMapPositions = Holes.Any(hole => hole.MapTeePosition != null);
            if (anyMapPositions && Holes.Any(hole => hole.MapTeePosition == null))
                throw new InvalidDataException("Map hole positions must be supplied for every hole or omitted for every hole.");
        }
        else if (Holes.Any(hole => hole.MapTeePosition != null))
        {
            throw new InvalidDataException("mapTeePosition and mapBasketPosition require mapScene.");
        }
    }

    private static void ValidateImageName(string name, string field)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) ||
            !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{field} must be a PNG filename in the course folder.");
    }
}

internal sealed class HoleDefinition
{
    [JsonPropertyName("par")]
    public int Par { get; set; } = 3;

    [JsonPropertyName("lengthMeters")]
    public int LengthMeters { get; set; } = 60;

    [JsonPropertyName("pinVariant")]
    public string PinVariant { get; init; } = "Default";

    [JsonPropertyName("teeVariant")]
    public string TeeVariant { get; init; } = "Default";

    [JsonPropertyName("teeOffset")]
    public LocalOffset TeeOffset { get; init; } = new();

    [JsonPropertyName("basketOffset")]
    public LocalOffset BasketOffset { get; init; } = new();

    [JsonPropertyName("teeYawDegrees")]
    public float TeeYawDegrees { get; init; }

    [JsonPropertyName("basketYawDegrees")]
    public float BasketYawDegrees { get; init; }

    [JsonPropertyName("teeEnabled")]
    public bool TeeEnabled { get; init; } = true;

    [JsonPropertyName("basketEnabled")]
    public bool BasketEnabled { get; init; } = true;

    [JsonPropertyName("shotLimit")]
    public int ShotLimit { get; init; }

    [JsonPropertyName("cardImage")]
    public string CardImage { get; init; } = "";

    [JsonPropertyName("obZones")]
    public List<ObZoneDefinition> ObZones { get; init; } = new();

    [JsonPropertyName("mapTeePosition")]
    public MapWorldPosition MapTeePosition { get; init; }

    [JsonPropertyName("mapBasketPosition")]
    public MapWorldPosition MapBasketPosition { get; init; }
}

internal sealed class MapSceneDefinition
{
    [JsonPropertyName("scenePath")]
    public string ScenePath { get; init; } = "";

    [JsonPropertyName("bundlePath")]
    public string BundlePath { get; init; } = "";

    [JsonPropertyName("dependencyBundlePaths")]
    public List<string> DependencyBundlePaths { get; init; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ScenePath) || !ScenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("mapScene.scenePath must be the .unity path inside the AssetBundle.");
        ValidateRelativePath(ScenePath, "mapScene.scenePath");
        ValidateRelativePath(BundlePath, "mapScene.bundlePath");
        foreach (var dependency in DependencyBundlePaths)
            ValidateRelativePath(dependency, "mapScene.dependencyBundlePaths");
    }

    private static void ValidateRelativePath(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
            throw new InvalidDataException($"{name} must be a relative path.");

        var segments = value.Replace('\\', '/').Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"{name} cannot contain empty, '.' or '..' path segments.");
    }
}

internal sealed class MapWorldBoundsDefinition
{
    [JsonPropertyName("minX")]
    public float MinX { get; init; } = -100;

    [JsonPropertyName("maxX")]
    public float MaxX { get; init; } = 100;

    [JsonPropertyName("minZ")]
    public float MinZ { get; init; } = -100;

    [JsonPropertyName("maxZ")]
    public float MaxZ { get; init; } = 100;

    [JsonPropertyName("groundY")]
    public float GroundY { get; init; }

    public void Validate()
    {
        if (!float.IsFinite(MinX) || !float.IsFinite(MaxX) || !float.IsFinite(MinZ) || !float.IsFinite(MaxZ) || !float.IsFinite(GroundY) ||
            MaxX <= MinX || MaxZ <= MinZ || Math.Max(Math.Max(Math.Abs(MinX), Math.Abs(MaxX)), Math.Max(Math.Max(Math.Abs(MinZ), Math.Abs(MaxZ)), Math.Abs(GroundY))) > 10000)
            throw new InvalidDataException("mapWorldBounds must contain finite coordinates with maxX/maxZ greater than minX/minZ.");
    }
}

internal sealed class MapLightingDefinition
{
    [JsonPropertyName("preserveLightmaps")]
    public bool PreserveLightmaps { get; init; }

    [JsonPropertyName("supplementLighting")]
    public bool SupplementLighting { get; init; }

    [JsonPropertyName("forceFallbackMaterials")]
    public bool ForceFallbackMaterials { get; init; }

    [JsonPropertyName("forceUnlitMaterials")]
    public bool ForceUnlitMaterials { get; init; } = true;

    [JsonPropertyName("ignoreSourceTint")]
    public bool IgnoreSourceTint { get; init; }

    [JsonPropertyName("repairTerrainMaterials")]
    public bool RepairTerrainMaterials { get; init; }

    [JsonPropertyName("repairTerrainTextureDetails")]
    public bool RepairTerrainTextureDetails { get; init; }

    [JsonPropertyName("useBaseSkybox")]
    public bool UseBaseSkybox { get; init; }

    [JsonPropertyName("disableCameraOcclusionCulling")]
    public bool DisableCameraOcclusionCulling { get; init; }

    [JsonPropertyName("hideHoloWalls")]
    public bool HideHoloWalls { get; init; }

    [JsonPropertyName("forceTreeMeshLods")]
    public bool ForceTreeMeshLods { get; init; }

    [JsonPropertyName("exportFoliageDiagnostics")]
    public bool ExportFoliageDiagnostics { get; init; }

    [JsonPropertyName("useLitFoliageMaterials")]
    public bool UseLitFoliageMaterials { get; init; }

    [JsonPropertyName("realtimeMapShadows")]
    public bool RealtimeMapShadows { get; init; }

    [JsonPropertyName("hideImportedParticles")]
    public bool HideImportedParticles { get; init; }

    [JsonPropertyName("generateTreeCanopyColliders")]
    public bool GenerateTreeCanopyColliders { get; init; }

    [JsonPropertyName("experimentalFoliageInteractions")]
    public bool ExperimentalFoliageInteractions { get; init; }

    [JsonPropertyName("useGameFoliageShader")]
    public bool UseGameFoliageShader { get; init; }

    [JsonPropertyName("preferBaseCourseSun")]
    public bool PreferBaseCourseSun { get; init; }

    [JsonPropertyName("localLightIntensityMultiplier")]
    public float LocalLightIntensityMultiplier { get; init; } = 1f;

    [JsonPropertyName("albedoBrightness")]
    public float AlbedoBrightness { get; init; } = 1.5f;

    [JsonPropertyName("ambientColor")]
    public MapColorDefinition AmbientColor { get; init; } = new() { R = 0.75f, G = 0.75f, B = 0.78f };

    [JsonPropertyName("ambientIntensity")]
    public float AmbientIntensity { get; init; } = 1f;

    [JsonPropertyName("keyLightColor")]
    public MapColorDefinition KeyLightColor { get; init; } = new();

    [JsonPropertyName("keyLightIntensity")]
    public float KeyLightIntensity { get; init; } = 2.5f;

    [JsonPropertyName("keyLightYawDegrees")]
    public float KeyLightYawDegrees { get; init; } = -30f;

    [JsonPropertyName("keyLightPitchDegrees")]
    public float KeyLightPitchDegrees { get; init; } = 50f;

    public void Validate()
    {
        (AmbientColor ?? new MapColorDefinition()).Validate("mapLighting.ambientColor");
        (KeyLightColor ?? new MapColorDefinition()).Validate("mapLighting.keyLightColor");
        if (!float.IsFinite(AmbientIntensity) || AmbientIntensity is < 0 or > 2.5f)
            throw new InvalidDataException("mapLighting.ambientIntensity must be between 0 and 2.5.");
        if (!float.IsFinite(KeyLightIntensity) || KeyLightIntensity is < 0 or > 5)
            throw new InvalidDataException("mapLighting.keyLightIntensity must be between 0 and 5.");
        if (!float.IsFinite(LocalLightIntensityMultiplier) || LocalLightIntensityMultiplier is < 0 or > 5)
            throw new InvalidDataException("mapLighting.localLightIntensityMultiplier must be between 0 and 5.");
        if (!float.IsFinite(AlbedoBrightness) || AlbedoBrightness is < 0.5f or > 3f)
            throw new InvalidDataException("mapLighting.albedoBrightness must be between 0.5 and 3.");
        if (!float.IsFinite(KeyLightYawDegrees) || !float.IsFinite(KeyLightPitchDegrees) || KeyLightPitchDegrees is < -89 or > 89)
            throw new InvalidDataException("mapLighting light angles must be finite, with pitch between -89 and 89 degrees.");
    }
}

internal sealed class MapColorDefinition
{
    [JsonPropertyName("r")]
    public float R { get; init; } = 1f;

    [JsonPropertyName("g")]
    public float G { get; init; } = 1f;

    [JsonPropertyName("b")]
    public float B { get; init; } = 1f;

    public void Validate(string name)
    {
        if (!float.IsFinite(R) || !float.IsFinite(G) || !float.IsFinite(B) || R is < 0 or > 1 || G is < 0 or > 1 || B is < 0 or > 1)
            throw new InvalidDataException($"{name} color channels must be between 0 and 1.");
    }
}

internal sealed class MapWorldPosition
{
    [JsonPropertyName("x")]
    public float X { get; init; }

    [JsonPropertyName("y")]
    public float Y { get; init; }

    [JsonPropertyName("z")]
    public float Z { get; init; }

    public void Validate(string name)
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z))
            throw new InvalidDataException($"{name} values must be finite.");
        if (Math.Abs(X) > 10000 || Math.Abs(Y) > 10000 || Math.Abs(Z) > 10000)
            throw new InvalidDataException($"{name} values are outside the supported range.");
    }
}

internal sealed class ObZoneDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "OB";

    [JsonPropertyName("points")]
    public List<PlanPoint> Points { get; init; } = new();
}

internal sealed class PlanPoint
{
    [JsonPropertyName("right")]
    public float Right { get; init; }

    [JsonPropertyName("forward")]
    public float Forward { get; init; }

    public void Validate(string name)
    {
        if (!float.IsFinite(Right) || !float.IsFinite(Forward) || Math.Abs(Right) > 1000 || Math.Abs(Forward) > 1000)
            throw new InvalidDataException($"{name} coordinates must be finite and between -1000 and 1000 meters.");
    }
}

internal sealed class LocalOffset
{
    [JsonPropertyName("right")]
    public float Right { get; init; }

    [JsonPropertyName("up")]
    public float Up { get; init; }

    [JsonPropertyName("forward")]
    public float Forward { get; init; }

    public void Validate(string name)
    {
        if (!float.IsFinite(Right) || !float.IsFinite(Up) || !float.IsFinite(Forward))
            throw new InvalidDataException($"{name} values must be finite.");
        if (Math.Abs(Right) > 2000 || Math.Abs(Up) > 200 || Math.Abs(Forward) > 2000)
            throw new InvalidDataException($"{name} values are outside the supported range.");
    }
}
