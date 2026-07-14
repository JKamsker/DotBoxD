using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Execution;

public sealed class ExecutionModeSelectorBoundaryTests
{
    private const string SelectorSecret = "selector-secret-must-not-leak";

    [Fact]
    public async Task Auto_mode_selector_exception_is_sanitized_and_completes_hotness_attempt()
    {
        var selector = new ThrowingSelector();
        var compiler = new CountingCompiler();
        var scenario = await SelectorScenario.CreateAsync(selector, compiler);

        var warmup = await scenario.ExecuteAsync();
        var failed = await scenario.ExecuteAsync();
        var repeated = await scenario.ExecuteAsync();

        Assert.True(warmup.Succeeded, warmup.Error?.SafeMessage);
        AssertSelectorFailure(failed);
        AssertSelectorFailure(repeated);
        Assert.Equal(0, compiler.Calls);
        Assert.Equal(2, selector.HotnessSnapshots.Count);
        Assert.Equal(3, selector.HotnessSnapshots[1].RunCount);
        Assert.Equal(2, selector.HotnessSnapshots[1].CompletedRunCount);
    }

    [Fact]
    public async Task Auto_mode_selector_cancellation_is_observed_before_backend_dispatch()
    {
        using var cancellation = new CancellationTokenSource();
        var selector = new CancelCallerTokenSelector(cancellation);
        var compiler = new CountingCompiler();
        var scenario = await SelectorScenario.CreateAsync(selector, compiler);

        var warmup = await scenario.ExecuteAsync();
        var cancelled = await scenario.ExecuteAsync(cancellation.Token);
        var next = await scenario.ExecuteAsync();

        Assert.True(warmup.Succeeded, warmup.Error?.SafeMessage);
        Assert.False(cancelled.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, cancelled.Error!.Code);
        Assert.Equal(ExecutionMode.Auto, cancelled.ActualMode);
        Assert.False(cancelled.ExecutionDispatched);
        Assert.Equal(0, compiler.Calls);
        Assert.Contains(cancelled.AuditEvents, e =>
            e.Kind == "RunSummary" && !e.Success && e.ErrorCode == SandboxErrorCode.Cancelled);
        Assert.True(next.Succeeded, next.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, next.ActualMode);
        Assert.Equal(2, selector.HotnessSnapshots.Count);
        Assert.Equal(3, selector.HotnessSnapshots[1].RunCount);
        Assert.Equal(2, selector.HotnessSnapshots[1].CompletedRunCount);
    }

    private static void AssertSelectorFailure(SandboxExecutionResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Equal("execution mode selector failed", result.Error.SafeMessage);
        Assert.Equal(ExecutionMode.Auto, result.ActualMode);
        Assert.False(result.ExecutionDispatched);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "ExecutionModeSelectionFailed" && e.ErrorCode == SandboxErrorCode.HostFailure);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "RunSummary" && !e.Success && e.ErrorCode == SandboxErrorCode.HostFailure);
        Assert.All(result.AuditEvents, e =>
            Assert.DoesNotContain(SelectorSecret, e.Message ?? string.Empty, StringComparison.Ordinal));
    }

    private sealed class ThrowingSelector : IExecutionModeSelector
    {
        public List<ModuleHotnessStats> HotnessSnapshots { get; } = [];

        public ExecutionModeDecision Choose(
            ExecutionPlan plan,
            SandboxExecutionOptions options,
            ModuleHotnessStats hotness,
            CompiledCacheStatus cacheStatus)
        {
            HotnessSnapshots.Add(hotness);
            throw new InvalidOperationException(SelectorSecret);
        }
    }

    private sealed class CancelCallerTokenSelector(CancellationTokenSource cancellation) : IExecutionModeSelector
    {
        public List<ModuleHotnessStats> HotnessSnapshots { get; } = [];

        public ExecutionModeDecision Choose(
            ExecutionPlan plan,
            SandboxExecutionOptions options,
            ModuleHotnessStats hotness,
            CompiledCacheStatus cacheStatus)
        {
            HotnessSnapshots.Add(hotness);
            if (HotnessSnapshots.Count == 1)
            {
                cancellation.Cancel();
                return ExecutionModeDecision.Compiled;
            }

            return ExecutionModeDecision.Interpreted;
        }
    }

    private sealed class CountingCompiler : ISandboxCompiler
    {
        public int Calls { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("compiler must not be called");
        }
    }

    private sealed record SelectorScenario(
        SandboxHost Host,
        ExecutionPlan Plan,
        SandboxValue Input,
        SandboxExecutionOptions Options)
    {
        public static async Task<SelectorScenario> CreateAsync(
            IExecutionModeSelector selector,
            ISandboxCompiler compiler)
        {
            var host = SandboxHost.Create(builder =>
            {
                builder.AddDefaultPureBindings();
                builder.UseInterpreter();
                builder.UseCompilerIfAvailable(compiler);
                builder.UseExecutionModeSelector(selector);
            });
            var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
            var policy = SandboxPolicyBuilder.Create().WithFuel(1_000).Build();
            var plan = await host.PrepareAsync(module, policy);
            var input = SandboxValue.FromList([
                SandboxValue.FromInt32(1),
                SandboxValue.FromInt32(1)
            ]);
            var options = new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Auto,
                AutoCompileThreshold = 1,
                AllowFallbackToInterpreter = false
            };
            return new SelectorScenario(host, plan, input, options);
        }

        public ValueTask<SandboxExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default)
            => Host.ExecuteAsync(Plan, "main", Input, Options, cancellationToken);
    }
}
