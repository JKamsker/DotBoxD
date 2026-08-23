using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

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

public interface IInheritedComputedProfileService
{
    Profile GetProfile(int health, int rank);
}

public sealed class ServerExtensionInheritedComputedHelperSurpriseTests
{
    private const string Source = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

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

        [ServerExtension("inherited-computed-profile")]
        public sealed partial class InheritedComputedProfileKernel
        {
            public Profile GetProfile(int health, int rank, HookContext ctx)
                => new() { Health = health, Rank = rank };
        }
        """;

    [Fact]
    public async Task Derived_dto_reconstructs_a_computed_property_using_an_inherited_protected_helper()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.InheritedComputedProfilePluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IInheritedComputedProfileService>(kernel);

        var profile = service.GetProfile(3, 4);

        Assert.Equal(3, profile.Health);
        Assert.Equal(4, profile.Rank);
        Assert.Equal(7, profile.Score);
    }
}
