using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionComputedInheritedIgnoredMemberSurpriseTests
{
    private const string Source = """
        using System.Text.Json.Serialization;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;

        namespace Sample;

        public class BaseDto
        {
            [JsonIgnore]
            public int Value { get; init; }

            public int Computed => Value + 1;
        }

        public sealed class DerivedDto : BaseDto
        {
            public new int Value { get; init; }
        }

        [ServerExtension("shadowed-ignored-base-member")]
        public sealed partial class ShadowedIgnoredBaseMemberKernel
        {
            public int ReadComputed(HookContext ctx)
            {
                var value = new DerivedDto { Value = 10 };
                return value.Computed;
            }
        }
        """;

    [Fact]
    public async Task Computed_dto_getter_uses_its_ignored_base_member_not_a_shadowing_wire_member()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.ShadowedIgnoredBaseMemberPluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IShadowedIgnoredBaseMemberService>(kernel);

        Assert.Equal(1, service.ReadComputed());
    }

    public interface IShadowedIgnoredBaseMemberService
    {
        int ReadComputed();
    }
}
