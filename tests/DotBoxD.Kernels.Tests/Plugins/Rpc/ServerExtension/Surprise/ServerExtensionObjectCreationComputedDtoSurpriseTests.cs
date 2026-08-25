using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public interface IObjectCreationComputedProfileService
{
    int ReadSnapshotHealth(int health);
}

public sealed class ServerExtensionObjectCreationComputedDtoSurpriseTests
{
    private const string Source = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;

        namespace Sample;

        public sealed record Stats(int Health);

        public sealed class Profile
        {
            public int Health { get; init; }

            public Stats Snapshot => new Stats(Health);
        }

        [ServerExtension("object-creation-computed-profile")]
        public sealed partial class ObjectCreationComputedProfileKernel
        {
            public int ReadSnapshotHealth(int health, HookContext ctx)
            {
                var profile = new Profile { Health = health };
                return profile.Snapshot.Health;
            }
        }
        """;

    [Fact]
    public async Task Server_extension_reconstructs_a_dto_with_an_object_creation_computed_member()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.ObjectCreationComputedProfilePluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IObjectCreationComputedProfileService>(kernel);

        Assert.Equal(7, service.ReadSnapshotHealth(7));
    }
}
