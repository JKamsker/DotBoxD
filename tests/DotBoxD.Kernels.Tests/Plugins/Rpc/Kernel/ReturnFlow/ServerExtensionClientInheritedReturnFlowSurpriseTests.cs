using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc.Kernel.ReturnFlow;

public sealed class ServerExtensionClientInheritedReturnFlowSurpriseTests
{
    [Fact]
    public void Generated_client_reports_conflicting_inherited_return_flow_attributes()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            [RpcService]
            public interface IRemoteControl
            {
            }

            public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
            {
                public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public interface ILeftEchoService
            {
                [return: MaybeNull]
                ValueTask<string> EchoAsync(CancellationToken cancellationToken = default);
            }

            public interface IRightEchoService
            {
                [return: NotNull]
                ValueTask<string> EchoAsync(CancellationToken cancellationToken = default);
            }

            public interface IEchoService : ILeftEchoService, IRightEchoService
            {
            }

            [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
            [ServerExtension("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                [ServerExtensionMethod(typeof(IRemoteControl), "Echo")]
                public string Echo(HookContext ctx) => "";
            }

            public static class Probe
            {
                public static ValueTask<string> ViaProperty(RemoteControl control, CancellationToken cancellationToken)
                    => control.EchoClient.EchoAsync(cancellationToken);

                public static ValueTask<string> ViaMethod(RemoteControl control, CancellationToken cancellationToken)
                    => control.Echo(cancellationToken);
            }
            """);
        var diagnostics = result.Diagnostics;
        var generatedSource = string.Join(Environment.NewLine, result.GeneratedTrees.Select(tree => tree.ToString()));
        var diagnosticText = string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString()));

        Assert.True(
            diagnostics.Any(d => d.Id == "DBXK100" &&
                                 d.GetMessage().Contains("inherited", StringComparison.OrdinalIgnoreCase) &&
                                 d.GetMessage().Contains("return", StringComparison.OrdinalIgnoreCase) &&
                                 d.GetMessage().Contains("flow", StringComparison.OrdinalIgnoreCase)),
            "Expected a focused DBXK100 diagnostic for conflicting inherited return-flow attributes. " +
            "Actual diagnostics:" +
            Environment.NewLine +
            diagnosticText);
        Assert.DoesNotContain("EchoKernelServerExtensionClient", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EchoKernelServerExtensionClientExtensions", generatedSource, StringComparison.Ordinal);
    }
}
