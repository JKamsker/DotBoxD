using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc.Kernel.MemberMetadata;

public sealed class ServerExtensionClientTypePlatformCompatibilityAttributeSurpriseTests
{
    private const string SupportedWindowsAttribute =
        "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]";

    [Fact]
    public void Service_backed_generated_client_preserves_platform_compatibility_service_attribute()
    {
        var result = RpcMemberMetadataGeneratorHarness.RunGenerator(ServiceBackedSource);

        Assert.DoesNotContain(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        RpcMemberMetadataGeneratorHarness.AssertGeneratedSourceContains(
            result.GeneratedSources,
            "EchoKernelServerExtensionClient",
            SupportedWindowsAttribute +
            "\npublic sealed class EchoKernelServerExtensionClient : global::Sample.IEchoService");
        RpcMemberMetadataGeneratorHarness.AssertGeneratedSourceContains(
            result.GeneratedSources,
            "PortableKernelServerExtensionClient",
            "public sealed class PortableKernelServerExtensionClient : global::Sample.IPortableService");
        var portableClientSource = Assert.Single(
            result.GeneratedSources,
            source => source.Contains("class PortableKernelServerExtensionClient", StringComparison.Ordinal));
        Assert.DoesNotContain(SupportedWindowsAttribute, portableClientSource, StringComparison.Ordinal);
    }

    private const string ServiceBackedSource = """
        #nullable enable
        using System.Runtime.Versioning;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IRemoteControl;

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        [SupportedOSPlatform("windows")]
        public interface IEchoService
        {
            ValueTask<int> EchoAsync(int value, CancellationToken cancellationToken = default);
        }

        public interface IPortableService
        {
            ValueTask<int> EchoAsync(int value, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "PortableEchoValue")]
            public int Echo(int value, HookContext ctx) => value;
        }

        [ServerExtensionClient(typeof(IRemoteControl), "PortableClient")]
        [ServerExtension("portable", typeof(IPortableService))]
        public sealed partial class PortableKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "EchoValue")]
            public int Echo(int value, HookContext ctx) => value;
        }
        """;
}
