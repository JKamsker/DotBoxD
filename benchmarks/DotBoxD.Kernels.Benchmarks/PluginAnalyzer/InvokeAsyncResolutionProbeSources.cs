using System.Text;

namespace DotBoxD.Kernels.Benchmarks.PluginAnalyzer;

internal static class InvokeAsyncResolutionProbeSources
{
    public const string Infrastructure = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Services.Attributes;

        namespace InvokeAsyncResolution;

        [RpcService]
        public interface IWorldAccess
        {
            [HostBinding("host.world.getHealth", "probe.world.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetHealth(string entityId);
        }

        public interface IWorldServer : IPluginServer<IWorldAccess>
        {
            IWorldServer Services { get; }

            IServerExtensionWireClient WireClient { get; }

            Task<string> EnsureAnonymousKernelAsync(
                string pluginId,
                Func<DotBoxD.Plugins.PluginPackage> factory,
                CancellationToken cancellationToken = default);

            [LowerToIrMethod(LoweredIrMethodKind.AnonymousInvocation)]
            ValueTask<TReturn> ProbeAsync<TReturn>(
                Func<IWorldAccess, ValueTask<TReturn>> lambda,
                CancellationToken cancellationToken = default);

            [LowerToIrMethod((LoweredIrMethodKind)99)]
            ValueTask<TReturn> BrokenAsync<TReturn>(
                Func<IWorldAccess, ValueTask<TReturn>> lambda,
                CancellationToken cancellationToken = default);

            [LowerToIrMethod((LoweredIrMethodKind)99)]
            ValueTask<int> ChooseAsync(Func<IWorldAccess, ValueTask<int>> lambda);

            [LowerToIrMethod(LoweredIrMethodKind.AnonymousInvocation)]
            ValueTask<long> ChooseAsync(Func<IWorldAccess, ValueTask<long>> lambda);
        }

        public sealed class OrdinaryReceiver
        {
            public void Touch() { }

            public void InvokeAsync() { }
        }

        public static class StableUsage
        {
            public static ValueTask<int> Run(IWorldServer server)
                => server.ProbeAsync(async (IWorldAccess world) =>
                {
                    return world.GetHealth("stable");
                });
        }
        """;

    public static string ResolvedCalls(char marker, int count)
        => Calls(marker, count, "receiver.Touch();");

    public static string UserInvokeAsyncCalls(char marker, int count)
        => Calls(marker, count, "receiver.InvokeAsync();");

    public static string UnresolvedCalls(char marker, int count)
        => Calls(marker, count, "missing.InvokeAsync();");

    public static string CustomCalls(char marker, int count)
        => Calls(
            marker,
            count,
            "_ = server.ProbeAsync(async (IWorldAccess world) => { return world.GetHealth(\"custom\"); });");

    public static string DiagnosticCalls(char marker, int count)
    {
        var source = Header(marker);
        source.AppendLine("        _ = server.ChooseAsync(null!);");
        AppendCalls(
            source,
            count,
            "_ = server.BrokenAsync(async (IWorldAccess world) => { return world.GetHealth(\"broken\"); });");
        return Footer(source);
    }

    private static string Calls(char marker, int count, string statement)
    {
        var source = Header(marker);
        AppendCalls(source, count, statement);
        return Footer(source);
    }

    private static StringBuilder Header(char marker)
        => new StringBuilder()
            .Append("// edit ").Append(marker).AppendLine()
            .AppendLine("namespace InvokeAsyncResolution;")
            .AppendLine("public static class Workload")
            .AppendLine("{")
            .AppendLine("    public static void Execute(IWorldServer server, OrdinaryReceiver receiver)")
            .AppendLine("    {");

    private static void AppendCalls(StringBuilder source, int count, string statement)
    {
        for (var i = 0; i < count; i++)
        {
            source.Append("        ").AppendLine(statement);
        }
    }

    private static string Footer(StringBuilder source)
        => source.AppendLine("    }").AppendLine("}").ToString();
}
