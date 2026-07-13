using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Abstractions;

/// <summary>
/// Exposes eligible public instance methods declared by an SDK value object as receiver-forwarding host
/// bindings. Binding ids derive from <see cref="BindingPrefix"/>, the method name, and parameter types;
/// methods inherit the default capability and effects unless they declare <see cref="HostBindingAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class HostBindingObjectAttribute : Attribute
{
    public HostBindingObjectAttribute(
        string bindingPrefix,
        string defaultCapability,
        SandboxEffect defaultEffects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCapability);

        BindingPrefix = bindingPrefix;
        DefaultCapability = defaultCapability;
        DefaultEffects = defaultEffects;
    }

    /// <summary>The prefix used for derived binding ids.</summary>
    public string BindingPrefix { get; }

    /// <summary>The capability inherited by methods without a binding override.</summary>
    public string DefaultCapability { get; }

    /// <summary>The effects inherited by methods without a binding override.</summary>
    public SandboxEffect DefaultEffects { get; }
}
