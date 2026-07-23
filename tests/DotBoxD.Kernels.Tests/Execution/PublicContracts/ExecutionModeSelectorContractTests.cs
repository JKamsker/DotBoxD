using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Execution.PublicContracts;

public sealed class ExecutionModeSelectorContractTests
{
    private static readonly SourceSpan Span = new(1, 1);
    private static readonly Expression Literal = new LiteralExpression(SandboxValue.FromInt32(1), Span);

    [Theory]
    [InlineData("plan")]
    [InlineData("options")]
    [InlineData("hotness")]
    public void Hotness_selector_rejects_null_required_arguments(string parameterName)
    {
        var selector = new HotnessExecutionModeSelector();
        var exception = Assert.Throws<ArgumentNullException>(() =>
            selector.Choose(
                parameterName == "plan" ? null! : ValidPlan(),
                parameterName == "options" ? null! : ValidOptions(),
                parameterName == "hotness" ? null! : ValidHotness(),
                CompiledCacheStatus.None));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 4)]
    [InlineData(5, 5)]
    [InlineData(int.MaxValue, int.MaxValue - 1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void Built_in_run_count_path_matches_public_selector(int threshold, int runCount)
    {
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Auto,
            AutoCompileThreshold = threshold
        };
        var selected = new HotnessExecutionModeSelector().Choose(
            ValidPlan(),
            options,
            Hotness(runCount: runCount),
            CompiledCacheStatus.None);

        Assert.Equal(selected.Mode, HotnessExecutionModeSelector.ChooseMode(options, runCount));
    }

    [Theory]
    [InlineData("PlanHash")]
    [InlineData("Entrypoint")]
    public void Module_hotness_stats_rejects_null_required_identifiers(string memberName)
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            CreateHotnessWithNullIdentifier(memberName));

        Assert.Equal(memberName, exception.ParamName);
    }

    [Theory]
    [InlineData("RunCount")]
    [InlineData("CompletedRunCount")]
    [InlineData("CompileFailures")]
    public void Module_hotness_stats_rejects_negative_counters(string memberName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateHotnessWithNegativeCounter(memberName));

        Assert.Equal(memberName, exception.ParamName);
    }

    private static ModuleHotnessStats CreateHotnessWithNullIdentifier(string memberName)
        => memberName switch
        {
            "PlanHash" => Hotness(planHash: null!),
            "Entrypoint" => Hotness(entrypoint: null!),
            _ => throw new ArgumentOutOfRangeException(nameof(memberName), memberName, null)
        };

    private static ModuleHotnessStats CreateHotnessWithNegativeCounter(string memberName)
        => memberName switch
        {
            "RunCount" => Hotness(runCount: -1),
            "CompletedRunCount" => Hotness(completedRunCount: -1),
            "CompileFailures" => Hotness(compileFailures: -1),
            _ => throw new ArgumentOutOfRangeException(nameof(memberName), memberName, null)
        };

    private static SandboxExecutionOptions ValidOptions()
        => new() { Mode = ExecutionMode.Auto, AutoCompileThreshold = 2 };

    private static ModuleHotnessStats ValidHotness()
        => Hotness();

    private static ModuleHotnessStats Hotness(
        string planHash = "plan",
        string entrypoint = "main",
        int runCount = 1,
        int completedRunCount = 0,
        int compileFailures = 0)
        => new(
            planHash,
            entrypoint,
            runCount,
            completedRunCount,
            TimeSpan.Zero,
            AverageFuelUsed: 0,
            LastRunAt: null,
            compileFailures,
            LastCompiledArtifactHash: null);

    private static ExecutionPlan ValidPlan()
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
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal)
            {
                ["main"] = new(SandboxType.I32, SandboxEffect.Cpu, CanReorder: true)
            });

    private static SandboxModule EmptyModule()
        => new("module", SemVersion.One, SemVersion.One, [], [EmptyFunction()], new Dictionary<string, string>());

    private static SandboxFunction EmptyFunction()
        => new("main", true, [], SandboxType.I32, [new ReturnStatement(Literal, Span)]);
}
