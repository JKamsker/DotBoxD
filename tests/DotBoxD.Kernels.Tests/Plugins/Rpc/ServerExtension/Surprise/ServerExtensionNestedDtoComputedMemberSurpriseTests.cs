using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed record NestedStats(int Health);

public sealed class NestedProfile
{
    public NestedStats Stats { get; init; } = new(0);

    public int Health => Stats.Health;
}

public interface INestedProfileService
{
    NestedProfile Create(int health);
}

public sealed class ServerExtensionNestedDtoComputedMemberSurpriseTests
{
    private const string Source = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record NestedStats(int Health);

        public sealed class NestedProfile
        {
            public NestedStats Stats { get; init; } = new(0);

            public int Health => Stats.Health;
        }

        [ServerExtension("nested-profile")]
        public sealed partial class NestedProfileKernel
        {
            public NestedProfile Create(int health, HookContext ctx)
                => new() { Stats = new NestedStats(health) };
        }
        """;

    [Fact]
    public async Task Server_extension_reconstructs_a_dto_with_a_nested_dto_computed_member()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.NestedProfilePluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<INestedProfileService>(kernel);

        var profile = service.Create(17);

        Assert.Equal(17, profile.Stats.Health);
        Assert.Equal(profile.Stats.Health, profile.Health);
    }
}
