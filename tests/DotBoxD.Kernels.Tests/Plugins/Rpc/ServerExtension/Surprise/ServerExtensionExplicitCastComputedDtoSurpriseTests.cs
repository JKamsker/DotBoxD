using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public interface IExplicitCastComputedDtoService
{
    long ReadDisplayScore(int health);
}

public sealed class ServerExtensionExplicitCastComputedDtoSurpriseTests
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
            public long DisplayScore => (long)Health;
        }

        [ServerExtension("explicit-cast-computed-dto")]
        public sealed partial class ExplicitCastComputedDtoKernel
        {
            public long ReadDisplayScore(int health, HookContext ctx)
            {
                var profile = new Profile { Health = health };
                return profile.DisplayScore;
            }
        }
        """;

    [Fact]
    public async Task Server_extension_reconstructs_a_dto_with_an_explicit_cast_computed_member()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.ExplicitCastComputedDtoPluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IExplicitCastComputedDtoService>(kernel);

        Assert.Equal(123_456_789L, service.ReadDisplayScore(123_456_789));
    }
}
