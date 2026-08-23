using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public interface ISwitchExpressionComputedDtoService
{
    string ReadStatus(int health);
}

public sealed class ServerExtensionSwitchExpressionComputedDtoSurpriseTests
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
            public string Status => Health switch { > 0 => "alive", _ => "dead" };
        }

        [ServerExtension("switch-expression-computed-dto")]
        public sealed partial class SwitchExpressionComputedDtoKernel
        {
            public string ReadStatus(int health, HookContext ctx)
            {
                var profile = new Profile { Health = health };
                return profile.Status;
            }
        }
        """;

    [Fact]
    public async Task Server_extension_reconstructs_a_dto_with_a_switch_expression_computed_member()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.SwitchExpressionComputedDtoPluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<ISwitchExpressionComputedDtoService>(kernel);

        Assert.Equal("alive", service.ReadStatus(1));
        Assert.Equal("dead", service.ReadStatus(0));
    }
}
