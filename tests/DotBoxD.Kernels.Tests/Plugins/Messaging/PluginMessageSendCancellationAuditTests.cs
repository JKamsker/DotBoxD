using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Messaging;

public sealed class PluginMessageSendCancellationAuditTests
{
    [Fact]
    public async Task Host_message_send_does_not_record_success_audit_after_sink_cancels_caller()
    {
        using var caller = new CancellationTokenSource();
        var messages = new CancellingPluginMessageSink(caller);
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(messages);
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync("""
        {
          "id": "plugin-message-cancels-after-send",
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
                      { "string": "player-1" },
                      { "string": "message" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, null, caller.Token);

        Assert.Equal(1, messages.Calls);
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.DoesNotContain(result.AuditEvents, e =>
            e.Kind == BindingAuditKinds.PluginMessage &&
            e.BindingId == PluginMessageBindings.SendBindingId &&
            e.Success);
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.Equal(SandboxErrorCode.Cancelled, summary.ErrorCode);
    }

    private sealed class CancellingPluginMessageSink(CancellationTokenSource caller) : IPluginMessageSink
    {
        public int Calls { get; private set; }

        public ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            caller.Cancel();
            return ValueTask.CompletedTask;
        }
    }
}
