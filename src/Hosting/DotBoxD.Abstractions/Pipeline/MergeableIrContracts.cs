using DotBoxD.Kernels;

namespace DotBoxD.Abstractions;

/// <summary>
/// Describes the pipeline semantics of a delegate argument lowered into a mergeable IR step.
/// </summary>
public enum LoweredPipelineStepKind
{
    Filter,
    Projection,
}

/// <summary>
/// Marks a delegate parameter whose lambda body should be lowered into a mergeable IR step.
/// The method must expose a sibling overload accepting <see cref="LoweredPipelineStep"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class LowerToIrAttribute(LoweredPipelineStepKind kind) : Attribute
{
    public LoweredPipelineStepKind Kind { get; } = kind;
}

/// <summary>
/// Describes method-level source-generator lowering into verified IR.
/// </summary>
public enum LoweredIrMethodKind
{
    AnonymousInvocation,
}

/// <summary>
/// Marks a method whose lambda argument should be lowered into a generated anonymous IR package.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class LowerToIrMethodAttribute(LoweredIrMethodKind kind) : Attribute
{
    public LoweredIrMethodKind Kind { get; } = kind;
}

/// <summary>
/// Experimental public carrier for one lowered pipeline stage. A later composer can merge ordered
/// steps into a complete <see cref="SandboxModule"/>.
/// </summary>
public sealed record LoweredPipelineStep(
    LoweredPipelineStepKind Kind,
    string InputType,
    string OutputType,
    IReadOnlyList<Parameter> Parameters,
    IReadOnlyList<Statement> Prefix,
    Expression Value,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> Effects)
{
    private IReadOnlyList<Parameter> _parameters = CopyList(Parameters);
    private IReadOnlyList<Statement> _prefix = CopyList(Prefix);
    private IReadOnlyList<string> _requiredCapabilities = CopyList(RequiredCapabilities);
    private IReadOnlyList<string> _effects = CopyList(Effects);

    public IReadOnlyList<Parameter> Parameters
    {
        get => _parameters;
        init => _parameters = CopyList(value);
    }

    public IReadOnlyList<Statement> Prefix
    {
        get => _prefix;
        init => _prefix = CopyList(value);
    }

    public IReadOnlyList<string> RequiredCapabilities
    {
        get => _requiredCapabilities;
        init => _requiredCapabilities = CopyList(value);
    }

    public IReadOnlyList<string> Effects
    {
        get => _effects;
        init => _effects = CopyList(value);
    }

    private static IReadOnlyList<T> CopyList<T>(IEnumerable<T> values)
        => values is T[] array
            ? Array.AsReadOnly((T[])array.Clone())
            : Array.AsReadOnly(values.ToArray());
}
