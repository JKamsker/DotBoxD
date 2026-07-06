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
    private IReadOnlyDictionary<string, FunctionAnalysis> _functions = CopyDictionary(Functions, nameof(Functions));
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
        init => _functions = CopyDictionary(value, nameof(Functions));
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
            if (value is null)
            {
                throw new ArgumentException("Collection values cannot be null.", paramName);
            }

            copy.Add(value);
        }

        return new ReadOnlyCollection<T>(copy);
    }

    private static IReadOnlyDictionary<string, TValue> CopyDictionary<TValue>(
        IReadOnlyDictionary<string, TValue> values,
        string paramName)
    {
        ArgumentNullException.ThrowIfNull(values, paramName);

        var copy = new Dictionary<string, TValue>(values.Count, StringComparer.Ordinal);
        foreach (var item in values)
        {
            if (item.Key is null)
            {
                throw new ArgumentException("Dictionary keys cannot be null.", paramName);
            }

            if (item.Value is null)
            {
                throw new ArgumentException("Dictionary values cannot be null.", paramName);
            }

            copy.Add(item.Key, item.Value);
        }

        return new ReadOnlyDictionary<string, TValue>(
            copy);
    }

    private static IReadOnlySet<string> CopySet(IReadOnlySet<string> values, string paramName)
    {
        ArgumentNullException.ThrowIfNull(values, paramName);

        var copy = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null)
            {
                throw new ArgumentException("Set values cannot be null.", paramName);
            }

            copy.Add(value);
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
            if (item.Key is null)
            {
                throw new ArgumentException("Dictionary keys cannot be null.", paramName);
            }

            copy.Add(item.Key, CopySet(item.Value, paramName));
        }

        return new ReadOnlyDictionary<string, IReadOnlySet<string>>(copy);
    }

    private static SandboxEffect ValidateModuleEffects(SandboxEffect effects, string paramName)
    {
        if (!effects.ContainsOnlyKnownBits())
        {
            throw new ArgumentOutOfRangeException(paramName, effects, "Module effects contain undefined bits.");
        }

        return effects;
    }
}
