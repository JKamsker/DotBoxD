using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientContractSurpriseTests
{
    [Fact]
    public void Generated_client_rejects_generic_service_methods_with_DBXK100()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IPingService
            {
                ValueTask PingAsync<T>();
            }

            [ServerExtension("ping", typeof(IPingService))]
            public sealed partial class PingKernel
            {
                public void Ping(HookContext ctx)
                {
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("generic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0305");
    }

}
