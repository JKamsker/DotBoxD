using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelLiveSettingGenerationTests
{
    [Fact]
    public void Generated_rpc_package_includes_live_settings()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            """
            using System.ComponentModel.DataAnnotations;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [KernelRpcService("threshold")]
            public sealed partial class ThresholdKernel
            {
                [LiveSetting]
                [Range(1, 10)]
                public int Threshold { get; set; } = 5;

                public bool Check(int value, HookContext ctx)
                {
                    return value >= 0;
                }
            }
            """,
            "Sample.ThresholdPluginPackage");

        var setting = Assert.Single(package.Manifest.LiveSettings);
        Assert.Equal("Threshold", setting.Name);
        Assert.Equal("int", setting.Type);
        Assert.Equal(5, setting.DefaultValue);
        Assert.Equal(1, setting.Min);
        Assert.Equal(10, setting.Max);
    }
}
