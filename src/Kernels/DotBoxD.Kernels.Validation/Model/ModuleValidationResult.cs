using System.Collections.Frozen;
using System.Collections.ObjectModel;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Validation.Model;

public sealed record ModuleValidationResult(
    bool Succeeded,
    IReadOnlyList<SandboxDiagnostic> Diagnostics,
    IReadOnlyDictionary<string, FunctionAnalysis> Functions,
    SandboxEffect ModuleEffects,
    IReadOnlySet<string> RequiredCapabilities,
    IReadOnlyDictionary<string, IReadOnlySet<string>> BindingReferences)
{
    private SandboxEffect _moduleEffects = ValidateModuleEffects(ModuleEffects, nameof(ModuleEffects));
    private IReadOnlyList<SandboxDiagnostic> _diagnostics = CopyList(Diagnostics, nameof(Diagnostics));
    private IReadOnlyDictionary<string, FunctionAnalysis> _functions = CopyFunctionAnalysis(Functions, nameof(Functions));
    private IReadOnlySet<string> _requiredCapabilities = CopySet(RequiredCapabilities, nameof(RequiredCapabilities));
    private IReadOnlyDictionary<string, IReadOnlySet<string>> _bindingReferences =
        CopyBindingReferences(BindingReferences, nameof(BindingReferences));

    public SandboxEffect ModuleEffects
    {
        get => _moduleEffects;
        init => _moduleEffects = ValidateModuleEffects(value, nameof(ModuleEffects));
    }

    public IReadOnlyList<SandboxDiagnostic> Diagnostics
    {
        get => _diagnostics;
        init => _diagnostics = CopyList(value, nameof(Diagnostics));
    }

    public IReadOnlyDictionary<string, FunctionAnalysis> Functions
    {
        get => _functions;
        init => _functions = CopyFunctionAnalysis(value, nameof(Functions));
    }

    public IReadOnlySet<string> RequiredCapabilities
    {
        get => _requiredCapabilities;
        init => _requiredCapabilities = CopySet(value, nameof(RequiredCapabilities));
    }

    public IReadOnlyDictionary<string, IReadOnlySet<string>> BindingReferences
    {
        get => _bindingReferences;
        init => _bindingReferences = CopyBindingReferences(value, nameof(BindingReferences));
    }

    public static ModuleValidationResult Failure(IReadOnlyList<SandboxDiagnostic> diagnostics)
        => new(
            false,
            diagnostics,
            new Dictionary<string, FunctionAnalysis>(),
            SandboxEffect.None,
            new HashSet<string>(),
            new Dictionary<string, IReadOnlySet<string>>());

    private static IReadOnlyList<T> CopyList<T>(IEnumerable<T> values, string paramName)
    {
        ArgumentNullException.ThrowIfNull(values, paramName);

        var copy = new List<T>();
        foreach (var value in values)
        {
            copy.Add(RequireNonNull(value, paramName, "Collection values cannot be null."));
        }

        return new ReadOnlyCollection<T>(copy);
    }

    private static IReadOnlyDictionary<string, FunctionAnalysis> CopyFunctionAnalysis(
        IReadOnlyDictionary<string, FunctionAnalysis> values,
        string paramName)
    {
        ArgumentNullException.ThrowIfNull(values, paramName);

        var copy = new Dictionary<string, FunctionAnalysis>(values.Count, StringComparer.Ordinal);
        foreach (var item in values)
        {
            copy.Add(
                RequireNonNull(item.Key, paramName, "Dictionary keys cannot be null."),
                RequireFunctionAnalysis(item.Value, paramName));
        }

        return new ReadOnlyDictionary<string, FunctionAnalysis>(copy);
    }

    private static IReadOnlySet<string> CopySet(IReadOnlySet<string> values, string paramName)
    {
        ArgumentNullException.ThrowIfNull(values, paramName);

        var copy = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            copy.Add(RequireNonNull(value, paramName, "Set values cannot be null."));
        }

        return copy.ToFrozenSet(StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> CopyBindingReferences(
        IReadOnlyDictionary<string, IReadOnlySet<string>> values,
        string paramName)
    {
        ArgumentNullException.ThrowIfNull(values, paramName);

        var copy = new Dictionary<string, IReadOnlySet<string>>(values.Count, StringComparer.Ordinal);
        foreach (var item in values)
        {
            copy.Add(
                RequireNonNull(item.Key, paramName, "Dictionary keys cannot be null."),
                CopyBindingReferenceSet(
                    RequireNonNull(item.Value, paramName, "Dictionary values cannot be null."),
                    paramName));
        }

        return new ReadOnlyDictionary<string, IReadOnlySet<string>>(copy);
    }

    private static IReadOnlySet<string> CopyBindingReferenceSet(IReadOnlySet<string> values, string paramName)
    {
        var copy = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            copy.Add(RequireBindingReferenceId(value, paramName));
        }

        return copy.ToFrozenSet(StringComparer.Ordinal);
    }

    private static SandboxEffect ValidateModuleEffects(SandboxEffect effects, string paramName)
    {
        if (!effects.ContainsOnlyKnownBits())
        {
            throw new ArgumentOutOfRangeException(paramName, effects, "Module effects contain undefined bits.");
        }

        return effects;
    }

    private static FunctionAnalysis RequireFunctionAnalysis(FunctionAnalysis value, string paramName)
    {
        RequireNonNull(value, paramName, "Dictionary values cannot be null.");
        RequireNonNull(value.ReturnType, paramName, "Function analysis return types cannot be null.");

        if (!value.Effects.ContainsOnlyKnownBits())
        {
            throw new ArgumentException("Function analysis effects must contain only known effect bits.", paramName);
        }

        return value;
    }

    private static T RequireNonNull<T>(T value, string paramName, string message)
    {
        if (value is null)
        {
            throw new ArgumentException(message, paramName);
        }

        return value;
    }

    private static string RequireBindingReferenceId(string value, string paramName)
    {
        RequireNonNull(value, paramName, "Set values cannot be null.");

        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Binding reference IDs cannot be blank.", paramName)
            : value;
    }
}
