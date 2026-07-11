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
public sealed class LowerToIrAttribute : Attribute
{
    public LowerToIrAttribute(LoweredPipelineStepKind kind)
        => Kind = ValidateStepKind(kind, nameof(kind));

    public LoweredPipelineStepKind Kind { get; }

    private static LoweredPipelineStepKind ValidateStepKind(
        LoweredPipelineStepKind kind,
        string parameterName)
        => Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(parameterName, kind, "Unsupported lowered pipeline step kind.");
}

/// <summary>
/// Marks an optional <see cref="IRFunc{TInput,TOutput}"/> parameter as the generated IR body of another
/// delegate parameter on the same method.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class IRBodyOfAttribute : Attribute
{
    public IRBodyOfAttribute(string parameterName)
    {
        ParameterName = ValidateParameterName(parameterName);
        StepKind = LoweredPipelineStepKind.Projection;
    }

    public IRBodyOfAttribute(string parameterName, LoweredPipelineStepKind stepKind)
    {
        ParameterName = ValidateParameterName(parameterName);
        StepKind = ValidateStepKind(stepKind);
        HasExplicitStepKind = true;
    }

    public string ParameterName { get; }

    public LoweredPipelineStepKind StepKind { get; }

    public bool HasExplicitStepKind { get; }

    private static string ValidateParameterName(string parameterName)
    {
        ArgumentNullException.ThrowIfNull(parameterName);

        return string.IsNullOrWhiteSpace(parameterName)
            ? throw new ArgumentException("Parameter name cannot be blank.", nameof(parameterName))
            : parameterName;
    }

    private static LoweredPipelineStepKind ValidateStepKind(LoweredPipelineStepKind stepKind)
        => Enum.IsDefined(stepKind)
            ? stepKind
            : throw new ArgumentOutOfRangeException(
                nameof(stepKind),
                stepKind,
                "Unsupported lowered pipeline step kind.");
}

/// <summary>
/// Typed public carrier for the IR body generated from a delegate argument.
/// </summary>
public sealed class IRFunc<TInput, TOutput>
{
    private IRFunc(LoweredPipelineStep step)
        => Step = step ?? throw new ArgumentNullException(nameof(step));

    public LoweredPipelineStep Step { get; }

    public static IRFunc<TInput, TOutput> FromStep(LoweredPipelineStep step) => new(step);
}

/// <summary>
/// Typed public carrier for an IR body generated from a delegate that receives an element and context.
/// </summary>
public sealed class IRFunc<TInput, TContext, TOutput>
{
    private IRFunc(LoweredPipelineStep step)
        => Step = step ?? throw new ArgumentNullException(nameof(step));

    public LoweredPipelineStep Step { get; }

    public static IRFunc<TInput, TContext, TOutput> FromStep(LoweredPipelineStep step) => new(step);
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
public sealed class LowerToIrMethodAttribute : Attribute
{
    public LowerToIrMethodAttribute(LoweredIrMethodKind kind)
        => Kind = ValidateMethodKind(kind);

    public LoweredIrMethodKind Kind { get; }

    private static LoweredIrMethodKind ValidateMethodKind(LoweredIrMethodKind kind)
        => Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unsupported lowered IR method kind.");
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
