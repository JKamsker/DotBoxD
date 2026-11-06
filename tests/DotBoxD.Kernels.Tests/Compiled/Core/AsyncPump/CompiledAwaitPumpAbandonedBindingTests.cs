using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

public sealed class CompiledAwaitPumpAbandonedBindingTests
{
    [Fact]
    public async Task Binding_abandoned_on_wall_time_timeout_still_resumes_and_runs_its_finally()
    {
        // The binding awaits without ConfigureAwait(false), so its continuation is captured on the compiled
        // await pump. The kernel times out (and the pump is disposed) before the await completes; completing
        // it afterwards must resume the binding on the thread pool rather than throw from the disposed pump.
        var gate = new TaskCompletionSource();
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(AbandonedBinding(gate, started, resumed));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(CallModuleJson);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowRuntimeAsync()
                .WithFuel(1_000)
                .WithWallTime(TimeSpan.FromMilliseconds(100))
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(started.Task.IsCompleted);
        Assert.False(result.Succeeded);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.False(resumed.Task.IsCompleted);

        gate.SetResult();

        await resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static BindingDescriptor AbandonedBinding(
        TaskCompletionSource gate,
        TaskCompletionSource started,
        TaskCompletionSource resumed)
        => new(
            "test.abandoned",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            async (_, _, _) =>
            {
                started.SetResult();
                try
                {
#pragma warning disable CA2007 // The continuation must be captured on the pump to reproduce the defect.
                    await gate.Task;
#pragma warning restore CA2007
                    return SandboxValue.FromInt32(42);
                }
                finally
                {
                    resumed.SetResult();
                }
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)))
        {
            IsAsync = true
        };

    private const string CallModuleJson = """
        {
          "id": "async-abandoned-binding",
          "version": "1.0.0",
          "targetSandboxVersion": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "return", "value": { "call": "test.abandoned", "args": [] } }
              ]
            }
          ]
        }
        """;
}
