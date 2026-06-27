using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionLoweringSurpriseTests
{
    [Fact]
    public void Server_extension_lowers_target_typed_dto_creation()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record KillResult(int MonsterId, bool Success);

            [ServerExtension("target-new")]
            public sealed partial class TargetNewKernel
            {
                public KillResult Build(int monsterId, HookContext ctx) => new(monsterId, true);
            }
            """, "Sample.TargetNewPluginPackage");

        Assert.Equal(
            SandboxType.Record([SandboxType.I32, SandboxType.Bool]),
            Assert.Single(package.Module.Functions).ReturnType);
    }

    [Fact]
    public void Server_extension_lowers_explicit_numeric_widening_cast()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("numeric-cast")]
            public sealed partial class NumericCastKernel
            {
                public long Widen(int value, HookContext ctx) => (long)value;
            }
            """, "Sample.NumericCastPluginPackage");

        var returned = Assert.IsType<ReturnStatement>(Assert.Single(Assert.Single(package.Module.Functions).Body));
        var cast = Assert.IsType<CallExpression>(returned.Value);
        Assert.Equal("numeric.toI64", cast.Name);
    }
}
