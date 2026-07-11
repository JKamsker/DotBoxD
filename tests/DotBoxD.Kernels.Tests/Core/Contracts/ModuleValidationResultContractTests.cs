using System.Collections;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation.Model;

namespace DotBoxD.Kernels.Tests.Core.Contracts;

public sealed class ModuleValidationResultContractTests
{
    public static IEnumerable<object[]> NullConstructorEvidenceInputs()
    {
        yield return
        [
            "Diagnostics",
            ThrowingAction(() => CreateRaw(null!, Functions(), SandboxEffect.Audit, RequiredCapabilities(), BindingReferences()))
        ];
        yield return
        [
            "Functions",
            ThrowingAction(() => CreateRaw(Diagnostics(), null!, SandboxEffect.Audit, RequiredCapabilities(), BindingReferences()))
        ];
        yield return
        [
            "RequiredCapabilities",
            ThrowingAction(() => CreateRaw(Diagnostics(), Functions(), SandboxEffect.Audit, null!, BindingReferences()))
        ];
        yield return
        [
            "BindingReferences",
            ThrowingAction(() => CreateRaw(Diagnostics(), Functions(), SandboxEffect.Audit, RequiredCapabilities(), null!))
        ];
    }

    public static IEnumerable<object[]> NullInitEvidenceInputs()
    {
        yield return
        [
            "Diagnostics",
            ThrowingAction(() => ValidResult() with { Diagnostics = null! })
        ];
        yield return
        [
            "Functions",
            ThrowingAction(() => ValidResult() with { Functions = null! })
        ];
        yield return
        [
            "RequiredCapabilities",
            ThrowingAction(() => ValidResult() with { RequiredCapabilities = null! })
        ];
        yield return
        [
            "BindingReferences",
            ThrowingAction(() => ValidResult() with { BindingReferences = null! })
        ];
    }

