using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionCancellationTokenDiagnosticsTests
{
    [Fact]
    public void Server_extension_accepts_cancellation_token_payload_parameters()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("cancellation-token-value")]
            public sealed partial class CancellationTokenValueKernel
            {
                public CancellationToken Echo(CancellationToken value, int marker, HookContext ctx) => value;
            }
            """);

        Assert.DoesNotContain(
            diagnostics,
            d => d.Id == "DBXK100" &&
                d.GetMessage().Contains("System.Threading.CancellationToken", StringComparison.Ordinal));
    }
}
