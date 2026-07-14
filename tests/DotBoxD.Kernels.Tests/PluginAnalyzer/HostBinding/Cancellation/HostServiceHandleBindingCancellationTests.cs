using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Services.Attributes;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding
{
    public sealed class HostServiceHandleBindingCancellationTests
    {
        private static readonly SourceSpan Span = new(1, 1);
        private static readonly string BindingId =
            $"host.{typeof(CancellationProbe.IProbeHandle).FullName}.{nameof(CancellationProbe.IProbeHandle.GetValue)}";

        [Fact]
        public async Task Handle_factory_cancellation_prevents_handle_method_invocation()
        {
            using var cancellation = new CancellationTokenSource();
            var world = new CancellationProbe.CancelingProbeWorld(cancellation.Cancel);
            using var host = SandboxHost.Create(
                builder => builder.AddBindingsFrom<CancellationProbe.IProbeWorld>(world));
            var plan = await host.PrepareAsync(HandleBindingModule(), ProbePolicy());

            var result = await host.ExecuteAsync(
                plan,
                "main",
                SandboxValue.Unit,
                new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
                cancellation.Token);

            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
            Assert.Equal(1, world.FactoryCalls);
            Assert.Equal(0, world.HandleCalls);
            Assert.DoesNotContain(
                result.AuditEvents,
                e => e.Kind == "BindingCall" && e.BindingId == BindingId && e.Success);
        }

        private static SandboxPolicy ProbePolicy()
            => SandboxPolicyBuilder.Create()
                .Grant("probe.read.handle.value", new { }, SandboxEffect.HostStateRead)
                .WithFuel(1_000)
                .WithMaxHostCalls(10)
                .WithWallTime(TimeSpan.FromSeconds(1))
                .Build();

        private static SandboxModule HandleBindingModule()
            => new(
                "canceling-host-service-handle-binding-probe",
                SemVersion.One,
                SemVersion.One,
                [],
                [
                    new SandboxFunction(
                        "main",
                        true,
                        [],
                        SandboxType.I32,
                        [
                            new ReturnStatement(
                                new CallExpression(
                                    BindingId,
                                    [new LiteralExpression(SandboxValue.FromString("probe-42"), Span)],
                                    null,
                                    Span),
                                Span)
                        ])
                ],
                new Dictionary<string, string>(StringComparer.Ordinal));
    }
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.CancellationProbe
{
    [RpcService(Name = "HostServiceHandleCancellationProbeHandle")]
    public interface IProbeHandle
    {
        [HostCapability("probe.read.handle.value", HostBindingEffect.HostStateRead)]
        int GetValue();
    }

    [RpcService(Name = "HostServiceHandleCancellationProbeWorld")]
    public interface IProbeWorld
    {
        [HostCapability("probe.read.handle", HostBindingEffect.HostStateRead)]
        IProbeHandle GetHandle(string id);
    }

    public sealed class CancelingProbeWorld(Action cancel) : IProbeWorld
    {
        private int _factoryCalls;
        private int _handleCalls;

        public int FactoryCalls => Volatile.Read(ref _factoryCalls);

        public int HandleCalls => Volatile.Read(ref _handleCalls);

        public IProbeHandle GetHandle(string id)
        {
            Interlocked.Increment(ref _factoryCalls);
            cancel();
            return new ProbeHandle(id, this);
        }

        private void RecordHandleCall() => Interlocked.Increment(ref _handleCalls);

        private sealed class ProbeHandle(string id, CancelingProbeWorld owner) : IProbeHandle
        {
            public int GetValue()
            {
                owner.RecordHandleCall();
                return id == "probe-42" ? 42 : 0;
            }
        }
    }
}
