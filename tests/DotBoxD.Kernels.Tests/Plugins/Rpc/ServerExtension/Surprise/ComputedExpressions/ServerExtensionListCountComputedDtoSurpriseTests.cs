using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ListCountProfile
{
    public List<int> Values { get; init; } = [];

    public int Count => Values.Count;

    public int ServerCount { get; init; }
}

public interface IListCountProfileService
{
    ListCountProfile Create(List<int> values);
}

public sealed class ServerExtensionListCountComputedDtoSurpriseTests
{
    private const string Source = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class ListCountProfile
        {
            public List<int> Values { get; init; } = [];

            public int Count => ((Values)).Count;

            public int ServerCount { get; init; }
        }

        [ServerExtension("list-count-profile")]
        public sealed partial class ListCountProfileKernel
        {
            public ListCountProfile Create(List<int> values, HookContext ctx)
                => new()
                {
                    Values = values,
                    ServerCount = new ListCountProfile { Values = values, ServerCount = 0 }.Count
                };
        }
        """;

    [Fact]
    public async Task Server_extension_reconstructs_a_dto_with_a_list_count_computed_member()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(Source, "Sample.ListCountProfilePluginPackage");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IListCountProfileService>(kernel);

        var profile = service.Create([3, 5, 8]);

        Assert.Equal(3, profile.ServerCount);
        Assert.Equal([3, 5, 8], profile.Values);
        Assert.Equal(profile.Values.Count, profile.Count);
    }
}
