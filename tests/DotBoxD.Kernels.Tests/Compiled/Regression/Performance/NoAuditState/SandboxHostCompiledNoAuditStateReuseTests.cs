using DotBoxD.Hosting.Execution.Prepared;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Compiled.Core;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.NoAuditState;

public sealed class SandboxHostCompiledNoAuditStateReuseTests
{
    private static readonly SandboxExecutionOptions Options = new()
    {
        Mode = ExecutionMode.Compiled,
        AllowFallbackToInterpreter = false,
        SuppressSuccessfulRunSummaryAudit = true
    };

    [Fact]
    public async Task Multiple_entrypoints_reuse_state_without_shortcutting_executable_lookup()
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(MultipleEntrypointModuleJson);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(long.MaxValue).Build());

        var first = await host.ExecuteAsync(plan, "first", SandboxValue.Unit, Options);
        AssertResult(first, expectedValue: 11);
        var probeExecutable = ProbeExecutable(plan, expectedValue: -1);
        CompiledNoAuditRunState pooledState;
        using (var lease = host.TryAcquireCompiledNoAuditState(
                   plan,
                   "first",
                   probeExecutable,
                   Options,
                   CancellationToken.None,
                   suppliedState: null,
                   useAsyncWorker: false))
        {
            pooledState = Assert.IsType<CompiledNoAuditRunState>(lease.State);
            pooledState.StoreExecutable("second", ProbeExecutable(plan, expectedValue: 999));
        }

        var second = await host.ExecuteAsync(plan, "second", SandboxValue.Unit, Options);

        AssertResult(second, expectedValue: 22);
        using var verification = host.TryAcquireCompiledNoAuditState(
            plan,
            "second",
            probeExecutable,
            Options,
            CancellationToken.None,
            suppliedState: null,
            useAsyncWorker: false);
        Assert.Same(pooledState, verification.State);
    }

    [Fact]
    public async Task Failed_structural_return_publication_is_cleared_before_pool_reuse()
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(StructuralModuleJson);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(long.MaxValue).Build());
        Assert.True(plan.BindingReferences.TryGetValue("main", out var allowedBindings));
        using var pool = new CompiledNoAuditRunStatePool();
        var publishedValue = new ListValue([SandboxValue.FromInt32(1)], SandboxType.I32);
        var expectedType = SandboxType.List(SandboxType.I32);
        var executable = new CompiledExecutable(
            CompiledArtifactTestFactory.DynamicMethod(
                plan,
                (context, _) =>
                {
                    _ = CompiledRuntime.RequireValueTypeAndRecordValidation(
                        context,
                        publishedValue,
                        expectedType);
                    throw new InvalidOperationException("fail after publishing proof");
                }),
            "Miss",
            SupportsReturnValidationProof: true);
        CompiledNoAuditRunState state;
        SandboxExecutionResult failed;
        using (var lease = pool.TryAcquire(plan))
        {
            state = Assert.IsType<CompiledNoAuditRunState>(lease.State);
            failed = await CompiledNoAuditResultRunner.Execute(
                executable,
                plan,
                "main",
                SandboxValue.Unit,
                Options,
                allowedBindings,
                CancellationToken.None,
                state);
        }

        Assert.False(failed.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, failed.Error!.Code);
        using var reused = pool.TryAcquire(plan);
        Assert.Same(state, reused.State);
        var context = state.ContextFor(allowedBindings, CancellationToken.None);
        Assert.False(context.TryConsumeCompiledReturnValidation(publishedValue, expectedType));
    }

    private static CompiledExecutable ProbeExecutable(ExecutionPlan plan, int expectedValue)
        => new(
            CompiledArtifactTestFactory.DynamicMethod(
                plan,
                (_, _) => SandboxValue.FromInt32(expectedValue)),
            "Miss");

    private static void AssertResult(SandboxExecutionResult result, int expectedValue)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expectedValue, Assert.IsType<I32Value>(result.Value).Value);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Empty(result.AuditEvents);
    }

    private const string MultipleEntrypointModuleJson = """
    {
      "id": "compiled-no-audit-state-pool-entrypoints",
      "version": "1.0.0",
      "functions": [
        {
          "id": "first",
          "visibility": "entrypoint",
          "parameters": [],
          "returnType": "I32",
          "body": [{ "op": "return", "value": { "i32": 11 } }]
        },
        {
          "id": "second",
          "visibility": "entrypoint",
          "parameters": [],
          "returnType": "I32",
          "body": [{ "op": "return", "value": { "i32": 22 } }]
        }
      ]
    }
    """;

    private const string StructuralModuleJson = """
    {
      "id": "compiled-no-audit-state-pool-proof-cleanup",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": { "name": "List", "arguments": ["I32"] },
        "body": [{ "op": "return", "value": { "call": "list.of", "args": [{ "i32": 1 }] } }]
      }]
    }
    """;
}
