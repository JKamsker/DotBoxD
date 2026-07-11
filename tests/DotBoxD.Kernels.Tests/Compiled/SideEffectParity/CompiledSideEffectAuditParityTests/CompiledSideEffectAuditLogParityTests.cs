using DotBoxD.Kernels.Sandbox;
using static DotBoxD.Kernels.Tests.Compiled.SideEffectParity.AuditParityTestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

public sealed class CompiledSideEffectAuditLogParityTests
{
    [Fact]
    public async Task Log_warn_compiled_audit_fields_match_interpreted_field_for_field()
    {
        const string moduleJson = """
        {
          "id": "parity-log-warn",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "audit parity" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "log.warn",
                    "args": [{ "string": "careful" }]
                  }
                }
              ]
            }
          ]
        }
        """;

        var interp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Interpreted);
        var comp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interp.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "SandboxLog");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "SandboxLog");

        Assert.Equal(ia.Kind, ca.Kind);
        Assert.Equal(ia.BindingId, ca.BindingId);
        Assert.Equal(ia.CapabilityId, ca.CapabilityId);
        Assert.Equal(ia.Effect, ca.Effect);
        Assert.Equal(ia.ResourceId, ca.ResourceId);
        Assert.Equal(ia.Message, ca.Message);
        Assert.Equal(ia.Success, ca.Success);

        Assert.Equal("log.warn", ca.BindingId);
        Assert.Equal("log.write", ca.CapabilityId);
        Assert.Equal("log:warn", ca.ResourceId);
        Assert.Equal("careful", ca.Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // file.writeText — SafeFileBindings (CapabilityId = file.write)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task File_writeText_compiled_audit_fields_match_interpreted_field_for_field()
    {
        using var temp = AuditParityTempDirectory.Create();
        var existingPath = Path.Combine(temp.Path, "data.txt");
        await File.WriteAllTextAsync(existingPath, "old");

        const string moduleJson = """
        {
          "id": "parity-file-write",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "file.writeText",
                    "args": [
                      { "path": "data.txt" },
                      { "string": "parity-content" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

        var interp = await AuditParityFileWriteRunAsync(temp.Path, moduleJson, ExecutionMode.Interpreted);
        await File.WriteAllTextAsync(existingPath, "old");  // reset for compiled run
        var comp = await AuditParityFileWriteRunAsync(temp.Path, moduleJson, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interp.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        // File was written in both cases.
        Assert.Equal("parity-content", await File.ReadAllTextAsync(existingPath));

        // Audit field-for-field parity.
        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "file.writeText");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "file.writeText");

        Assert.Equal(ia.Kind, ca.Kind);
        Assert.Equal(ia.BindingId, ca.BindingId);
        Assert.Equal(ia.CapabilityId, ca.CapabilityId);
        Assert.Equal(ia.Effect, ca.Effect);
        Assert.Equal(ia.Success, ca.Success);
        Assert.Equal(ia.Bytes, ca.Bytes);
        Assert.Equal(ia.Fields!["resourceKind"], ca.Fields!["resourceKind"]);
        Assert.Equal("file", ca.Fields["resourceKind"]);
        Assert.Equal("file.writeText", ca.BindingId);
        Assert.Equal("file.write", ca.CapabilityId);
        Assert.Equal(SandboxEffect.FileWrite, ca.Effect);
        Assert.True(ca.Success);
    }

    [Fact]
    public async Task File_writeText_compiled_delivers_side_effect_and_increments_HostCalls()
    {
        using var temp = AuditParityTempDirectory.Create();
        var targetPath = Path.Combine(temp.Path, "written.txt");
        await File.WriteAllTextAsync(targetPath, "before");

        const string moduleJson = """
        {
          "id": "parity-file-write-delivery",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "file.writeText",
                    "args": [
                      { "path": "written.txt" },
                      { "string": "compiled-wrote-this" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

        var comp = await AuditParityFileWriteRunAsync(temp.Path, moduleJson, ExecutionMode.Compiled);

        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);
        Assert.Equal("compiled-wrote-this", await File.ReadAllTextAsync(targetPath));
        Assert.Equal(1, comp.ResourceUsage.HostCalls);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Two side-effecting calls in sequence: log.info + log.warn
    // Validates that two compiled side-effecting binding calls in the same
    // module both execute and both emit audit events.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Two_log_calls_compiled_each_emit_audit_event_matching_interpreted()
    {
        const string moduleJson = """
        {
          "id": "parity-two-logs",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "dual call parity" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "log.info", "args": [{ "string": "first" }] } },
                { "op": "return", "value": { "call": "log.warn", "args": [{ "string": "second" }] } }
              ]
            }
          ]
        }
        """;

        var interp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Interpreted);
        var comp = await AuditParityLogRunAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        // Both modes emit exactly 2 SandboxLog events.
        var iLogs = interp.AuditEvents.Where(e => e.Kind == "SandboxLog").ToList();
        var cLogs = comp.AuditEvents.Where(e => e.Kind == "SandboxLog").ToList();
        Assert.Equal(2, iLogs.Count);
        Assert.Equal(2, cLogs.Count);

        // Per-event field parity.
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(iLogs[i].BindingId, cLogs[i].BindingId);
            Assert.Equal(iLogs[i].ResourceId, cLogs[i].ResourceId);
            Assert.Equal(iLogs[i].Message, cLogs[i].Message);
        }

        // ResourceUsage parity.
        Assert.Equal(interp.ResourceUsage.HostCalls, comp.ResourceUsage.HostCalls);
        Assert.Equal(interp.ResourceUsage.LogEvents, comp.ResourceUsage.LogEvents);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Host call quota is enforced identically for compiled side-effecting calls
    // ──────────────────────────────────────────────────────────────────────────

}
