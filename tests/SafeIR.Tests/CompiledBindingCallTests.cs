using SafeIR;
using SafeIR.Hosting;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class CompiledBindingCallTests
{
    [Fact]
    public async Task Compiled_mode_routes_host_binding_calls_through_runtime_stub()
    {
        var host = SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.AddBinding(DoubleBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ParseJsonAsync("""
        {
          "id": "compiled-binding-call",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "test.double", "args": [{ "i32": 21 }] } }]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var interpreted = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(((I32Value)interpreted.Value!).Value, ((I32Value)compiled.Value!).Value);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.Equal(42, ((I32Value)compiled.Value!).Value);
        Assert.Equal(1, compiled.ResourceUsage.HostCalls);
    }

    private static BindingDescriptor DoubleBinding()
        => new(
            "test.double",
            SemVersion.One,
            [SandboxType.I32],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, args, _) => {
                var value = ((I32Value)args[0]).Value;
                return ValueTask.FromResult(SandboxValue.FromInt32(value * 2));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}
