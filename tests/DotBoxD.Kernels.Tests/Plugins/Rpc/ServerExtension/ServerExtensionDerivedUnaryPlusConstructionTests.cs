using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public interface IEchoPlusService
{
    int MakeAndReadPlus(int a);
}

/// <summary>
/// Guards finding #22 (unary-plus half): a derived get-only member whose getter is a unary <c>+</c> over a
/// constructor field is accepted by the reconstruction gate (<c>IsSupportedUnary</c> includes
/// <c>UnaryPlusExpression</c>), so the kernel JSON lowerer must also be able to build its wire slot. Before the
/// fix the lowerer handled only <c>!</c>/unary <c>-</c> and threw on <c>+A</c> (a DBXK100 generator error);
/// <c>+A</c> is identity and must lower to the value of <c>A</c>.
/// </summary>
public sealed class ServerExtensionDerivedUnaryPlusConstructionTests
{
    private const string Source = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public readonly record struct Echoed(int A)
        {
            public int Plus => +A;
        }

        [ServerExtension("echo-plus")]
        public sealed partial class EchoPlusKernel
        {
            public int MakeAndReadPlus(int a, HookContext ctx)
            {
                var value = new Echoed(a);
                return value.Plus;
            }
        }
        """;

    [Fact]
    public async Task A_kernel_constructs_and_reads_a_unary_plus_derived_member()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(Source, "Sample.EchoPlusPluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IEchoPlusService>(kernel);

        // The derived slot must hold the lowered getter (+A == A), read back from inside the sandbox.
        Assert.Equal(5, service.MakeAndReadPlus(5));
        Assert.Equal(9, service.MakeAndReadPlus(9));
    }
}
