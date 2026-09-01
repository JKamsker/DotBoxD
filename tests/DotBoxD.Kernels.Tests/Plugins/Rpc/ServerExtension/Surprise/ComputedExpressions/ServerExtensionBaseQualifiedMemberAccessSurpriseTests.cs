using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public interface IBaseQualifiedMemberAccessProfileService
{
    int GetMirror(int health);
}

public sealed class ServerExtensionBaseQualifiedMemberAccessSurpriseTests
{
    private const string ProfileSource = """
        namespace Sample;

        public class BaseProfile
        {
            public int Health { get; set; }
        }

        public sealed class Profile : BaseProfile
        {
            public int Mirror => base.Health;
        }
        """;

    private const string KernelSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;

        namespace Sample;

        [ServerExtension("base-qualified-member-access-profile")]
        public sealed partial class BaseQualifiedMemberAccessProfileKernel
        {
            public int GetMirror(int health, HookContext ctx)
                => new Profile { Health = health }.Mirror;
        }
        """;

    [Fact]
    public async Task Server_extension_reconstructs_a_dto_with_a_base_qualified_computed_member()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            [ProfileSource, KernelSource],
            "Sample.BaseQualifiedMemberAccessProfilePluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IBaseQualifiedMemberAccessProfileService>(kernel);

        Assert.Equal(7, service.GetMirror(7));
    }
}
