using static DotBoxD.Kernels.Tests.Compiled.SideEffectParity.AuditParityTestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

/// <summary>
/// Differential / parity tests for audit-success on the compiled side-effecting binding path.
/// PR #27 lifts the effects gate in BindingCallEmitter, allowing bindings declared with
/// CompiledRuntime.CallBinding stubs to run compiled even when they carry capability requirements,
/// external effects, or mandatory audit obligations.  Every test here runs the same module
/// interpreted AND compiled and asserts that every observable — result value, Succeeded,
/// ActualMode, sink deliveries, AuditEvent fields, ResourceUsage, Error.Code — is identical
/// between the two execution paths.  ActualMode==Compiled in the compiled run confirms that the
/// compiled path ran and did not silently fall back to the interpreter.
/// </summary>
public sealed class CompiledSideEffectAuditMessageParityTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // host.message.send — PluginMessageBindings (CapabilityId = host.message.write)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Host_message_send_compiled_audit_fields_match_interpreted_field_for_field()
    {
        // Ensures ALL audit-event fields (Kind, BindingId, CapabilityId, Effect, ResourceId,
        // redacted Message, Fields["messageLength"]) are bit-for-bit identical between modes.
        const string moduleJson = """
        {
          "id": "parity-msg-fields",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
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
                    "call": "host.message.send",
                    "args": [
                      { "string": "player-99" },
                      { "string": "token=s3cr3t hello world" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

        var (interp, interpSink) = await AuditParityMessageRunAsync(moduleJson, ExecutionMode.Interpreted);
        var (comp, compSink) = await AuditParityMessageRunAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interp.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        // Sink delivery — unredacted, identical in both modes.
        var im = Assert.Single(interpSink.Messages);
        var cm = Assert.Single(compSink.Messages);
        Assert.Equal(im.TargetId, cm.TargetId);
        Assert.Equal(im.Message, cm.Message);

        // Audit event: field-for-field parity.
        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "PluginMessage");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "PluginMessage");

        Assert.Equal(ia.Kind, ca.Kind);
        Assert.Equal(ia.BindingId, ca.BindingId);
        Assert.Equal(ia.CapabilityId, ca.CapabilityId);
        Assert.Equal(ia.Effect, ca.Effect);
        Assert.Equal(ia.ResourceId, ca.ResourceId);
        Assert.Equal(ia.Message, ca.Message);
        Assert.Equal(ia.Success, ca.Success);
        Assert.Equal(ia.Fields!["messageLength"], ca.Fields!["messageLength"]);
        Assert.Equal(ia.Fields["resourceKind"], ca.Fields["resourceKind"]);
    }

    [Fact]
    public async Task Host_message_send_compiled_ResourceUsage_HostCalls_matches_interpreted()
    {
        const string moduleJson = """
        {
          "id": "parity-msg-hostcalls",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
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
                    "call": "host.message.send",
                    "args": [ { "string": "player-1" }, { "string": "hello" } ]
                  }
                }
              ]
            }
          ]
        }
        """;

        var (interp, _) = await AuditParityMessageRunAsync(moduleJson, ExecutionMode.Interpreted);
        var (comp, _) = await AuditParityMessageRunAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);
        Assert.Equal(interp.ResourceUsage.HostCalls, comp.ResourceUsage.HostCalls);
        Assert.Equal(1, comp.ResourceUsage.HostCalls);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // log.info — SafeLogBindings (CapabilityId = log.write)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Log_info_compiled_audit_fields_match_interpreted_field_for_field()
    {
        const string moduleJson = """
        {
          "id": "parity-log-info",
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
                    "call": "log.info",
                    "args": [{ "string": "hello parity" }]
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
        Assert.Equal(ia.Fields!["resourceKind"], ca.Fields!["resourceKind"]);

        // Concrete expected values pinned from the binding implementation.
        Assert.Equal("log.info", ca.BindingId);
        Assert.Equal("log.write", ca.CapabilityId);
        Assert.Equal("log:info", ca.ResourceId);
        Assert.Equal("hello parity", ca.Message);
        Assert.Equal("log", ca.Fields["resourceKind"]);
        Assert.Equal(1, comp.ResourceUsage.HostCalls);
        Assert.Equal(1, comp.ResourceUsage.LogEvents);
    }

    [Fact]
    public async Task Log_info_compiled_redacts_secrets_identically_to_interpreted()
    {
        const string moduleJson = """
        {
          "id": "parity-log-info-redact",
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
                    "call": "log.info",
                    "args": [{ "string": "token=abc123 ok" }]
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
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "SandboxLog");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "SandboxLog");

        Assert.Equal(ia.Message, ca.Message);
        Assert.Equal("token=[redacted] ok", ca.Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // log.warn — SafeLogBindings (CapabilityId = log.write)
    // ──────────────────────────────────────────────────────────────────────────

}
