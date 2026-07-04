using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientFileLocalAccessibilityTests
{
    [Fact]
    public void Generated_client_rejects_file_local_service_contract_without_generated_cs0234()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            file interface IEchoService
            {
                ValueTask<int> EchoAsync(int value);
            }

            [ServerExtension("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(int value, HookContext ctx)
                {
                    return value;
                }
            }
            """);

        Assert.True(
            diagnostics.Any(d =>
                d.Id == "DBXK100" &&
                d.GetMessage().Contains("IEchoService", StringComparison.Ordinal) &&
                d.GetMessage().Contains("file-local", StringComparison.Ordinal)),
            "Expected a focused DBXK100 diagnostic for the file-local IEchoService contract."
            + Environment.NewLine
            + string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString())));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0234");
    }
}
