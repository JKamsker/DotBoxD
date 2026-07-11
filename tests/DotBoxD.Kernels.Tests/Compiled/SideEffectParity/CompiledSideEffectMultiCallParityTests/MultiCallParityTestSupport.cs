using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

internal static class MultiCallParityTestSupport
{
    internal static async Task<(SandboxExecutionResult Result, InMemoryPluginMessageSink Sink)> MultiCallRunSendAsync(
        string moduleJson,
        ExecutionMode mode)
    {
        var policy = SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build();
        return await MultiCallRunSendWithPolicyAsync(moduleJson, mode, policy);
    }

    internal static async Task<(SandboxExecutionResult Result, InMemoryPluginMessageSink Sink)> MultiCallRunSendWithPolicyAsync(
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

    internal static async Task<(SandboxExecutionResult Result, InMemoryPluginMessageSink Sink)> MultiCallRunMixedAsync(
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
