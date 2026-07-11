using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core.Contracts;

public sealed class ExecutionPlanContractTests
{
    private static readonly SourceSpan Span = new(1, 1);
    private static readonly Expression Literal = new LiteralExpression(SandboxValue.FromInt32(1), Span);

    [Fact]
    public void Constructor_rejects_null_function_analysis_at_public_boundary()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            CreatePlanWithFunctionAnalysis(null!));

        Assert.Equal("functionAnalysis", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_null_function_analysis_entries_at_public_boundary()
    {
        var functionAnalysis = ValidFunctionAnalysis();
        functionAnalysis["main"] = null!;

        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            CreatePlan(functionAnalysis: functionAnalysis));

        Assert.Equal("functionAnalysis", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_function_analysis_with_null_return_type()
    {
        var functionAnalysis = ValidFunctionAnalysis();
        functionAnalysis["main"] = new FunctionAnalysis(null!, SandboxEffect.Cpu, CanReorder: true);

        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            CreatePlan(functionAnalysis: functionAnalysis));

        Assert.Equal("functionAnalysis", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_function_analysis_with_undefined_effect_bits()
    {
        var functionAnalysis = ValidFunctionAnalysis();
        functionAnalysis["main"] = new FunctionAnalysis(SandboxType.I32, UndefinedEffect(), CanReorder: true);

        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            CreatePlan(functionAnalysis: functionAnalysis));

        Assert.Equal("functionAnalysis", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_null_binding_reference_sets_at_public_boundary()
    {
        var bindingReferences = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["main"] = null!
        };

        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            CreatePlan(bindingReferences: bindingReferences));

        Assert.Equal("bindingReferences", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_null_binding_reference_elements_at_public_boundary()
    {
        var bindingReferences = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["main"] = new HashSet<string>(StringComparer.Ordinal) { null! }
        };

        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            CreatePlan(bindingReferences: bindingReferences));

        Assert.Equal("bindingReferences", exception.ParamName);
    }

    private static ExecutionPlan CreatePlan(
        IReadOnlyDictionary<string, FunctionAnalysis>? functionAnalysis = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? bindingReferences = null)
        => new(
            "module",
            "plan",
            new ExecutionPlanSeal("seal"),
            "policy",
            "bindings",
            EmptyModule(),
            SandboxPolicyBuilder.Create().Build(),
            new BindingRegistryBuilder().Build(),
            new ResourceLimits(),
            functionAnalysis ?? ValidFunctionAnalysis(),
            bindingReferences);

    private static ExecutionPlan CreatePlanWithFunctionAnalysis(
        IReadOnlyDictionary<string, FunctionAnalysis>? functionAnalysis)
        => new(
            "module",
            "plan",
            new ExecutionPlanSeal("seal"),
            "policy",
            "bindings",
            EmptyModule(),
            SandboxPolicyBuilder.Create().Build(),
            new BindingRegistryBuilder().Build(),
            new ResourceLimits(),
            functionAnalysis!,
            bindingReferences: null);

    private static Dictionary<string, FunctionAnalysis> ValidFunctionAnalysis()
        => new(StringComparer.Ordinal)
        {
            ["main"] = new(SandboxType.I32, SandboxEffect.Cpu, CanReorder: true)
        };

    private static SandboxEffect UndefinedEffect()
        => (SandboxEffect)(1 << 20);

    private static SandboxModule EmptyModule()
        => new("module", SemVersion.One, SemVersion.One, [], [EmptyFunction()], new Dictionary<string, string>());

    private static SandboxFunction EmptyFunction()
        => new("main", true, [], SandboxType.I32, [new ReturnStatement(Literal, Span)]);
}
