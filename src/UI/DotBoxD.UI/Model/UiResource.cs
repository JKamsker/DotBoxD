namespace DotBoxD.UI;

/// <summary>Opaque host-granted image identity. Never a URL, path or executable resource.</summary>
public sealed record UiResource(int Id, string Handle);
