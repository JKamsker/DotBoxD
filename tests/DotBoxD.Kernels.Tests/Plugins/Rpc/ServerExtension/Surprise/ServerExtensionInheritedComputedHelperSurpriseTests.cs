using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public interface IInheritedComputedProfileService
{
    int GetScore(int health, int rank);
}

public sealed class ServerExtensionInheritedComputedHelperSurpriseTests
{
    private const string ProfileSource = """
        namespace Sample;

        public class BaseProfile
        {
            public int Health { get; set; }

            public int Rank { get; set; }

            protected int ComputeScore() => Health + Rank;
        }

        public sealed class Profile : BaseProfile
        {
            public int Score => ComputeScore();
        }
        """;

    private const string KernelSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        [ServerExtension("inherited-computed-profile")]
        public sealed partial class InheritedComputedProfileKernel
        {
            public int GetScore(int health, int rank, HookContext ctx)
                => new Profile { Health = health, Rank = rank }.Score;
        }
        """;

    [Fact]
    public async Task Derived_dto_reconstructs_a_computed_property_using_an_inherited_protected_helper()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            [ProfileSource, KernelSource],
            "Sample.InheritedComputedProfilePluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IInheritedComputedProfileService>(kernel);

        Assert.Equal(7, service.GetScore(3, 4));
    }
}
