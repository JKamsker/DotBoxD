using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcClientProxyValidationTests
{
    [Fact]
    public void Kernel_rpc_service_rejects_ref_parameters_without_service_interface()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [KernelRpcService("echo")]
            public sealed partial class EchoKernel
            {
                public int Echo(ref int value, HookContext ctx)
                {
                    return value;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("cannot use ref, in, or out", StringComparison.Ordinal));
    }

    [Fact]
    public void Generated_client_rejects_ref_parameters_even_when_contract_matches()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IEchoService
            {
                ValueTask<int> EchoAsync(ref int value);
            }

            [KernelRpcService("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(ref int value, HookContext ctx) => value;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("cannot use ref, in, or out", StringComparison.Ordinal));
    }
}
