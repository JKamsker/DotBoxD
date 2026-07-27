using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Core;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.NoAuditState;

public sealed class SandboxHostCompiledNoAuditStateEligibilityTests
{
    private static readonly SandboxExecutionOptions CompiledOptions = new()
    {
        Mode = ExecutionMode.Compiled,
        AllowFallbackToInterpreter = false,
        SuppressSuccessfulRunSummaryAudit = true
    };

    [Fact]
    public async Task Host_pool_is_created_only_for_direct_non_cancelable_no_audit_execution()
    {
        using var host = SandboxTestHost.Create();
        var purePlan = await PrepareAsync(host, PureModuleJson);
        var pureExecutable = Executable(purePlan);
        Assert.False(host.HasCompiledNoAuditRunStatePool);

        using var cancellation = new CancellationTokenSource();
        AssertBypassed(host, purePlan, pureExecutable, CompiledOptions, cancellation.Token);
        cancellation.Cancel();
        AssertBypassed(host, purePlan, pureExecutable, CompiledOptions, cancellation.Token);
        AssertBypassed(host, purePlan, pureExecutable, CompiledOptions with
        {
            SuppressSuccessfulRunSummaryAudit = false
        });
        AssertBypassed(host, purePlan, pureExecutable, CompiledOptions with { EnableDebugTrace = true });
        AssertBypassed(host, purePlan, pureExecutable, CompiledOptions with
        {
            Isolation = SandboxIsolation.WorkerProcess
        });
        AssertBypassed(host, purePlan, pureExecutable, CompiledOptions, useAsyncWorker: true);
        AssertBypassed(
            host,
            purePlan,
            pureExecutable with
            {
                Artifact = pureExecutable.Artifact with { CacheInvalidReason = "stale cache entry" }
            },
            CompiledOptions);
        var suppliedState = new CompiledNoAuditRunState(purePlan);
        AssertBypassed(host, purePlan, pureExecutable, CompiledOptions, suppliedState: suppliedState);
        suppliedState.Dispose();

        var bindingPlan = await PrepareAsync(host, BindingModuleJson);
        AssertBypassed(host, bindingPlan, Executable(bindingPlan), CompiledOptions);
        Assert.False(host.HasCompiledNoAuditRunStatePool);

        using var eligible = host.TryAcquireCompiledNoAuditState(
            purePlan,
            "main",
            pureExecutable,
            CompiledOptions,
            CancellationToken.None,
            suppliedState: null,
            useAsyncWorker: false);
        Assert.True(eligible.IsAcquired);
        Assert.True(host.HasCompiledNoAuditRunStatePool);
    }

    [Fact]
    public async Task Prepared_compiled_scope_does_not_start_using_its_auto_state()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var plan = await PrepareAsync(host, PureModuleJson);
        var suppliedState = new CompiledNoAuditRunState(plan);

        var result = await host.ExecutePreparedInProcessAsync(
            plan,
            "main",
            SandboxValue.Unit,
            CompiledOptions,
            CancellationToken.None,
            suppliedState);

        AssertSuccess(result, expectedMode: ExecutionMode.Compiled);
        Assert.Equal(0, suppliedState.Budget.FuelUsed);
        Assert.True(host.HasCompiledNoAuditRunStatePool);
        suppliedState.Dispose();
    }

    [Fact]
    public async Task Prepared_auto_compiled_execution_prefers_its_supplied_state()
    {
        using var host = CreateAlwaysCompiledHost();
        var plan = await PrepareAsync(host, PureModuleJson);
        var suppliedState = new CompiledNoAuditRunState(plan);
        var options = CompiledOptions with { Mode = ExecutionMode.Auto };

        var interpreted = await host.ExecutePreparedInProcessAsync(
            plan,
            "main",
            SandboxValue.Unit,
            options,
            CancellationToken.None,
            suppliedState);
        var compiled = await host.ExecutePreparedInProcessAsync(
            plan,
            "main",
            SandboxValue.Unit,
            options,
            CancellationToken.None,
            suppliedState);

        AssertSuccess(interpreted, expectedMode: ExecutionMode.Interpreted);
        AssertSuccess(compiled, expectedMode: ExecutionMode.Compiled);
        Assert.True(suppliedState.Budget.FuelUsed > 0);
        Assert.False(host.HasCompiledNoAuditRunStatePool);
        suppliedState.Dispose();
    }

    [Fact]
    public async Task Public_auto_selected_compiled_execution_uses_the_host_pool()
    {
        using var host = CreateAlwaysCompiledHost();
        var plan = await PrepareAsync(host, PureModuleJson);
        var options = CompiledOptions with { Mode = ExecutionMode.Auto };

        var interpreted = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);
        AssertSuccess(interpreted, expectedMode: ExecutionMode.Interpreted);
        Assert.False(host.HasCompiledNoAuditRunStatePool);
        var compiled = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);

        AssertSuccess(compiled, expectedMode: ExecutionMode.Compiled);
        Assert.True(host.HasCompiledNoAuditRunStatePool);
    }

    [Fact]
    public async Task Host_disposal_prevents_late_pool_initialization()
    {
        var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, PureModuleJson);
        var executable = Executable(plan);
        host.Dispose();

        AssertBypassed(host, plan, executable, CompiledOptions);
        Assert.False(host.HasCompiledNoAuditRunStatePool);
        host.Dispose();
    }

    private static void AssertBypassed(
        SandboxHost host,
        ExecutionPlan plan,
        CompiledExecutable executable,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken = default,
        CompiledNoAuditRunState? suppliedState = null,
        bool useAsyncWorker = false)
    {
        using var lease = host.TryAcquireCompiledNoAuditState(
            plan,
            "main",
            executable,
            options,
            cancellationToken,
            suppliedState,
            useAsyncWorker);
        Assert.False(lease.IsAcquired);
        Assert.Null(lease.State);
        Assert.False(host.HasCompiledNoAuditRunStatePool);
    }

    private static void AssertSuccess(SandboxExecutionResult result, ExecutionMode expectedMode)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(7, Assert.IsType<I32Value>(result.Value).Value);
        Assert.Equal(expectedMode, result.ActualMode);
    }

    private static SandboxHost CreateAlwaysCompiledHost()
        => SandboxHost.Create(builder =>
        {
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
            builder.UseExecutionModeSelector(new AlwaysCompiledSelector());
        });

    private static CompiledExecutable Executable(ExecutionPlan plan)
        => new(
            CompiledArtifactTestFactory.DynamicMethod(
                plan,
                static (_, _) => SandboxValue.FromInt32(7)),
            "Miss");

    private static async Task<ExecutionPlan> PrepareAsync(SandboxHost host, string json)
    {
        var module = await host.ImportJsonAsync(json);
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(long.MaxValue).Build());
    }

    private sealed class AlwaysCompiledSelector : IExecutionModeSelector
    {
        public ExecutionModeDecision Choose(
            ExecutionPlan plan,
            SandboxExecutionOptions options,
            ModuleHotnessStats hotness,
            CompiledCacheStatus cacheStatus)
            => ExecutionModeDecision.Compiled;
    }

    private const string PureModuleJson = """
    {
      "id": "compiled-no-audit-pool-eligibility-pure",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{ "op": "return", "value": { "i32": 7 } }]
      }]
    }
    """;

    private const string BindingModuleJson = """
    {
      "id": "compiled-no-audit-pool-eligibility-binding",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": { "call": "math.abs", "args": [{ "i32": -7 }] }
        }]
      }]
    }
    """;
}
