using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public interface IConditionalComputedProfileService
{
    int ReadDisplayScore(int health, int rank);
}

public sealed class ServerExtensionConditionalComputedDtoSurpriseTests
{
    private const string Source = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class Profile
        {
            public int Health { get; init; }
            public int Rank { get; init; }
            public int DisplayScore => Health > 0 ? Health : Rank;
        }

        [ServerExtension("conditional-computed-profile")]
        public sealed partial class ConditionalComputedProfileKernel
        {
            public int ReadDisplayScore(int health, int rank, HookContext ctx)
                => new Profile { Health = health, Rank = rank }.DisplayScore;
        }
        """;

    [Fact]
    public async Task Server_extension_reconstructs_a_dto_with_a_conditional_computed_member()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.ConditionalComputedProfilePluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IConditionalComputedProfileService>(kernel);

        Assert.Equal(7, service.ReadDisplayScore(7, 3));
        Assert.Equal(3, service.ReadDisplayScore(0, 3));
    }
}
