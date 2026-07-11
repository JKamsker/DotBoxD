using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

public sealed class CompiledSideEffectMultiCallLimitParityTests
{
    [Fact]
    public async Task Loop_five_sends_with_max_host_calls_3_stops_identically_in_both_modes()
    {
        const string moduleJson = """
        {
          "id": "multi-send-loop-quota",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "i32": 5 },
                  "body": [
                    {
                      "op": "expr",
                      "value": {
                        "call": "host.message.send",
                        "args": [ { "string": "player-1" }, { "string": "batch" } ]
                      }
                    }
                  ]
                },
                { "op": "return", "value": { "i32": 0 } }
              ]
            }
          ]
        }
        """;

        var policy = SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .WithMaxHostCalls(3)
            .Build();

        var (interpreted, interpretedSink) = await MultiCallRunSendWithPolicyAsync(moduleJson, ExecutionMode.Interpreted, policy);
        var (compiled, compiledSink) = await MultiCallRunSendWithPolicyAsync(moduleJson, ExecutionMode.Compiled, policy);

        // Both must fail.
        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, interpreted.Error!.Code);
        Assert.Equal(interpreted.Error.Code, compiled.Error!.Code);

        // At most 3 messages delivered in each mode (quota stops at the 4th attempt).
        Assert.True(interpretedSink.Messages.Count <= 3);
        Assert.Equal(interpretedSink.Messages.Count, compiledSink.Messages.Count);

        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async Task<(SandboxExecutionResult Result, InMemoryPluginMessageSink Sink)> MultiCallRunSendAsync(
        string moduleJson,
        ExecutionMode mode)
    {
        var policy = SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build();
        return await MultiCallRunSendWithPolicyAsync(moduleJson, mode, policy);
    }

    private static async Task<(SandboxExecutionResult Result, InMemoryPluginMessageSink Sink)> MultiCallRunSendWithPolicyAsync(
        string moduleJson,
        ExecutionMode mode,
        SandboxPolicy policy)
    {
        var sink = new InMemoryPluginMessageSink();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
        return (result, sink);
    }

    private static async Task<(SandboxExecutionResult Result, InMemoryPluginMessageSink Sink)> MultiCallRunMixedAsync(
        string moduleJson,
        ExecutionMode mode)
    {
        var sink = new InMemoryPluginMessageSink();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module,
            SandboxPolicyBuilder.Create()
                .GrantHostMessageWrite()
                .GrantLogging()
                .WithFuel(10_000)
                .Build());
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
        return (result, sink);
    }

}
