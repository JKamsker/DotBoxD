// netstandard2.1 predates this attribute. The linker recognizes its full name in referenced libraries.
namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method, Inherited = false)]
internal sealed class RequiresUnreferencedCodeAttribute(string message) : Attribute
{
    public string Message { get; } = message;
}
