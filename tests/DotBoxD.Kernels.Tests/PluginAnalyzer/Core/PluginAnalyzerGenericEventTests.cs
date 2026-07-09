using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core
{

    public sealed class PluginAnalyzerGenericEventTests
    {
        [Fact]
        public async Task Generated_package_matches_convention_adapter_for_generic_event_type()
        {
            var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

            public sealed record GenericDamageEvent<T>(T Payload, string TargetId, string Message);

            [Plugin("generated-generic-event")]
            public sealed partial class GenericDamageKernel : IEventKernel<GenericDamageEvent<string>>
            {
                public bool ShouldHandle(GenericDamageEvent<string> e, HookContext ctx)
                    => e.Payload == "fire";

                public void Handle(GenericDamageEvent<string> e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """, "DotBoxD.Kernels.Tests.PluginAnalyzer.Core.GenericDamagePluginPackage");
            var messages = new InMemoryPluginMessageSink();
            var server = PluginAddendumTestPolicies.CreateServer(messages);
            var kernel = await server.InstallAsync(package);

            server.Hooks.On<GenericDamageEvent<string>>().Use(kernel);
            await server.Hooks.PublishAsync(new GenericDamageEvent<string>("ice", "player-1", "ignored"));
            await server.Hooks.PublishAsync(new GenericDamageEvent<string>("fire", "player-1", "generic matched"));

            var message = Assert.Single(messages.Messages);
            Assert.Equal("player-1", message.TargetId);
            Assert.Equal("generic matched", message.Message);
        }

        [Fact]
        public void Hook_registry_allows_distinct_closed_generic_event_adapter_shapes()
        {
            var server = PluginAddendumTestPolicies.CreateServer();
            _ = server.Hooks.On<GenericDamageEvent<int>>();

            var pipeline = server.Hooks.On<GenericDamageEvent<string>>();

            Assert.NotNull(pipeline);
        }

        [Fact]
        public void Hook_registry_keeps_cross_namespace_closed_generic_event_names_separate()
        {
            var server = PluginAddendumTestPolicies.CreateServer();
            _ = server.Hooks.On<CrossNamespaceA.SharedGenericEvent<int>>();
            _ = server.Hooks.On<CrossNamespaceB.SharedGenericEvent<int>>();

            Assert.True(server.Events.TryResolveErased(typeof(CrossNamespaceB.SharedGenericEvent<int>).FullName!, out var erased));
            Assert.Equal(typeof(CrossNamespaceB.SharedGenericEvent<int>), erased.EventType);
        }

        [Fact]
        public void Hook_registry_resolves_nested_closed_generic_event_names_from_canonical_name()
        {
            var server = PluginAddendumTestPolicies.CreateServer();
            server.Events.Register(new SimpleNameAdapter<NamespaceB.SharedGenericEvent<int>>());
            var eventName = typeof(PluginAnalyzerGenericEventTests).FullName + ".NamespaceB.SharedGenericEvent<Int32>";

            Assert.True(server.Events.TryResolveErased(eventName, out var erased));
            Assert.Equal(typeof(NamespaceB.SharedGenericEvent<int>), erased.EventType);
        }

        private static class NamespaceA
        {
            public sealed record SharedGenericEvent<T>(T Payload);
        }

        private static class NamespaceB
        {
            public sealed record SharedGenericEvent<T>(T Payload);
        }

        private sealed class SimpleNameAdapter<TEvent> : IPluginEventAdapter<TEvent>
        {
            public string EventName => "SharedGenericEvent";

            public IReadOnlyList<Parameter> Parameters => [];

            public IReadOnlyList<SandboxValue> ToSandboxValues(TEvent e) => [];
        }
    }

    internal sealed record GenericDamageEvent<T>(T Payload, string TargetId, string Message);
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core.CrossNamespaceA
{
    internal sealed record SharedGenericEvent<T>(T Payload);
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core.CrossNamespaceB
{
    internal sealed record SharedGenericEvent<T>(T Payload);
}
