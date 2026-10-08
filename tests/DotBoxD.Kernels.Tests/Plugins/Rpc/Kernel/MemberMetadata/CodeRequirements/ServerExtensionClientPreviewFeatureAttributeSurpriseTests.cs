using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientPreviewFeatureAttributeSurpriseTests
{
    private const string PreviewFeature =
        "[global::System.Runtime.Versioning.RequiresPreviewFeaturesAttribute(\"Preview RPC\", Url = \"https://example.invalid/preview\")]";

    [Fact]
    public void Service_backed_client_and_receiver_extension_preserve_preview_feature_requirements()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(ServiceBackedSource);

        AssertGeneratedTypeContains(
            generatedSources,
            "EchoKernelServerExtensionClient",
            "PreviewServiceEchoAsync",
            PreviewFeature);
        AssertGeneratedTypeContains(
            generatedSources,
            "EchoKernelServerExtensionClientExtensions",
            "PreviewServiceEcho",
            PreviewFeature);
    }

    [Fact]
    public void Direct_receiver_extension_preserves_preview_feature_requirements()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(DirectExtensionSource);

        AssertGeneratedTypeContains(
            generatedSources,
            "EchoKernelDirectServerExtensionClientExtensions",
            "PreviewDirectEcho",
            PreviewFeature);
    }

    [Fact]
    public void Unmarked_server_extension_methods_remain_unmarked()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(UnmarkedServiceBackedSource);

        AssertGeneratedMethodDoesNotContain(
            generatedSources,
            "PortableServiceEchoAsync");
    }

    private static void AssertGeneratedTypeContains(
        IReadOnlyList<string> generatedSources,
        string generatedTypeName,
        string generatedMethodName,
        string expectedAttribute)
    {
        var generatedType = generatedSources
            .Select(static source => CSharpSyntaxTree.ParseText(source))
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            .Single(type => string.Equals(type.Identifier.ValueText, generatedTypeName, StringComparison.Ordinal));
        var generatedMethod = generatedType.Members
            .OfType<MethodDeclarationSyntax>()
            .Single(method => string.Equals(method.Identifier.ValueText, generatedMethodName, StringComparison.Ordinal));

        Assert.Contains(
            generatedMethod.AttributeLists,
            attribute => attribute.ToFullString().Contains(expectedAttribute, StringComparison.Ordinal));
    }

    private static void AssertGeneratedMethodDoesNotContain(
        IReadOnlyList<string> generatedSources,
        string methodName)
    {
        var source = Assert.Single(
            generatedSources,
            generatedSource => generatedSource.Contains(methodName, StringComparison.Ordinal));
        var generatedMethod = CSharpSyntaxTree.ParseText(source)
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => string.Equals(method.Identifier.ValueText, methodName, StringComparison.Ordinal));

        Assert.DoesNotContain(
            generatedMethod.AttributeLists.SelectMany(static attributeList => attributeList.Attributes),
            static attribute => attribute.Name.ToString() is "RequiresPreviewFeatures" or "RequiresPreviewFeaturesAttribute");
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

        public interface IEchoService
        {
            [RequiresPreviewFeatures("Preview RPC", Url = "https://example.invalid/preview")]
            ValueTask<int> PreviewServiceEchoAsync(int value, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "PreviewServiceEcho")]
            public int PreviewServiceEcho(int value, HookContext context) => value;
        }
        """;

    private static string UnmarkedServiceBackedSource => ServiceBackedSource
        .Replace(
            "[RequiresPreviewFeatures(\"Preview RPC\", Url = \"https://example.invalid/preview\")]",
            string.Empty,
            StringComparison.Ordinal)
        .Replace("PreviewServiceEcho", "PortableServiceEcho", StringComparison.Ordinal);

    private const string DirectExtensionSource = """
        #nullable enable
        using System.Runtime.Versioning;
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

        [ServerExtension(typeof(IRemoteControl), "direct-echo")]
        public sealed partial class EchoKernel
        {
            [RequiresPreviewFeatures("Preview RPC", Url = "https://example.invalid/preview")]
            [ServerExtensionMethod(typeof(IRemoteControl), "PreviewDirectEcho")]
            public int PreviewDirectEcho(int value, HookContext context) => value;
        }
        """;
}
