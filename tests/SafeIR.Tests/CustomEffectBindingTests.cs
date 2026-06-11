using SafeIR.Hosting;
using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class CustomEffectBindingTests
{
    [Fact]
    public async Task Custom_effect_binding_requires_policy_grant()
    {
        var host = HostWithCounterBinding(_ => { });
        var module = await host.ParseJsonAsync(CounterModule());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    [Fact]
    public async Task Custom_effect_binding_executes_with_grant()
    {
        var observed = 0;
        var host = HostWithCounterBinding(value => observed += value);
        var module = await host.ParseJsonAsync(CounterModule());
        var plan = await host.PrepareAsync(
            module,
            GameWritePolicy());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(7, ((I32Value)result.Value!).Value);
        Assert.Equal(7, observed);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Custom_effect_binding_compiled_mode_fails_without_interpreter_fallback()
    {
        var host = HostWithCounterBinding(_ => { });
        var module = await host.ParseJsonAsync(CounterModule());
        var plan = await host.PrepareAsync(
            module,
            GameWritePolicy());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Fact]
    public async Task Custom_effect_binding_auto_mode_stays_interpreted()
    {
        var observed = 0;
        var host = HostWithCounterBinding(value => observed += value);
        var module = await host.ParseJsonAsync(CounterModule());
        var plan = await host.PrepareAsync(
            module,
            GameWritePolicy());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Auto });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal(7, observed);
    }

    private static SandboxHost HostWithCounterBinding(Action<int> record)
        => SandboxHost.Create(builder => {
            builder.AddBinding(CounterBinding(record));
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxPolicy GameWritePolicy()
        => new(
            "game-write",
            SandboxEffect.Cpu | SandboxEffect.GameStateWrite,
            [new CapabilityGrant("game.write", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000));

    private static BindingDescriptor CounterBinding(Action<int> record)
        => new(
            "app.counter",
            SemVersion.One,
            [SandboxType.I32],
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.GameStateWrite,
            "game.write",
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.SideEffectingExternal,
            (_, args, _) => {
                var value = ((I32Value)args[0]).Value;
                record(value);
                return ValueTask.FromResult(SandboxValue.FromInt32(value));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static string CounterModule()
        => """
        {
          "id": "custom-effect-binding",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "app.counter", "args": [{ "i32": 7 }] } }]
            }
          ]
        }
        """;
}
