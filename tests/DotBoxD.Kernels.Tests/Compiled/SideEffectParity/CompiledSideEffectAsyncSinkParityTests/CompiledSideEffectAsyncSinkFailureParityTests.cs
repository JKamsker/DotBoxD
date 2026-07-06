using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

public sealed class CompiledSideEffectAsyncSinkFailureParityTests
{
    [Fact]
    public async Task Async_sink_throwing_OperationCanceledException_with_private_token_maps_to_BindingFailure()
    {
        // Arrange — OCE from a private CTS, NOT the sandbox's token
        var sink = new AsyncSinkAsyncSinkParityTests_CanceledSink();
        var host = CreateHost(sink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var module = await host.ImportJsonAsync(MessageSendModule("oce-private-token"));
        var plan = await host.PrepareAsync(module, policy);

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        // Assert — fails but maps to BindingFailure (private OCE is not sandbox cancellation)
        Assert.False(result.Succeeded);
        // CompiledBindingDispatcher catches OCE not from sandbox CT and maps to BindingFailure.
        // The interpreter routes through the same binding invocation path.
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.Empty(sink.Messages);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static SandboxHost CreateHost(IPluginMessageSink sink)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.AddLogBindings();
            builder.UseInterpreter();
        });

    private static SandboxHost CreateCompiledHost(IPluginMessageSink sink)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    /// <summary>
    /// Module that calls host.message.send once with ("player-1", "hello").
    /// </summary>
    private static string MessageSendModule(string id) => $$"""
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

    /// <summary>
    /// Module that calls host.message.send twice sequentially:
    /// ("player-1", "first") then ("player-2", "second").
    /// </summary>
    private static string MultiSendModule(string id) => $$"""
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

    // -----------------------------------------------------------------------
    // Nested sink implementations — prefixed to avoid collision when files merge
    // -----------------------------------------------------------------------

    /// <summary>
    /// A sink whose SendAsync always yields to the thread pool before recording,
    /// so the CompiledBindingDispatcher pending-await path is exercised on any
    /// code path that invokes the binding.
    /// </summary>
    private sealed class AsyncSinkAsyncSinkParityTests_AsyncYieldSink : IPluginMessageSink
    {
        private readonly List<PluginMessage> _messages = [];

        public IReadOnlyList<PluginMessage> Messages => _messages.AsReadOnly();

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            // Force genuine async completion — ValueTask is NOT already complete.
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            _messages.Add(new PluginMessage(targetId, message));
        }
    }

    /// <summary>
    /// A sink that throws an InvalidOperationException from SendAsync,
    /// simulating a misbehaving external dependency.
    /// </summary>
    private sealed class AsyncSinkAsyncSinkParityTests_ThrowingSink(string detail) : IPluginMessageSink
    {
        private readonly List<PluginMessage> _messages = [];

        public IReadOnlyList<PluginMessage> Messages => _messages.AsReadOnly();

        public ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(detail);
    }

    /// <summary>
    /// A sink that throws OperationCanceledException using its own private CancellationToken
    /// (NOT the sandbox's token), simulating a host-side timeout or abort.
    /// </summary>
    private sealed class AsyncSinkAsyncSinkParityTests_CanceledSink : IPluginMessageSink
    {
        private readonly List<PluginMessage> _messages = [];

        public IReadOnlyList<PluginMessage> Messages => _messages.AsReadOnly();

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            // Cancel with a private token — simulates host-side abort, NOT sandbox cancellation.
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            cts.Token.ThrowIfCancellationRequested();
        }
    }

}
