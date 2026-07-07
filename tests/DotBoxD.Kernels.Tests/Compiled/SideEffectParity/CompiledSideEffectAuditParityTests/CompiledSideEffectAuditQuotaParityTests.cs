using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

public sealed class CompiledSideEffectAuditQuotaParityTests
{
    [Fact]
    public async Task Log_info_quota_exceeded_error_code_matches_between_interpreted_and_compiled()
    {
        const string moduleJson = """
        {
          "id": "parity-log-quota",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "quota parity" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "log.info", "args": [{ "string": "first" }] } },
                { "op": "return", "value": { "call": "log.info", "args": [{ "string": "second" }] } }
              ]
            }
          ]
        }
        """;

        var interp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Interpreted, maxLogEvents: 1);
        var comp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Compiled, maxLogEvents: 1);

        Assert.False(interp.Succeeded);
        Assert.False(comp.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, interp.Error!.Code);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, comp.Error!.Code);
        Assert.Equal(interp.Error.Code, comp.Error.Code);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static async Task<(SandboxExecutionResult Result, InMemoryPluginMessageSink Sink)> AuditParityMessageRunAsync(
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

    private static async Task<SandboxExecutionResult> AuditParityLogRunAsync(
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

    private static async Task<SandboxExecutionResult> AuditParityFileWriteRunAsync(
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

    // Scoped temp-directory helper — defined locally so this file is self-contained
    // and cannot collide with helpers in other test files.
    private sealed class AuditParityTempDirectory : IDisposable
    {
        private AuditParityTempDirectory(string path) => Path = path;

        public string Path { get; }

        public static AuditParityTempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotboxd-parity-" + Guid.NewGuid().ToString("N"));
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
