namespace DotBoxD.Abstractions;

/// <summary>Excludes a method from explicit, automatic, and <see cref="HostBindingObjectAttribute"/> host binding resolution.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class HostBindingIgnoreAttribute : Attribute;
