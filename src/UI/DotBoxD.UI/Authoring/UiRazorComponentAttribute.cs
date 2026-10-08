namespace DotBoxD.UI.Authoring;

/// <summary>Opt-in worker-side safe Razor composition over the public UiBuilder API.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class UiRazorComponentAttribute(string path) : Attribute
{
    public string Path { get; } = path;
    public bool GenerateRender { get; set; } = true;
}
