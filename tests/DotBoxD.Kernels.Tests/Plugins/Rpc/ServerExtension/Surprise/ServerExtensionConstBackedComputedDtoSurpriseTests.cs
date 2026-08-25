using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public interface IConstBackedComputedDtoService
{
    string ReadLabel(string health);
}

public sealed class ServerExtensionConstBackedComputedDtoSurpriseTests
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
            private const string DefaultLabel = "Health";

            public string Health { get; init; } = string.Empty;
            public string Label => DefaultLabel;
        }

        [ServerExtension("const-backed-computed-dto")]
        public sealed partial class ConstBackedComputedDtoKernel
        {
            public string ReadLabel(string health, HookContext ctx) => new Profile { Health = health }.Label;
        }
        """;

    [Fact]
    public async Task Server_extension_reconstructs_a_dto_with_a_const_backed_computed_member()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.ConstBackedComputedDtoPluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IConstBackedComputedDtoService>(kernel);

        Assert.Equal("Health", service.ReadLabel("Critical"));
    }
}
