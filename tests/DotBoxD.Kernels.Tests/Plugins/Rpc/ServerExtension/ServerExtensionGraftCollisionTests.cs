using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionGraftCollisionTests
{
    [Fact]
    public void Duplicate_direct_graft_methods_in_same_namespace_report_DBXK115()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class RemoteMonsterControl : IServerExtensionClientAccessor
            {
                public IServerExtensionClientRegistry ServerExtensions { get; } = null!;
            }

            [ServerExtension(typeof(RemoteMonsterControl), "first")]
            public sealed partial class FirstKernel
            {
                [ServerExtensionMethod(typeof(RemoteMonsterControl))]
                public int Kill(int monsterId, HookContext ctx)
                {
                    return monsterId;
                }
            }

            [ServerExtension(typeof(RemoteMonsterControl), "second")]
            public sealed partial class SecondKernel
            {
                [ServerExtensionMethod(typeof(RemoteMonsterControl))]
                public int Kill(int id, HookContext ctx)
                {
                    return id;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK115" &&
                 d.GetMessage().Contains("Kill(int)", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("FirstKernel", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_direct_graft_methods_in_different_namespaces_are_allowed()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Abstractions;

            namespace Domain
            {
                public sealed class RemoteMonsterControl : IServerExtensionClientAccessor
                {
                    public IServerExtensionClientRegistry ServerExtensions { get; } = null!;
                }
            }

            namespace Sample.One
            {
                [ServerExtension(typeof(Domain.RemoteMonsterControl), "first")]
                public sealed partial class FirstKernel
                {
                    [ServerExtensionMethod(typeof(Domain.RemoteMonsterControl))]
                    public int Kill(int monsterId, HookContext ctx)
                    {
                        return monsterId;
                    }
                }
            }

            namespace Sample.Two
            {
                [ServerExtension(typeof(Domain.RemoteMonsterControl), "second")]
                public sealed partial class SecondKernel
                {
                    [ServerExtensionMethod(typeof(Domain.RemoteMonsterControl))]
                    public int Kill(int id, HookContext ctx)
                    {
                        return id;
                    }
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DBXK115");
    }
}
