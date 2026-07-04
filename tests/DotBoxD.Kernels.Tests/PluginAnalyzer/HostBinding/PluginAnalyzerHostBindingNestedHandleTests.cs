using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;
using DotBoxD.Services.Attributes;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding
{
    public sealed class PluginAnalyzerHostBindingNestedHandleTests
    {
        private static readonly SourceSpan Span = new(1, 1);

        private const string NestedHandleSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [DotBoxDService]
        public interface IMonsterHandle
        {
            [HostCapability("probe.read.monster.threat", HostBindingEffect.HostStateRead)]
            int GetThreat();
        }

        [DotBoxDService]
        public interface IProbeWorld
        {
            [HostCapability("probe.read.monster", HostBindingEffect.HostStateRead)]
            IMonsterHandle GetMonster(string id);
        }

        public sealed record ProbeEvent(string TargetId, int Threshold);

        [Plugin("nested-host-binding-handle")]
        public sealed partial class ProbeKernel : IEventKernel<ProbeEvent>
        {
            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => ctx.Host<IProbeWorld>().GetMonster(e.TargetId).GetThreat() >= e.Threshold;

            public void Handle(ProbeEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, "matched");
        }
        """;

        [Fact]
        public async Task Host_binding_call_on_a_returned_service_handle_round_trips_through_AddBindingsFrom()
        {
            var package = PluginAnalyzerGeneratedPackageFactory.Create(
                NestedHandleSource,
                "Sample.ProbePluginPackage");

            using var server = PluginServer.Create(
                configureHost: builder => builder.AddBindingsFrom<Sample.IProbeWorld>(new Sample.ProbeWorld()),
                defaultPolicy: ProbeReadPolicy());

            var kernel = await server.InstallAsync(package);
            var adapter = new ProbeEventAdapter();

            Assert.True(await kernel.ShouldHandleAsync(adapter, new ProbeEvent("monster-42", 40)));
        }

        [Fact]
        public async Task Async_handle_factory_returned_service_handle_is_awaited_before_method_invocation()
        {
            var world = new Sample.AsyncProbeWorld();
            using var host = SandboxHost.Create(
                builder => builder.AddBindingsFrom<Sample.IAsyncProbeWorld>(world));
            var module = AsyncHandleBindingModule();
            var plan = await host.PrepareAsync(module, AsyncProbeReadPolicy(allowAsync: true));

            var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal(73, Assert.IsType<I32Value>(result.Value).Value);
            Assert.Equal(1, world.FactoryCalls);
            Assert.Equal(1, world.HandleCalls);
        }

        [Fact]
        public async Task Async_handle_factory_requires_runtime_async_capability_even_when_handle_method_is_sync()
        {
            using var host = SandboxHost.Create(
                builder => builder.AddBindingsFrom<Sample.IAsyncProbeWorld>(new Sample.AsyncProbeWorld()));
            var module = AsyncHandleBindingModule();

            var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
                await host.PrepareAsync(module, AsyncProbeReadPolicy(allowAsync: false)));

            Assert.Contains(
                ex.Diagnostics,
                diagnostic => diagnostic.Code == "E-POLICY-CAP" &&
                              diagnostic.Message.Contains("dotboxd.runtime.async", StringComparison.Ordinal));
        }

        private static SandboxPolicy ProbeReadPolicy()
            => SandboxPolicyBuilder.Create()
                .GrantLogging()
                .GrantHostMessageWrite()
                .Grant("probe.read.*", new { }, SandboxEffect.HostStateRead)
                .WithFuel(100_000)
                .WithMaxHostCalls(1_000)
                .WithWallTime(TimeSpan.FromSeconds(10))
                .Build();

        private static SandboxPolicy AsyncProbeReadPolicy(bool allowAsync)
        {
            var builder = SandboxPolicyBuilder.Create()
                .Grant("probe.read.handle.value", new { }, SandboxEffect.HostStateRead)
                .WithFuel(100_000)
                .WithMaxHostCalls(1_000)
                .WithWallTime(TimeSpan.FromSeconds(10));

            if (allowAsync)
            {
                builder.AllowRuntimeAsync();
            }

            return builder.Build();
        }

        private static SandboxModule AsyncHandleBindingModule()
            => new(
                "async-host-binding-handle",
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
                                    "host.Sample.IAsyncProbeHandle.GetValue",
                                    [new LiteralExpression(SandboxValue.FromString("monster-42"), Span)],
                                    null,
                                    Span),
                                Span)
                        ])
                ],
                new Dictionary<string, string>(StringComparer.Ordinal));

        private sealed record ProbeEvent(string TargetId, int Threshold);

        private sealed class ProbeEventAdapter : IPluginEventAdapter<ProbeEvent>
        {
            public string EventName => "ProbeEvent";

            public IReadOnlyList<Parameter> Parameters { get; } =
            [
                new("e_TargetId", SandboxType.String),
            new("e_Threshold", SandboxType.I32)
            ];

            public IReadOnlyList<SandboxValue> ToSandboxValues(ProbeEvent e)
                =>
                [
                    SandboxValue.FromString(e.TargetId),
                SandboxValue.FromInt32(e.Threshold)
                ];
        }
    }
}

namespace Sample
{
    using DotBoxD.Abstractions;

    [DotBoxDService]
    public interface IMonsterHandle
    {
        [HostCapability("probe.read.monster.threat", HostBindingEffect.HostStateRead)]
        int GetThreat();
    }

    [DotBoxDService]
    public interface IProbeWorld
    {
        [HostCapability("probe.read.monster", HostBindingEffect.HostStateRead)]
        IMonsterHandle GetMonster(string id);
    }

    public sealed class MonsterHandle(string id) : IMonsterHandle
    {
        public int GetThreat() => id == "monster-42" ? 42 : 0;
    }

    public sealed class ProbeWorld : IProbeWorld
    {
        public IMonsterHandle GetMonster(string id) => new MonsterHandle(id);
    }

    [DotBoxDService]
    public interface IAsyncProbeHandle
    {
        [HostCapability("probe.read.handle.value", HostBindingEffect.HostStateRead)]
        int GetValue();
    }

    [DotBoxDService]
    public interface IAsyncProbeWorld
    {
        [HostCapability("probe.read.handle", HostBindingEffect.HostStateRead)]
        Task<IAsyncProbeHandle> GetHandle(string id);
    }

    public sealed class AsyncProbeWorld : IAsyncProbeWorld
    {
        public int FactoryCalls { get; private set; }

        public int HandleCalls { get; private set; }

        public Task<IAsyncProbeHandle> GetHandle(string id)
        {
            FactoryCalls++;
            return Task.FromResult<IAsyncProbeHandle>(new AsyncProbeHandle(id, this));
        }

        private void RecordHandleCall() => HandleCalls++;

        private sealed class AsyncProbeHandle(string id, AsyncProbeWorld owner) : IAsyncProbeHandle
        {
            public int GetValue()
            {
                owner.RecordHandleCall();
                return id == "monster-42" ? 73 : 0;
            }
        }
    }
}
