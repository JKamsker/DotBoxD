using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

internal static class AsyncSinkParityTestSupport
{
    internal static SandboxHost CreateHost(IPluginMessageSink sink)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.AddLogBindings();
            builder.UseInterpreter();
        });

    internal static SandboxHost CreateCompiledHost(IPluginMessageSink sink)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    internal static string MessageSendModule(string id) => $$"""
        {
          "id": "{{id}}",
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
                      { "string": "hello" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    internal static string MultiSendModule(string id) => $$"""
        {
          "id": "{{id}}",
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
                  "op": "expr",
                  "value": {
                    "call": "host.message.send",
                    "args": [
                      { "string": "player-1" },
                      { "string": "first" }
                    ]
                  }
                },
                {
                  "op": "return",
                  "value": {
                    "call": "host.message.send",
                    "args": [
                      { "string": "player-2" },
                      { "string": "second" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    internal sealed class AsyncSinkAsyncSinkParityTests_AsyncYieldSink : IPluginMessageSink
    {
        private readonly List<PluginMessage> _messages = [];

        public IReadOnlyList<PluginMessage> Messages => _messages.AsReadOnly();

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            _messages.Add(new PluginMessage(targetId, message));
        }
    }

    internal sealed class AsyncSinkAsyncSinkParityTests_ThrowingSink(string detail) : IPluginMessageSink
    {
        private readonly List<PluginMessage> _messages = [];

        public IReadOnlyList<PluginMessage> Messages => _messages.AsReadOnly();

        public ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(detail);
    }

    internal sealed class AsyncSinkAsyncSinkParityTests_CanceledSink : IPluginMessageSink
    {
        private readonly List<PluginMessage> _messages = [];

        public IReadOnlyList<PluginMessage> Messages => _messages.AsReadOnly();

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            cts.Token.ThrowIfCancellationRequested();
        }
    }
}
