namespace DotBoxD.Services.Attributes;

/// <summary>
/// Marks a method as a DotBoxD endpoint. This attribute is optional -
/// all methods in a [DotBoxDService] interface are included by default.
/// Use this attribute to customize method behavior.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DotBoxDMethodAttribute : Attribute
{
    /// <summary>
    /// Optional custom method name. If not specified, the method name is used.
    /// </summary>
    public string? Name { get; set; }
}
