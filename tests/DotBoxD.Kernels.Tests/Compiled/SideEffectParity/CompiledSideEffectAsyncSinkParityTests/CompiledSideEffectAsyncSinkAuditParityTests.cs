using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using static DotBoxD.Kernels.Tests.Compiled.SideEffectParity.AsyncSinkParityTestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

public sealed class CompiledSideEffectAsyncSinkAuditParityTests
{
    [Fact]
    public async Task Async_yield_sink_audit_event_fields_are_complete_under_interpreter()
    {
        // Arrange
        var sink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var host = CreateHost(sink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var module = await host.ImportJsonAsync(MessageSendModule("audit-field-check"));
        var plan = await host.PrepareAsync(module, policy);

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);

        var audit = Assert.Single(result.AuditEvents, e => e.Kind == "PluginMessage");

        // Assert — binding and capability ids
        Assert.Equal("host.message.send", audit.BindingId);
        Assert.Equal("host.message.write", audit.CapabilityId);

        // Assert — success, no error code
        Assert.True(audit.Success);
        Assert.Null(audit.ErrorCode);

        // Assert — resource id is present
        Assert.False(string.IsNullOrWhiteSpace(audit.ResourceId));

        // Assert — required audit Fields present
        Assert.NotNull(audit.Fields);
        Assert.True(audit.Fields!.ContainsKey("resourceKind"), "resourceKind must be present");
        Assert.True(audit.Fields.ContainsKey("moduleHash"), "moduleHash must be present");
        Assert.True(audit.Fields.ContainsKey("policyHash"), "policyHash must be present");
        Assert.True(audit.Fields.ContainsKey("messageLength"), "messageLength must be present");
        Assert.Equal("5", audit.Fields["messageLength"]); // "hello".Length == 5
    }

    // -----------------------------------------------------------------------
    // Test 6: revoked capability — both interpreted and compiled (no-fallback)
    //         return PolicyDenied. Revocation check runs before the compiler gate.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Revoked_capability_produces_PolicyDenied_in_interpreted_and_compiled()
    {
        // Arrange
        var interpretedSink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var compiledSink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var iHost = CreateHost(interpretedSink);
        var cHost = CreateCompiledHost(compiledSink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var iModule = await iHost.ImportJsonAsync(MessageSendModule("revocation-parity-i"));
        var iPlan = await iHost.PrepareAsync(iModule, policy);
        var cModule = await cHost.ImportJsonAsync(MessageSendModule("revocation-parity-c"));
        var cPlan = await cHost.PrepareAsync(cModule, policy);

        // Revoke before execution
        iHost.RevokeCapability(PluginMessageBindings.CapabilityId, "parity-test-revoked");
        cHost.RevokeCapability(PluginMessageBindings.CapabilityId, "parity-test-revoked");

        // Act
        var iResult = await iHost.ExecuteAsync(
            iPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        var cResult = await cHost.ExecuteAsync(
            cPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        // Assert — both fail with PolicyDenied
        Assert.False(iResult.Succeeded);
        Assert.False(cResult.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, iResult.Error!.Code);
        Assert.Equal(SandboxErrorCode.PolicyDenied, cResult.Error!.Code);

        // Assert — no messages delivered to either sink
        Assert.Empty(interpretedSink.Messages);
        Assert.Empty(compiledSink.Messages);

        // Assert — CapabilityRevoked audit in interpreted result
        var iRevoke = Assert.Single(iResult.AuditEvents, e => e.Kind == "CapabilityRevoked");
        Assert.False(iRevoke.Success);
        Assert.Equal(PluginMessageBindings.CapabilityId, iRevoke.CapabilityId);
        Assert.Equal("parity-test-revoked", iRevoke.Message);

        // Assert — CapabilityRevoked audit in compiled result
        var cRevoke = Assert.Single(cResult.AuditEvents, e => e.Kind == "CapabilityRevoked");
        Assert.False(cRevoke.Success);
        Assert.Equal(PluginMessageBindings.CapabilityId, cRevoke.CapabilityId);
        Assert.Equal("parity-test-revoked", cRevoke.Message);
    }

    // -----------------------------------------------------------------------
    // Test 7: OperationCanceledException from async sink (host-side private token) —
    //         interpreted maps this to BindingFailure; the error does not leak
    //         implementation details.
    // -----------------------------------------------------------------------

}
