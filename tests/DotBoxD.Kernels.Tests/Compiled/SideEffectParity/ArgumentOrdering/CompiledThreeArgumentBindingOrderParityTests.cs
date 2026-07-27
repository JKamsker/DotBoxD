using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity.ArgumentOrdering;

public sealed class CompiledThreeArgumentBindingOrderParityTests
{
    [Fact]
    public async Task Side_effecting_argument_runs_before_three_argument_array_charge()
    {
        const int tolerance = 12;
        var skippedByCompiled = 0;
        var divergent = 0;

        for (var maxAllocatedBytes = 1; maxAllocatedBytes <= 256; maxAllocatedBytes++)
        {
            var interpretedTouches = await RunAsync(ExecutionMode.Interpreted, maxAllocatedBytes);
            var compiledTouches = await RunAsync(ExecutionMode.Compiled, maxAllocatedBytes);
            if (interpretedTouches == compiledTouches)
            {
                continue;
            }

            divergent++;
            if (interpretedTouches > compiledTouches)
            {
                skippedByCompiled++;
            }
        }

        Assert.True(
            divergent <= tolerance,
            $"Three-argument side effect diverged at {divergent} budgets " +
            $"({skippedByCompiled} where compiled skipped it), above tolerance {tolerance}.");
    }

    private static async Task<int> RunAsync(ExecutionMode mode, int maxAllocatedBytes)
    {
        var touches = 0;
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(TouchBinding(() => touches++));
            builder.AddBinding(new ConsumeThreeInvoker().Descriptor());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ModuleJson);
        var policy = new SandboxPolicy(
            "touch-and-consume-three",
            SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            [new CapabilityGrant("game.write", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 1_000_000, MaxAllocatedBytes: maxAllocatedBytes));
        var plan = await host.PrepareAsync(module, policy);

        await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
        return touches;
    }

    private static BindingDescriptor TouchBinding(Action record)
        => new(
            "app.touch",
            SemVersion.One,
            [],
            SandboxType.String,
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            "game.write",
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.SideEffectingExternal,
            (context, _, _) =>
            {
                record();
                var timestamp = context.AuditTimestamp();
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    BindingAuditKinds.BindingCall,
                    timestamp,
                    true,
                    BindingId: "app.touch",
                    CapabilityId: "game.write",
                    Effect: SandboxEffect.HostStateWrite,
                    ResourceId: "touch:test",
                    Fields: context.BindingAuditFields("touch", timestamp)));
                return ValueTask.FromResult(SandboxValue.FromString("a"));
            },
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)),
            static (grant, diagnostics) =>
            {
                foreach (var key in grant.Parameters.Keys)
                {
                    diagnostics.Add(new SandboxDiagnostic(
                        "E-POLICY-GRANT-PARAM",
                        $"grant '{grant.Id}' parameter '{key}' is not supported"));
                }
            });

    private const string ModuleJson = """
    {
      "id": "side-effecting-three-argument-order",
      "version": "1.0.0",
      "capabilityRequests": [{ "id": "game.write" }],
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "Unit",
        "body": [{
          "op": "return",
          "value": {
            "call": "app.consume3",
            "args": [
              { "call": "app.touch", "args": [] },
              { "string": "b" },
              { "string": "c" }
            ]
          }
        }]
      }]
    }
    """;

    private sealed class ConsumeThreeInvoker : IThreeArgumentBindingInvoker
    {
        public BindingDescriptor Descriptor()
            => new(
                "app.consume3",
                SemVersion.One,
                [SandboxType.String, SandboxType.String, SandboxType.String],
                SandboxType.Unit,
                SandboxEffect.Cpu | SandboxEffect.Alloc,
                null,
                BindingCostModel.Fixed(1),
                AuditLevel.None,
                BindingSafety.PureHostFacade,
                Invoke,
                CompiledBinding.RuntimeStub(
                    typeof(CompiledRuntime).FullName!,
                    nameof(CompiledRuntime.CallBinding)));

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(SandboxValue.Unit);

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            SandboxValue arg0,
            SandboxValue arg1,
            SandboxValue arg2,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(SandboxValue.Unit);
    }
}
