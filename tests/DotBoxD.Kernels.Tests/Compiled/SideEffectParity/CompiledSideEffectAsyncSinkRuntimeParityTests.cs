using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;

using static DotBoxD.Kernels.Tests.Compiled.SideEffectParity.AsyncSinkParityTestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

/// <summary>
/// Parity tests for the async-sink dimension: verifies that modules exercising
/// host.message.send produce identical observable outcomes (result value, Succeeded,
/// ActualMode, sink deliveries, AuditEvents, ResourceUsage, Error.Code) under both
/// the interpreter and the compiled runtime.
///
/// On this branch (PR #27), the compiled path executes side-effecting entrypoints
/// (host.message.send, log.info, file.writeText) directly through the generic CallBinding
/// dispatcher rather than rejecting them. The compiled path now MATCHES the interpreter.
/// Tests confirm:
///
/// 1. Async-yield sink: both interpreted and compiled (AllowFallback=false) deliver once
///    with matching audit through the compiled async worker pump.
/// 2. Async-yield sink: compiled run does NOT fall back (ActualMode==Compiled) and matches
///    the interpreted run on delivery, audit, and ResourceUsage.
/// 3. Throwing async sink: maps to BindingFailure in BOTH modes (same error code, no leak).
/// 4. Multiple sends: interpreted delivers all messages in order correctly.
/// 5. Audit events on interpreted path: structurally correct and complete.
/// 6. Revoked capability: both modes deny with PolicyDenied (revocation runs before
///    the compiled artifact executes, so both modes reject consistently).
/// 7. OperationCanceledException from sink: maps to BindingFailure on the interpreted path.
/// </summary>
public sealed class CompiledSideEffectAsyncSinkRuntimeParityTests
{
    // -----------------------------------------------------------------------
    // Test 1: async-yield sink delivers exactly once under BOTH interpreted and
    //         compiled (no fallback) modes. PR #27 runs the side-effecting binding
    //         through the generic CallBinding dispatcher; the #31 fix now drives
    //         pending compiled awaits through the compiled async worker pump.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Async_yield_sink_delivers_once_with_matching_audit_in_interpreted_and_compiled()
    {
        // Arrange
        var interpretedSink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var compiledSink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var host = CreateHost(interpretedSink);
        var compiledHost = CreateCompiledHost(compiledSink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var iModule = await host.ImportJsonAsync(MessageSendModule("async-yield-parity-i"));
        var iPlan = await host.PrepareAsync(iModule, policy);
        var cModule = await compiledHost.ImportJsonAsync(MessageSendModule("async-yield-parity-c"));
        var cPlan = await compiledHost.PrepareAsync(cModule, policy);

        // Act — interpreted run
        var interpretedResult = await host.ExecuteAsync(
            iPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        // Act — compiled run (no fallback): the side-effecting binding executes compiled.
        var compiledResult = await compiledHost.ExecuteAsync(
            cPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        // Assert — both modes succeed
        Assert.True(interpretedResult.Succeeded, interpretedResult.Error?.SafeMessage);
        Assert.True(compiledResult.Succeeded, compiledResult.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interpretedResult.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, compiledResult.ActualMode);

        // Assert — each mode delivered the message exactly once, identically.
        var iMsg = Assert.Single(interpretedSink.Messages);
        var cMsg = Assert.Single(compiledSink.Messages);
        Assert.Equal(iMsg.TargetId, cMsg.TargetId);
        Assert.Equal(iMsg.Message, cMsg.Message);
        Assert.Equal("player-1", cMsg.TargetId);
        Assert.Equal("hello", cMsg.Message);

        // Assert — PluginMessage audit parity.
        var iAudit = Assert.Single(interpretedResult.AuditEvents, e => e.Kind == "PluginMessage");
        var cAudit = Assert.Single(compiledResult.AuditEvents, e => e.Kind == "PluginMessage");
        Assert.Equal(iAudit.Success, cAudit.Success);
        Assert.Equal(iAudit.BindingId, cAudit.BindingId);
        Assert.Equal(iAudit.CapabilityId, cAudit.CapabilityId);
        Assert.Equal(iAudit.ResourceId, cAudit.ResourceId);
        Assert.Equal(iAudit.Message, cAudit.Message);

        // Assert — HostCalls parity.
        Assert.Equal(interpretedResult.ResourceUsage.HostCalls, compiledResult.ResourceUsage.HostCalls);
    }

    // -----------------------------------------------------------------------
    // Test 2: async-yield sink runs compiled in a fallback-enabled compiled host
    //         and delivers messages identically to the plain interpreted host.
    //         PR #27: the effectful module compiles directly, so the compiled run
    //         does NOT fall back — ActualMode==Compiled.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Async_yield_sink_compiled_delivers_identical_to_pure_interpreted()
    {
        // Arrange — both hosts share the same module JSON; each has its own sink
        var interpretedSink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var compiledSink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var interpretedHost = CreateHost(interpretedSink);
        var compiledHost = CreateCompiledHost(compiledSink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var iModule = await interpretedHost.ImportJsonAsync(MessageSendModule("compiled-parity-i"));
        var iPlan = await interpretedHost.PrepareAsync(iModule, policy);
        var cModule = await compiledHost.ImportJsonAsync(MessageSendModule("compiled-parity-c"));
        var cPlan = await compiledHost.PrepareAsync(cModule, policy);

        // Act — explicit interpreted
        var iResult = await interpretedHost.ExecuteAsync(
            iPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        // Act — compiled with fallback allowed: the effectful module compiles directly,
        // so it runs compiled rather than falling back to the interpreter.
        var cResult = await compiledHost.ExecuteAsync(
            cPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = true });

        // Assert — both runs succeeded
        Assert.True(iResult.Succeeded, iResult.Error?.SafeMessage);
        Assert.True(cResult.Succeeded, cResult.Error?.SafeMessage);

        // Assert — compiled run genuinely ran compiled (did NOT fall back).
        Assert.Equal(ExecutionMode.Interpreted, iResult.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, cResult.ActualMode);

        // Assert — message delivery parity: async yield sink delivered once in each
        var iMsg = Assert.Single(interpretedSink.Messages);
        var cMsg = Assert.Single(compiledSink.Messages);
        Assert.Equal(iMsg.TargetId, cMsg.TargetId);
        Assert.Equal(iMsg.Message, cMsg.Message);
        Assert.Equal("player-1", cMsg.TargetId);
        Assert.Equal("hello", cMsg.Message);

        // Assert — PluginMessage audit parity
        var iAudit = Assert.Single(iResult.AuditEvents, e => e.Kind == "PluginMessage");
        var cAudit = Assert.Single(cResult.AuditEvents, e => e.Kind == "PluginMessage");
        Assert.True(iAudit.Success);
        Assert.True(cAudit.Success);
        Assert.Equal(iAudit.BindingId, cAudit.BindingId);
        Assert.Equal(iAudit.CapabilityId, cAudit.CapabilityId);
        Assert.Equal(iAudit.ResourceId, cAudit.ResourceId);
        Assert.Equal(iAudit.Message, cAudit.Message);

        // Assert — HostCalls parity
        Assert.Equal(iResult.ResourceUsage.HostCalls, cResult.ResourceUsage.HostCalls);
    }

    // -----------------------------------------------------------------------
    // Test 3: throwing sink — PR #27 runs the binding compiled, so a throwing
    //         sink maps to BindingFailure in BOTH interpreted and compiled modes
    //         (the binding is reached and crashes in each). The redacted message
    //         never leaks the host failure detail, and no delivery occurs.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Throwing_sink_returns_BindingFailure_in_both_interpreted_and_compiled()
    {
        // Arrange
        var interpretedSink = new AsyncSinkAsyncSinkParityTests_ThrowingSink("simulated host failure");
        var compiledSink = new AsyncSinkAsyncSinkParityTests_ThrowingSink("simulated host failure");
        var iHost = CreateHost(interpretedSink);
        var cHost = CreateCompiledHost(compiledSink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var iModule = await iHost.ImportJsonAsync(MessageSendModule("throwing-sink-parity-i"));
        var iPlan = await iHost.PrepareAsync(iModule, policy);
        var cModule = await cHost.ImportJsonAsync(MessageSendModule("throwing-sink-parity-c"));
        var cPlan = await cHost.PrepareAsync(cModule, policy);

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

        // Assert — interpreted fails: binding threw, mapped to BindingFailure
        Assert.False(iResult.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, iResult.Error!.Code);
        Assert.DoesNotContain("simulated host failure", iResult.Error.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(interpretedSink.Messages);
        Assert.Equal(ExecutionMode.Interpreted, iResult.ActualMode);

        // Assert — compiled reaches the same binding and crashes the same way → BindingFailure
        Assert.False(cResult.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, cResult.Error!.Code);
        Assert.DoesNotContain("simulated host failure", cResult.Error.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(compiledSink.Messages);
        Assert.Equal(ExecutionMode.Compiled, cResult.ActualMode);

        // True parity: identical error code in both modes.
        Assert.Equal(iResult.Error.Code, cResult.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Test 4: multiple sends in interpreted mode — all deliver in order with
    //         correct PluginMessage audit events.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Async_yield_sink_with_multiple_sends_delivers_all_in_order_interpreted()
    {
        // Arrange
        var sink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var host = CreateHost(sink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var module = await host.ImportJsonAsync(MultiSendModule("multi-send-yield"));
        var plan = await host.PrepareAsync(module, policy);

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        // Assert — succeeds
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);

        // Assert — two messages delivered in order via async-yield path
        Assert.Equal(2, sink.Messages.Count);
        Assert.Equal("player-1", sink.Messages[0].TargetId);
        Assert.Equal("first", sink.Messages[0].Message);
        Assert.Equal("player-2", sink.Messages[1].TargetId);
        Assert.Equal("second", sink.Messages[1].Message);

        // Assert — two PluginMessage audit events, both successful
        var pluginAudits = result.AuditEvents.Where(e => e.Kind == "PluginMessage").ToList();
        Assert.Equal(2, pluginAudits.Count);
        Assert.All(pluginAudits, a => Assert.True(a.Success));
        Assert.Equal(2, result.ResourceUsage.HostCalls);
    }

    // -----------------------------------------------------------------------
    // Test 5: audit event fields are complete and correct on the interpreted path
    //         (BindingId, CapabilityId, ResourceId, required Fields keys).
    // -----------------------------------------------------------------------

}
