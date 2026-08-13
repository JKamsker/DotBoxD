using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Plugins.Runtime;

internal static class PluginServerCreation
{
    public static PluginServerCreationState Create(
        IPluginMessageSink? messages,
        Action<SandboxHostBuilder>? configureHost,
        SandboxPolicy? defaultPolicy,
        ExecutionMode executionMode,
        PluginRemoteDebugOptions? remoteDebugOptions)
    {
        if (!Enum.IsDefined(executionMode))
        {
            throw new ArgumentOutOfRangeException(nameof(executionMode));
        }

        remoteDebugOptions?.Validate();
        messages ??= new InMemoryPluginMessageSink();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.AddPluginMessageBindings(messages);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
            configureHost?.Invoke(builder);
        });
        defaultPolicy ??= SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .Build();
        return new PluginServerCreationState(host, defaultPolicy, messages);
    }
}

internal sealed record PluginServerCreationState(
    SandboxHost Host,
    SandboxPolicy Policy,
    IPluginMessageSink Messages);
