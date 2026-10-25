using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc.Kernel.MemberMetadata;

public sealed class ServerExtensionClientTypePreviewFeatureAttributeSurpriseTests
{
    [Fact]
    public void Service_backed_generated_client_preserves_preview_feature_service_attribute()
    {
        var result = RpcMemberMetadataGeneratorHarness.RunGenerator(ServiceBackedSource);

        Assert.DoesNotContain(result.GeneratorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var previewService = result.OutputCompilation.GetTypeByMetadataName("Sample.IPreviewService")!;
        var previewClient = result.OutputCompilation.GetSymbolsWithName("PreviewKernelServerExtensionClient")
            .OfType<INamedTypeSymbol>()
            .Single();
        var portableClient = result.OutputCompilation.GetSymbolsWithName("PortableKernelServerExtensionClient")
            .OfType<INamedTypeSymbol>()
            .Single();

        var originalAttribute = Assert.Single(previewService.GetAttributes(), IsPreviewFeatureAttribute);
        var generatedAttribute = Assert.Single(previewClient.GetAttributes(), IsPreviewFeatureAttribute);

        Assert.Equal(originalAttribute.ToString(), generatedAttribute.ToString());
        Assert.DoesNotContain(portableClient.GetAttributes(), IsPreviewFeatureAttribute);
    }

    private static bool IsPreviewFeatureAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() == "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute";

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

        [RequiresPreviewFeatures("Preview service", Url = "https://example.invalid/preview")]
        public interface IPreviewService
        {
            ValueTask<int> EchoAsync(int value, CancellationToken cancellationToken = default);
        }

        public interface IPortableService
        {
            ValueTask<int> EchoAsync(int value, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "PreviewClient")]
        [ServerExtension("preview", typeof(IPreviewService))]
        public sealed partial class PreviewKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "PreviewEcho")]
            public int Echo(int value, HookContext context) => value;
        }

        [ServerExtensionClient(typeof(IRemoteControl), "PortableClient")]
        [ServerExtension("portable", typeof(IPortableService))]
        public sealed partial class PortableKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "PortableEcho")]
            public int Echo(int value, HookContext context) => value;
        }
        """;
}
