using System.Reflection;
using System.Text.Json;
using DotBoxD.DebugAdapter;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Debugging;
using DotBoxD.Pushdown.Services;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Protocol;

public sealed class DapInspectionHandlerTests
{
    [Fact]
    public void Step_resume_preserves_the_thread_identity_for_the_same_execution()
    {
        var store = new DapStoppedExecutionStore();
        var threadId = store.RecordThread("same-run", "plugin");

        _ = store.RemoveThread(threadId, preserveIdentity: true);
        var resumedThreadId = store.RecordThread("same-run", "plugin");

        Assert.Equal(threadId, resumedThreadId);
        Assert.Equal("same-run", store.RunId(resumedThreadId));
    }

    [Fact]
    public async Task Resuming_one_execution_preserves_a_concurrent_stop()
    {
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false
        });
        const string sessionToken = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        await bridge.PublishAsync(Envelope("session", "bootstrap", sessionToken, new { sessionToken }));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);
        await using var output = new MemoryStream();
        var handler = new DapInspectionHandler(new DapConnection(Stream.Null, output), client, string.Empty);

        await handler.OnRemoteEventAsync(Stopped(sessionToken, "first-run", "first-plugin"));
        handler.BeginResume();
        handler.InvalidateStoppedState(1);
        await handler.OnRemoteEventAsync(Stopped(sessionToken, "second-run", "second-plugin"));

        Assert.Throws<DebugAdapterException>(() => handler.RunId(1));
        Assert.Throws<DebugAdapterException>(() => handler.RunId(2));
        await handler.CompleteResumeAsync();
        Assert.Equal("second-run", handler.RunId(2));
        output.Position = 0;
        var reader = new DapConnection(output, Stream.Null);
        using var first = await reader.ReadAsync(CancellationToken.None);
        using var second = await reader.ReadAsync(CancellationToken.None);
        Assert.False(first!.RootElement.GetProperty("body").GetProperty("allThreadsStopped").GetBoolean());
        Assert.False(second!.RootElement.GetProperty("body").GetProperty("allThreadsStopped").GetBoolean());
    }

    [Fact]
    public async Task Set_expression_translates_the_target_and_value_expression_to_runtime_slots()
    {
        const string sourcePath = "/source/Inspection.cs";
        const string source = "return e.Amount + e.Minimum;";
        await using var bridge = PluginDebugBridge.Start(new PluginDebugBridgeOptions
        {
            WaitForDebuggerBeforeInstall = false,
            SourceReader = _ => System.Text.Encoding.UTF8.GetBytes(source)
        });
        var package = FireDamagePluginPackage.Create();
        var node = SandboxNodeMap.Create(package.Module).Nodes[0];
        var document = KernelDebugDocument.FromSource("inspection", sourcePath, source);
        bridge.RegisterPackage(package with
        {
            DebugInfo = new KernelDebugInfo(
                [document],
                [new KernelSequencePoint(node.Id, new SourceSpan(1, 1, document.Id, 1, source.Length))],
                [
                    new KernelDebugVariableBinding(
                        node.FunctionId,
                        "$event",
                        "e",
                        typeName: "DamageEvent",
                        displayValue: "{DamageEvent}"),
                    new KernelDebugVariableBinding(node.FunctionId, "e_Amount", "e.Amount"),
                    new KernelDebugVariableBinding(node.FunctionId, "e_Minimum", "e.Minimum")
                ])
        });
        var control = new RecordingPluginDebugControl
        {
            ResponseBody = (command, _) => command switch
            {
                PluginDebugCommands.StackTrace => new
                {
                    frames = new[]
                    {
                        new { frameId = "run:0", functionId = node.FunctionId, nodeId = node.Id.Value }
                    }
                },
                PluginDebugCommands.Variables => new
                {
                    arguments = new[]
                    {
                        Variable("e_Amount", 10),
                        Variable("e_Minimum", 2)
                    },
                    locals = Array.Empty<object>()
                },
                PluginDebugCommands.SetExpression => new { value = new { type = "I32", value = 3 } },
                _ => new { }
            }
        };
        bridge.AttachControl(control);
        await bridge.PublishAsync(Envelope("session", "bootstrap", control.SessionToken, new { }));
        await using var client = await BridgeClient.ConnectAsync(
            bridge.Descriptor.PipeName,
            bridge.Descriptor.DiscoveryToken,
            CancellationToken.None);
        await using var output = new MemoryStream();
        var handler = new DapInspectionHandler(new DapConnection(Stream.Null, output), client, string.Empty);
        await handler.OnRemoteEventAsync(Stopped(control.SessionToken, "run", package.Manifest.PluginId));
        using var request = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new
        {
            seq = 1,
            type = "request",
            command = "setExpression",
            arguments = new { frameId = 1, expression = "e.Amount", value = "e.Minimum + 1" }
        }));

        await handler.HandleAsync(request.RootElement, CancellationToken.None);

        var payload = Assert.Single(
            control.Payloads,
            item => item.Command == PluginDebugCommands.SetExpression).Payload;
        Assert.Equal("e_Amount", payload.GetProperty("expression").GetString());
        Assert.Equal("e_Minimum + 1", payload.GetProperty("valueExpression").GetString());

        var store = Assert.IsType<DapVariableStore>(typeof(DapInspectionHandler)
            .GetField("_variableStore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(handler));
        var reference = store.ValueReference(
            JsonSerializer.SerializeToElement(new
            {
                type = "DamageEvent",
                value = "{DamageEvent}",
                children = new[] { new { name = "Amount", value = new { type = "I32", value = 10 } } }
            }),
            "run:0",
            "e");
        using var setVariable = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new
        {
            seq = 2,
            type = "request",
            command = "setVariable",
            arguments = new { variablesReference = reference, name = "Amount", value = "e.Minimum + 1" }
        }));

        await handler.HandleAsync(setVariable.RootElement, CancellationToken.None);

        var variablePayload = control.Payloads
            .Last(item => item.Command == PluginDebugCommands.SetExpression).Payload;
        Assert.Equal("e_Amount", variablePayload.GetProperty("expression").GetString());
        Assert.Equal(JsonValueKind.Null, variablePayload.GetProperty("path").ValueKind);
    }

    private static object Variable(string name, int value) => new
    {
        name,
        kind = "Argument",
        type = "I32",
        assigned = true,
        value = new { type = "I32", value }
    };

    private static PluginDebugEnvelope Stopped(string token, string runId, string pluginId)
        => new(
            PluginDebugProtocol.Version,
            "stopped",
            Guid.NewGuid().ToString("N"),
            token,
            JsonSerializer.SerializeToElement(new
            {
                runId,
                pluginId,
                nodeId = "v1:test",
                reason = "breakpoint"
            }));

    private static byte[] Envelope(string kind, string id, string token, object payload)
        => PluginDebugProtocol.Encode(
            new PluginDebugEnvelope(
                PluginDebugProtocol.Version,
                kind,
                id,
                token,
                JsonSerializer.SerializeToElement(payload)),
            1024 * 1024);
}