    public static IEnumerable<object[]> MalformedEvidenceInputs()
    {
        yield return
        [
            "Diagnostics",
            ThrowingAction(() => Create(diagnostics: [null!]))
        ];
        yield return
        [
            "Functions",
            ThrowingAction(() => Create(functions: new NullKeyDictionary<FunctionAnalysis>(Analysis())))
        ];
        yield return
        [
            "Functions",
            ThrowingAction(() => Create(functions: new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal)
            {
                ["main"] = null!
            }))
        ];
        yield return
        [
            "RequiredCapabilities",
            ThrowingAction(() => Create(requiredCapabilities: new HashSet<string>(StringComparer.Ordinal) { null! }))
        ];
        yield return
        [
            "BindingReferences",
            ThrowingAction(() => Create(bindingReferences: new NullKeyDictionary<IReadOnlySet<string>>(References())))
        ];
        yield return
        [
            "BindingReferences",
            ThrowingAction(() => Create(
                bindingReferences: new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["main"] = null!
            }))
        ];
        yield return
        [
            "BindingReferences",
            ThrowingAction(() => Create(
                bindingReferences: new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["main"] = new HashSet<string>(StringComparer.Ordinal) { null! }
            }))
        ];
        yield return
        [
            "BindingReferences",
            ThrowingAction(() => Create(bindingReferences: BindingReferencesWith("")))
        ];
        yield return
        [
            "BindingReferences",
            ThrowingAction(() => Create(bindingReferences: BindingReferencesWith("   ")))
        ];
    }

    public static IEnumerable<object[]> MalformedInitEvidenceInputs()
    {
        yield return ["Diagnostics", ThrowingAction(() => ValidResult() with { Diagnostics = [null!] })];
        yield return ["Functions", ThrowingAction(() => ValidResult() with { Functions = new NullKeyDictionary<FunctionAnalysis>(Analysis()) })];
        yield return ["Functions", ThrowingAction(() => ValidResult() with { Functions = new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal) { ["main"] = null! } })];
        yield return ["RequiredCapabilities", ThrowingAction(() => ValidResult() with { RequiredCapabilities = new HashSet<string>(StringComparer.Ordinal) { null! } })];
        yield return ["BindingReferences", ThrowingAction(() => ValidResult() with { BindingReferences = new NullKeyDictionary<IReadOnlySet<string>>(References()) })];
        yield return ["BindingReferences", ThrowingAction(() => ValidResult() with { BindingReferences = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal) { ["main"] = null! } })];
        yield return ["BindingReferences", ThrowingAction(() => ValidResult() with { BindingReferences = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal) { ["main"] = new HashSet<string>(StringComparer.Ordinal) { null! } } })];
        yield return ["BindingReferences", ThrowingAction(() => ValidResult() with { BindingReferences = BindingReferencesWith("") })];
        yield return ["BindingReferences", ThrowingAction(() => ValidResult() with { BindingReferences = BindingReferencesWith("   ") })];
    }

    [Theory]
    [MemberData(nameof(NullConstructorEvidenceInputs))]
    public void Constructor_rejects_null_evidence_collections_with_public_parameter_name(
        string expectedParamName,
        Action action)
    {
        var exception = Assert.Throws<ArgumentNullException>(action);

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(NullInitEvidenceInputs))]
    public void Init_setters_reject_null_evidence_collections_with_public_property_name(
        string expectedParamName,
        Action action)
    {
        var exception = Assert.Throws<ArgumentNullException>(action);

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(MalformedEvidenceInputs))]
    public void Constructor_rejects_malformed_evidence_values_with_public_parameter_name(
        string expectedParamName,
        Action action)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(action);

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(MalformedInitEvidenceInputs))]
    public void Init_setters_reject_malformed_evidence_values_with_public_property_name(
        string expectedParamName,
        Action action)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(action);

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_undefined_module_effects()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(moduleEffects: SandboxEffect.Audit | (SandboxEffect)(1 << 20)));

        Assert.Equal("ModuleEffects", exception.ParamName);
    }

    [Fact]
    public void Init_rejects_undefined_module_effects()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = ValidResult() with { ModuleEffects = UndefinedEffect() });

        Assert.Equal("ModuleEffects", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_function_analysis_with_undefined_effect_bits()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => Create(functions: FunctionsWithUndefinedEffects()));

        Assert.Equal("Functions", exception.ParamName);
    }

    [Fact]
    public void Init_rejects_function_analysis_with_undefined_effect_bits()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => _ = ValidResult() with { Functions = FunctionsWithUndefinedEffects() });

        Assert.Equal("Functions", exception.ParamName);
    }

    private static ModuleValidationResult ValidResult()
        => Create();

    private static Action ThrowingAction(Func<ModuleValidationResult> create)
        => () => _ = create();

    private static ModuleValidationResult Create(
        IReadOnlyList<SandboxDiagnostic>? diagnostics = null,
        IReadOnlyDictionary<string, FunctionAnalysis>? functions = null,
        SandboxEffect moduleEffects = SandboxEffect.Audit,
        IReadOnlySet<string>? requiredCapabilities = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? bindingReferences = null)
        => CreateRaw(
            diagnostics ?? Diagnostics(),
            functions ?? Functions(),
            moduleEffects,
            requiredCapabilities ?? RequiredCapabilities(),
            bindingReferences ?? BindingReferences());

    private static ModuleValidationResult CreateRaw(
        IReadOnlyList<SandboxDiagnostic> diagnostics,
        IReadOnlyDictionary<string, FunctionAnalysis> functions,
        SandboxEffect moduleEffects,
        IReadOnlySet<string> requiredCapabilities,
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences)
        => new(
            true,
            diagnostics,
            functions,
            moduleEffects,
            requiredCapabilities,
            bindingReferences);

    private static IReadOnlyList<SandboxDiagnostic> Diagnostics()
        => [Diagnostic()];

    private static IReadOnlyDictionary<string, FunctionAnalysis> Functions()
        => new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal)
        {
            ["main"] = Analysis()
        };

    private static IReadOnlySet<string> RequiredCapabilities()
        => new HashSet<string>(StringComparer.Ordinal) { "log.write" };

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> BindingReferences()
        => new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["main"] = References()
        };

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> BindingReferencesWith(
        string bindingId)
        => new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["main"] = new HashSet<string>(StringComparer.Ordinal) { bindingId }
        };

    private static SandboxDiagnostic Diagnostic()
        => new("W-TEST", "test", DiagnosticSeverity.Warning);

    private static FunctionAnalysis Analysis()
        => new(SandboxType.I32, SandboxEffect.Audit, true);

    private static IReadOnlyDictionary<string, FunctionAnalysis> FunctionsWithUndefinedEffects()
        => new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal)
        {
            ["main"] = new(SandboxType.I32, UndefinedEffect(), true)
        };

    private static SandboxEffect UndefinedEffect()
        => (SandboxEffect)(1 << 20);

    private static IReadOnlySet<string> References()
        => new HashSet<string>(StringComparer.Ordinal) { "log.write" };

    private sealed class NullKeyDictionary<TValue>(TValue value) : IReadOnlyDictionary<string, TValue>
    {
        public int Count => 1;

        public IEnumerable<string> Keys => [null!];

        public IEnumerable<TValue> Values => [value];

        public TValue this[string key] => value;

        public bool ContainsKey(string key)
            => key is null;

        public bool TryGetValue(string key, out TValue dictionaryValue)
        {
            dictionaryValue = value;
            return key is null;
        }

        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
        {
            yield return new KeyValuePair<string, TValue>(null!, value);
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
