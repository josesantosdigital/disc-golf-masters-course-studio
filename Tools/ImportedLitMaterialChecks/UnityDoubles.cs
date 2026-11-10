namespace UnityEngine;

public readonly record struct Color(float r, float g, float b, float a)
{
    public static Color black => new(0, 0, 0, 1);
}

// Minimal stateful doubles. In particular shader assignment clears keyword state,
// and CopyPropertiesFromMaterial deliberately does not copy shader/override tags.
public class Object
{
    private static int nextId;
    private readonly int id = ++nextId;
    public string name { get; set; }
    public int GetInstanceID() => id;
    public static void Destroy(Object value)
    {
        if (value is Material { IsClone: true } material)
        {
            Material.LiveClones--;
            material.IsClone = false;
        }
    }
}
public class Shader : Object
{
    public bool isSupported = true;
    internal bool Native;
    public Shader(string name, bool native = false) { this.name = name; Native = native; }
}
public class Material : Object
{
    public static int LiveClones;
    internal bool IsClone;
    private Shader currentShader;
    private HashSet<string> keywords = new();
    private Dictionary<string, string> tags = new();
    public Dictionary<string, object> Properties = new();
    public bool FailNextNativeCopy;
    public int renderQueue = 2000;
    public Shader shader
    {
        get => currentShader;
        set { currentShader = value; keywords.Clear(); renderQueue = 2000; }
    }
    public string[] shaderKeywords { get => keywords.ToArray(); set => keywords = new(value); }
    public Material(Shader shader) { this.shader = shader; }
    public Material(Material source)
    {
        shader = source.shader;
        CopyPropertiesFromMaterial(source);
        tags = new(source.tags);
        name = source.name;
        IsClone = true;
        LiveClones++;
    }
    public bool HasProperty(string property) =>
        property is "_WorkflowMode" or "_SrcBlendAlpha" or "_DstBlendAlpha" ? shader.Native :
        property == "_DetailMap" ? !shader.Native : true;
    public float GetFloat(string property) => Properties.TryGetValue(property, out var value) ? (float)value : 0f;
    public void SetFloat(string property, float value) => Properties[property] = value;
    public Color GetColor(string property) => Properties.TryGetValue(property, out var value) && value is Color color ? color : default;
    public void SetColor(string property, Color value) => Properties[property] = value;
    public object GetTexture(string property) => Properties.TryGetValue(property, out var value) ? value : null;
    public void SetTexture(string property, object value) => Properties[property] = value;
    public void EnableKeyword(string value) => keywords.Add(value);
    public void DisableKeyword(string value) => keywords.Remove(value);
    public bool IsKeywordEnabled(string value) => keywords.Contains(value);
    public string GetTag(string tag, bool searchFallbacks, string defaultValue) => tags.TryGetValue(tag, out var value) ? value : defaultValue;
    public void SetOverrideTag(string tag, string value) => tags[tag] = value;
    public void CopyPropertiesFromMaterial(Material source)
    {
        if (shader.Native && FailNextNativeCopy)
        {
            FailNextNativeCopy = false;
            throw new Exception("synthetic copy failure");
        }
        Properties = new(source.Properties);
        keywords = new(source.keywords);
        renderQueue = source.renderQueue;
    }
}
