namespace DotBoxD.Abstractions;

/// <summary>Excludes a public method from its containing type's <see cref="HostBindingObjectAttribute"/> surface.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class HostBindingIgnoreAttribute : Attribute;
