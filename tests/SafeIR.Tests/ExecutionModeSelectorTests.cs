using SafeIR;
using SafeIR.Compiler;
using SafeIR.Hosting;

namespace SafeIR.Tests;

public sealed class ExecutionModeSelectorTests
{
    [Fact]
    public async Task Auto_mode_uses_configured_execution_mode_selector_after_first_run()
    {
        var selector = new RecordingSelector(ExecutionModeDecision.Interpreted);
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(new FailingCompiler());
            builder.UseExecutionModeSelector(selector);
        });
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(1)]);
        var options = new SandboxExecutionOptions { Mode = ExecutionMode.Auto, AutoCompileThreshold = 1 };

        var first = await host.ExecuteAsync(plan, "main", input, options);
        var second = await host.ExecuteAsync(plan, "main", input, options);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.True(second.Succeeded, second.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, first.ActualMode);
        Assert.Equal(ExecutionMode.Interpreted, second.ActualMode);
        Assert.Equal(1, selector.Calls);
        Assert.Equal(2, selector.LastHotness!.RunCount);
    }

    private sealed class RecordingSelector(ExecutionModeDecision decision) : IExecutionModeSelector
    {
        public int Calls { get; private set; }
        public ModuleHotnessStats? LastHotness { get; private set; }

        public ExecutionModeDecision Choose(
            ExecutionPlan plan,
            SandboxExecutionOptions options,
            ModuleHotnessStats hotness,
            CompiledCacheStatus cacheStatus)
        {
            Calls++;
            LastHotness = hotness;
            Assert.Equal(CompiledCacheStatus.None, cacheStatus);
            return decision;
        }
    }

    private sealed class FailingCompiler : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("compiler must not be called");
    }
}
