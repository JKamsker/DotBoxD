using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

internal static class CancellationParityTestSupport
{
    internal static SandboxHost CancellationMessageHost(IPluginMessageSink sink)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    internal static SandboxHost CancellationLogHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    internal static SandboxHost CancellationPureHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(CancellationInstantPureBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    internal static SandboxHost CancellationSlowPureHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(CancellationSlowPureBinding());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    internal static SandboxPolicy CancellationMessagePolicy()
        => SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build();

    internal static string CancellationSendModuleJson(string id)
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

    internal static string CancellationLogModuleJson(string id)
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

    internal static string CancellationPureModuleJson(string id)
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

    internal static string CancellationSlowPureModuleJson(string id)
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
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

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
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))
        { IsAsync = true };

    internal sealed class CancellationBlockingMessageSink : IPluginMessageSink
    {
        private readonly List<PluginMessage> _committed = [];

        public IReadOnlyList<PluginMessage> CommittedMessages => _committed.AsReadOnly();

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            _committed.Add(new PluginMessage(targetId, message));
        }
    }

    internal sealed class CancellationTcsMessageSink(Task gate, CancellationToken runToken) : IPluginMessageSink
    {
        private bool _committed;

        public bool WasCommitted => _committed;

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            runToken.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            _committed = true;
        }
    }
}
