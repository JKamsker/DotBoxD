using System.Collections.Immutable;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientAttributeIdentitySurpriseTests
{
    [Fact]
    public void Generated_clients_ignore_aliased_lookalike_receiver_attributes()
    {
        var foreignAttributes = PluginServerGenerationTestDriver.CompileReference(
                """
                namespace DotBoxD.Abstractions;

                public sealed class ServerExtensionClientAttribute(System.Type receiverType, string? name = null) : System.Attribute;
                """,
                "ForeignServerExtensionAttributes")
            .WithAliases(ImmutableArray.Create("ForeignServerExtensionAttributes"));
        var result = PluginAnalyzerGeneratedPackageFactory.RunGeneratorWithReferences(
            """
            extern alias ForeignServerExtensionAttributes;

            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            [RpcService]
            public interface IRemoteControl;

            public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
            {
                public IServerExtensionClientRegistry ServerExtensions { get; } = null!;
            }

            public interface IHijackedService
            {
                int GetValue();
            }

            public interface IControlService
            {
                int GetValue();
            }

            [ForeignServerExtensionAttributes::DotBoxD.Abstractions.ServerExtensionClient(typeof(IRemoteControl), "HijackedClient")]
            [ServerExtension("hijacked", typeof(IHijackedService))]
            public sealed partial class HijackedKernel
            {
                public int GetValue(HookContext context) => 0;
            }

            [ServerExtensionClient(typeof(IRemoteControl), "ControlClient")]
            [ServerExtension("control", typeof(IControlService))]
            public sealed partial class ControlKernel
            {
                public int GetValue(HookContext context) => 0;
            }
            """,
            foreignAttributes);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain("HijackedClient", generated, StringComparison.Ordinal);
        Assert.Contains("ControlClient", generated, StringComparison.Ordinal);
    }
}
