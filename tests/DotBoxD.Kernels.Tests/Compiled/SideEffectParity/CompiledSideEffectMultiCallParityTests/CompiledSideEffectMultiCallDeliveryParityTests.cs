using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Policies;
using static DotBoxD.Kernels.Tests.Compiled.SideEffectParity.MultiCallParityTestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

public sealed class CompiledSideEffectMultiCallDeliveryParityTests
{
    [Fact]
    public async Task Mixed_pure_arithmetic_and_two_sends_return_correct_value_and_deliver_sink()
    {
        // Compute a value from pure arithmetic, send it as a message, then
        // send another message, then return the computed integer.
        // This stresses the interaction between the fast I32 path and CallBinding2.
        const string moduleJson = """
        {
          "id": "multi-send-mixed",
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
                  "op": "set",
                  "name": "score",
                  "value": { "op": "mul", "left": { "i32": 6 }, "right": { "i32": 7 } }
                },
                {
                  "op": "expr",
                  "value": { "call": "host.message.send", "args": [{ "string": "player-1" }, { "string": "score-computed" }] }
                },
                {
                  "op": "expr",
                  "value": { "call": "host.message.send", "args": [{ "string": "player-2" }, { "string": "done" }] }
                },
                { "op": "return", "value": { "var": "score" } }
              ]
            }
          ]
        }
        """;

        var (interpreted, interpretedSink) = await MultiCallRunSendAsync(moduleJson, ExecutionMode.Interpreted);
        var (compiled, compiledSink) = await MultiCallRunSendAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);

        // Return value: 6 * 7 = 42.
        Assert.Equal(42, ((I32Value)interpreted.Value!).Value);
        Assert.Equal(42, ((I32Value)compiled.Value!).Value);

        // Sink: 2 messages in each mode, same content.
        Assert.Equal(2, interpretedSink.Messages.Count);
        Assert.Equal(interpretedSink.Messages.Count, compiledSink.Messages.Count);
        for (var i = 0; i < interpretedSink.Messages.Count; i++)
        {
            Assert.Equal(interpretedSink.Messages[i].TargetId, compiledSink.Messages[i].TargetId);
            Assert.Equal(interpretedSink.Messages[i].Message, compiledSink.Messages[i].Message);
        }

        // HostCalls.
        Assert.Equal(2, interpreted.ResourceUsage.HostCalls);
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
    }

    // -------------------------------------------------------------------------
    // 6. Mixed: log.info (1-arg SideEffectingExternal, CallBinding path)
    //    interleaved with host.message.send (2-arg, CallBinding2 path).
    //    Verifies audit ordering across two different side-effecting bindings.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Log_then_send_then_log_audit_order_matches_interpreted()
    {
        const string moduleJson = """
        {
          "id": "multi-mixed-log-send",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "log.write", "reason": "operational trace" },
            { "id": "host.message.write" }
          ],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "log.info", "args": [{ "string": "before-send" }] } },
                { "op": "expr", "value": { "call": "host.message.send", "args": [{ "string": "player-1" }, { "string": "payload" }] } },
                { "op": "return", "value": { "call": "log.info", "args": [{ "string": "after-send" }] } }
              ]
            }
          ]
        }
        """;

        var (interpreted, interpretedSink) = await MultiCallRunMixedAsync(moduleJson, ExecutionMode.Interpreted);
        var (compiled, compiledSink) = await MultiCallRunMixedAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);

        // Sink: 1 message in each mode.
        var interpretedMsg = Assert.Single(interpretedSink.Messages);
        var compiledMsg = Assert.Single(compiledSink.Messages);
        Assert.Equal(interpretedMsg.TargetId, compiledMsg.TargetId);
        Assert.Equal(interpretedMsg.Message, compiledMsg.Message);

        // Audit event ORDER must be identical: SandboxLog, PluginMessage, SandboxLog.
        var interpretedSideEffectAudits = interpreted.AuditEvents
            .Where(e => e.Kind is "SandboxLog" or "PluginMessage")
            .ToList();
        var compiledSideEffectAudits = compiled.AuditEvents
            .Where(e => e.Kind is "SandboxLog" or "PluginMessage")
            .ToList();

        Assert.Equal(3, interpretedSideEffectAudits.Count);
        Assert.Equal(interpretedSideEffectAudits.Count, compiledSideEffectAudits.Count);

        // Kind sequence is identical.
        Assert.Equal(
            interpretedSideEffectAudits.Select(e => e.Kind).ToArray(),
            compiledSideEffectAudits.Select(e => e.Kind).ToArray());

        // Log messages preserved identically.
        Assert.Equal(
            interpretedSideEffectAudits.Select(e => e.Message).ToArray(),
            compiledSideEffectAudits.Select(e => e.Message).ToArray());

        // HostCalls: 3 in each mode (2 log.info + 1 host.message.send).
        Assert.Equal(3, interpreted.ResourceUsage.HostCalls);
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
    }

    // -------------------------------------------------------------------------
    // 7. Host-call quota stops both modes at the same point.
    //    With MaxHostCalls=2, the third send must fail with QuotaExceeded.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Three_sends_with_max_host_calls_2_fails_identically_in_both_modes()
    {
        const string moduleJson = """
        {
          "id": "multi-send-quota",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "host.message.send", "args": [{ "string": "player-1" }, { "string": "first" }] } },
                { "op": "expr", "value": { "call": "host.message.send", "args": [{ "string": "player-2" }, { "string": "second" }] } },
                { "op": "return", "value": { "call": "host.message.send", "args": [{ "string": "player-3" }, { "string": "third" }] } }
              ]
            }
          ]
        }
        """;

        var policy = SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .WithMaxHostCalls(2)
            .Build();

        var (interpreted, interpretedSink) = await MultiCallRunSendWithPolicyAsync(moduleJson, ExecutionMode.Interpreted, policy);
        var (compiled, compiledSink) = await MultiCallRunSendWithPolicyAsync(moduleJson, ExecutionMode.Compiled, policy);

        // Both must fail.
        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, interpreted.Error!.Code);
        Assert.Equal(interpreted.Error.Code, compiled.Error!.Code);

        // Both stop before the third send.
        Assert.True(interpretedSink.Messages.Count <= 2);
        Assert.Equal(interpretedSink.Messages.Count, compiledSink.Messages.Count);

        // Host-call counter identical.
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
    }

    // -------------------------------------------------------------------------
    // 8. Loop with early quota stop: loop tries 5 sends but MaxHostCalls=3.
    //    Both modes must stop after the same iteration and count.
    // -------------------------------------------------------------------------

}
