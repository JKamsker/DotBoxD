using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

internal static class AuditParityTestSupport
{
    internal static async Task<(SandboxExecutionResult Result, InMemoryPluginMessageSink Sink)> AuditParityMessageRunAsync(
        string moduleJson,
        ExecutionMode mode)
    {
        var sink = new InMemoryPluginMessageSink();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build());
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
        return (result, sink);
    }

    internal static async Task<SandboxExecutionResult> AuditParityLogRunAsync(
        string moduleJson,
        ExecutionMode mode,
        int? maxLogEvents = null)
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(moduleJson);
        var policyBuilder = SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(10_000);
        if (maxLogEvents.HasValue)
        {
            policyBuilder = policyBuilder.WithMaxLogEvents(maxLogEvents.Value);
        }

        var plan = await host.PrepareAsync(module, policyBuilder.Build());
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    internal static async Task<SandboxExecutionResult> AuditParityFileWriteRunAsync(
        string root,
        string moduleJson,
        ExecutionMode mode)
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddFileBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .GrantFileWrite(root, 1024, allowCreate: false, allowOverwrite: true)
            .WithFuel(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build());
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    internal sealed class AuditParityTempDirectory : IDisposable
    {
        private AuditParityTempDirectory(string path) => Path = path;

        public string Path { get; }

        public static AuditParityTempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotboxd-parity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new AuditParityTempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
