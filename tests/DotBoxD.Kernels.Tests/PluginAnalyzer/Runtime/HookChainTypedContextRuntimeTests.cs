using System.Reflection;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.HookChainRuntimeTestCompiler;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class HookChainTypedContextRuntimeTests
{
    private const string TypedContextChainSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public sealed class TypedHookContext
        {
            private readonly HookContext _inner;

            public TypedHookContext(HookContext inner) => _inner = inner;

            public IPluginMessageSink Messages => _inner.Messages;
        }

        public static class TypedUsage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent, TypedHookContext>(
                        ctx => new TypedHookContext(ctx))
                    .Where((e, ctx) => e.Distance <= 5)
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "typed"));
        }
        """;

    [Fact]
    public async Task The_generated_interceptor_preserves_a_typed_hook_context_receiver_and_handler()
    {
        var assembly = Compile(TypedContextChainSource, enableInterceptors: true);

        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: ChainPolicy());
        var configure = assembly.GetType("ChainSample.TypedUsage")!
            .GetMethod("Configure", BindingFlags.Public | BindingFlags.Static)!;
        configure.Invoke(null, [server.Hooks]);

        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-typed", 3));
        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-skip", 10));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("monster-typed", message.TargetId);
        Assert.Equal("typed", message.Message);
    }
}
