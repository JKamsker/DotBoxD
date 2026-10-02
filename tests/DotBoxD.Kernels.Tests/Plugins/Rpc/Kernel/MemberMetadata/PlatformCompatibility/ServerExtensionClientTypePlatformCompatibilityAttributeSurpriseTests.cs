using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc.Kernel.MemberMetadata;

public sealed class ServerExtensionClientTypePlatformCompatibilityAttributeSurpriseTests
{
    private const string SupportedWindowsAttribute =
        "[global::System.Runtime.Versioning.SupportedOSPlatformAttribute(\"windows\")]";

    [Fact]
    public void Service_backed_generated_client_preserves_platform_obsoletion_url()
    {
        const string annotation =
            "[ObsoletedOSPlatform(\"windows10.0\", \"use replacement\", Url = \"https://example.invalid/replacement\")]";
        var source = ServiceBackedSource.Replace("[SupportedOSPlatform(\"windows\")]", annotation, StringComparison.Ordinal);
        var result = RpcMemberMetadataGeneratorHarness.RunGenerator(source);

        Assert.DoesNotContain(result.GeneratorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.OutputCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var serviceType = result.OutputCompilation.GetTypeByMetadataName("Sample.IEchoService")!;
        var clientType = result.OutputCompilation.GetSymbolsWithName("EchoKernelServerExtensionClient")
            .OfType<INamedTypeSymbol>().Single();
        var originalAttribute = Assert.Single(serviceType.GetAttributes(), attribute =>
            attribute.AttributeClass?.Name == "ObsoletedOSPlatformAttribute");
        var generatedAttribute = Assert.Single(clientType.GetAttributes(), attribute =>
            attribute.AttributeClass?.Name == "ObsoletedOSPlatformAttribute");
        Assert.Equal(originalAttribute.ToString(), generatedAttribute.ToString());
    }

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
