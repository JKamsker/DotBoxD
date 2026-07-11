using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

public sealed class CompiledSideEffectMidRunCancellationParityTests
{
    [Fact]
    public async Task Mid_run_cancellation_blocks_blocking_sink_delivery_in_interpreted_mode()
    {
        // Arrange: the sink blocks (awaiting a TCS) so cancellation fires while the binding is pending
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var blockingSink = new CancellationTcsMessageSink(tcs.Task, cts.Token);

        using var host = CancellationMessageHost(blockingSink);
        var module = await host.ImportJsonAsync(CancellationSendModuleJson("cancellation-mid-run-interp"));
        var plan = await host.PrepareAsync(module, CancellationMessagePolicy());

        // Cancel after a short delay to simulate mid-run cancellation
        var cancelTask = Task.Run(async () =>
        {
            await Task.Delay(50);
            await cts.CancelAsync();
        });

        // Act
        var result = await host.ExecuteAsync(
            plan, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            cts.Token);
        await cancelTask;

        // Assert: Cancelled error, no committed message
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
        Assert.False(blockingSink.WasCommitted);
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private static SandboxHost CancellationMessageHost(IPluginMessageSink sink)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxHost CancellationLogHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxHost CancellationPureHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(CancellationInstantPureBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxHost CancellationSlowPureHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(CancellationSlowPureBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static SandboxPolicy CancellationMessagePolicy()
        => SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build();

    private static string CancellationSendModuleJson(string id)
        => $$"""
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
                    "args": [{ "string": "player-1" }, { "string": "ping" }]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string CancellationLogModuleJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write" }],
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
                    "args": [{ "string": "should-not-emit" }]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string CancellationPureModuleJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "test.cancellation.instant", "args": [] }
                }
              ]
            }
          ]
        }
        """;

    private static string CancellationSlowPureModuleJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "test.cancellation.slow", "args": [] }
                }
              ]
            }
          ]
        }
        """;

    /// <summary>
    /// A pure binding that returns instantly. Used to exercise pre-cancelled-token
    /// detection before the binding itself does any work.
    /// </summary>
    private static BindingDescriptor CancellationInstantPureBinding()
        => new(
            "test.cancellation.instant",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.FromInt32(42)),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    /// <summary>
    /// A pure binding that blocks until the wall-time cancellation token fires,
    /// simulating a long-running computation that triggers the wall-time budget.
    /// </summary>
    private static BindingDescriptor CancellationSlowPureBinding()
        => new(
            "test.cancellation.slow",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            async (_, _, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                return SandboxValue.FromInt32(0);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)))
        { IsAsync = true };

    // ---------------------------------------------------------------------------
    // Nested helpers (prefixed with "Cancellation" to avoid collisions when test
    // files are combined)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A message sink that blocks inside <c>SendAsync</c> until an external
    /// <see cref="TaskCompletionSource"/> releases it, and then records delivery
    /// only after the gate opens (never when cancelled before the gate fires).
    /// </summary>
    private sealed class CancellationBlockingMessageSink : IPluginMessageSink
    {
        private readonly List<PluginMessage> _committed = [];

        public IReadOnlyList<PluginMessage> CommittedMessages => _committed.AsReadOnly();

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            // Block for long enough that the wall-time timeout fires first.
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            // Only reached if NOT cancelled — represents a committed side effect.
            _committed.Add(new PluginMessage(targetId, message));
        }
    }

    /// <summary>
    /// A message sink that waits on a <see cref="Task"/> gate, then commits only
    /// when the gate opens AND the run token has not yet fired.
    /// </summary>
    private sealed class CancellationTcsMessageSink(Task gate, CancellationToken runToken) : IPluginMessageSink
    {
        private bool _committed;

        public bool WasCommitted => _committed;

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            // Wait for the gate (or for either cancel token to fire).
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            runToken.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            _committed = true;
        }
    }

}
