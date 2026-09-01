using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public interface INameofComputedProfileService
{
    string ReadHealthFieldName(int health);
}

public sealed class ServerExtensionNameofComputedDtoSurpriseTests
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

            public string HealthFieldName => nameof(Health);
        }

        [ServerExtension("nameof-computed-profile")]
        public sealed partial class NameofComputedProfileKernel
        {
            public string ReadHealthFieldName(int health, HookContext ctx)
                => new Profile { Health = health }.HealthFieldName;
        }
        """;

    [Fact]
    public async Task Server_extension_reconstructs_a_dto_with_a_nameof_computed_member()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.NameofComputedProfilePluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<INameofComputedProfileService>(kernel);

        Assert.Equal("Health", service.ReadHealthFieldName(3));
    }
}
