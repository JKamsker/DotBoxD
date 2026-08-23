using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public interface IComputedHelperOverloadService
{
    int ReadComputed(int value);
}

public sealed class ServerExtensionComputedHelperOverloadSurpriseTests
{
    private const string Source = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class ComputedHelperOverloadDto
        {
            public int Value { get; init; }
            public int Computed => Calculate();

            private int Calculate<T>() => Value + 1;
            private int Calculate() => Value + 2;
        }

        [ServerExtension("computed-helper-overload")]
        public sealed partial class ComputedHelperOverloadKernel
        {
            public int ReadComputed(int value, HookContext ctx)
            {
                var dto = new ComputedHelperOverloadDto { Value = value };
                return dto.Computed;
            }
        }
        """;

    [Fact]
    public async Task Computed_dto_helper_uses_the_CSharp_bound_non_generic_overload()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            Source,
            "Sample.ComputedHelperOverloadPluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IComputedHelperOverloadService>(kernel);

        Assert.Equal(5, service.ReadComputed(3));
    }
}
