using System.Text;
using DgmCustomCourses;

var path = Path.Combine(Path.GetTempPath(), "dgm-preview-check-" + Guid.NewGuid().ToString("N") + ".json");
const string original = "{\"schemaVersion\":2,\"id\":\"sample\",\"name\":\"Sample\",\"baseCourse\":\"CanonPark\",\"holes\":[{\"par\":3,\"lengthMeters\":60}]}";
try
{
    File.WriteAllText(path, original, new UTF8Encoding(false));
    var first = CourseDefinition.Read(path);
    if (first.PreviewFingerprint.Length != 16) throw new Exception("Fingerprint is missing.");
    File.WriteAllText(path, original, new UTF8Encoding(true));
    var withBom = CourseDefinition.Read(path);
    if (first.PreviewFingerprint != withBom.PreviewFingerprint) throw new Exception("A UTF-8 BOM changed the fingerprint.");
    File.WriteAllText(path, original.Replace("Sample", "Sample Changed"), new UTF8Encoding(false));
    var changed = CourseDefinition.Read(path);
    if (changed.PreviewFingerprint == first.PreviewFingerprint) throw new Exception("A course edit did not invalidate its preview.");
    const string imported = "{\"schemaVersion\":2,\"id\":\"sample\",\"name\":\"Sample\",\"baseCourse\":\"CanonPark\",\"autoLayout\":\"long\",\"layoutGroundColliderExclusions\":[\"Terrain\"],\"mapScene\":{\"scenePath\":\"Assets/Level.unity\",\"bundlePath\":\"Maps/sample/scene.bundle\"},\"holes\":[{\"par\":4,\"lengthMeters\":200}]}";
    File.WriteAllText(path, imported, new UTF8Encoding(false));
    var importedDefinition = CourseDefinition.Read(path);
    if (importedDefinition.AutoLayout != "long") throw new Exception("Long imported layout was not loaded.");
    if (importedDefinition.LayoutGroundColliderExclusions.Count != 1 || importedDefinition.LayoutGroundColliderExclusions[0] != "Terrain")
        throw new Exception("Auto-layout ground exclusions were not loaded.");
    var invalidExclusions = imported.Replace("\"Terrain\"", "\"a\",\"b\",\"c\",\"d\",\"e\",\"f\",\"g\",\"h\",\"i\"");
    File.WriteAllText(path, invalidExclusions, new UTF8Encoding(false));
    var invalidExclusionsRejected = false;
    try { CourseDefinition.Read(path); } catch (InvalidDataException) { invalidExclusionsRejected = true; }
    if (!invalidExclusionsRejected) throw new Exception("An oversized auto-layout ground exclusion list was accepted.");
    File.WriteAllText(path, imported, new UTF8Encoding(false));
    File.WriteAllText(path, imported.Replace("\"par\":4", "\"mapTeePosition\":{\"x\":0,\"y\":0,\"z\":0},\"par\":4"), new UTF8Encoding(false));
    var rejected = false;
    try { CourseDefinition.Read(path); } catch (InvalidDataException) { rejected = true; }
    if (!rejected) throw new Exception("Automatic and explicit positions were not rejected.");
    Console.WriteLine("Course preview checks passed: UTF-8/BOM loading, cache invalidation, automatic-layout validation, and ground exclusion parsing.");
}
finally { if (File.Exists(path)) File.Delete(path); }
