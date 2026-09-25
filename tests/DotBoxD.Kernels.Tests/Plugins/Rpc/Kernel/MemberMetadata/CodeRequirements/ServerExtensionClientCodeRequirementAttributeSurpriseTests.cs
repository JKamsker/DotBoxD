using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientCodeRequirementAttributeSurpriseTests
{
    private const string RequiresUnreferencedCode =
        "[global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute(\"Trimming contract\", Url = \"https://example.invalid/trimming\")]";

    private const string RequiresDynamicCode =
        "[global::System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute(\"Dynamic-code contract\", Url = \"https://example.invalid/dynamic-code\")]";

    [Fact]
    public void Service_backed_client_and_receiver_extension_preserve_code_requirements()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(Source);

        AssertGeneratedTypeContains(
            generatedSources,
            "EchoKernelServerExtensionClient",
            "RestrictedServiceEchoAsync",
            RequiresUnreferencedCode);
        AssertGeneratedTypeContains(
            generatedSources,
            "EchoKernelServerExtensionClient",
            "RestrictedServiceEchoAsync",
            RequiresDynamicCode);
        AssertGeneratedTypeContains(
            generatedSources,
            "EchoKernelServerExtensionClientExtensions",
            "RestrictedServiceEcho",
            RequiresUnreferencedCode);
        AssertGeneratedTypeContains(
            generatedSources,
            "EchoKernelServerExtensionClientExtensions",
            "RestrictedServiceEcho",
            RequiresDynamicCode);
    }

    [Fact]
    public void Direct_receiver_extension_preserves_code_requirements()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(DirectExtensionSource);

        AssertGeneratedTypeContains(
            generatedSources,
            "EchoKernelDirectServerExtensionClientExtensions",
            "RestrictedDirectEcho",
            RequiresUnreferencedCode);
        AssertGeneratedTypeContains(
            generatedSources,
            "EchoKernelDirectServerExtensionClientExtensions",
            "RestrictedDirectEcho",
            RequiresDynamicCode);
    }

    [Fact]
    public void Unmarked_server_extension_methods_remain_unmarked()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(PortableServiceSource);

        AssertGeneratedMethodDoesNotContain(
            generatedSources,
            "PortableServiceEchoAsync",
            "RequiresUnreferencedCodeAttribute");
        AssertGeneratedMethodDoesNotContain(
            generatedSources,
            "PortableServiceEchoAsync",
            "RequiresDynamicCodeAttribute");
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
        string methodName,
        string unexpectedAttribute)
    {
        var source = Assert.Single(
            generatedSources,
            generatedSource => generatedSource.Contains(methodName, StringComparison.Ordinal));
        var methodIndex = source.IndexOf(methodName, StringComparison.Ordinal);
        var precedingMethodEnd = source.LastIndexOf('}', methodIndex);
        var methodDeclaration = source[(precedingMethodEnd + 1)..methodIndex];

        Assert.DoesNotContain(unexpectedAttribute, methodDeclaration, StringComparison.Ordinal);
    }

    private const string Source = """
        #nullable enable
        using System.Diagnostics.CodeAnalysis;
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
            [RequiresUnreferencedCode("Trimming contract", Url = "https://example.invalid/trimming")]
            [RequiresDynamicCode("Dynamic-code contract", Url = "https://example.invalid/dynamic-code")]
            ValueTask<int> RestrictedServiceEchoAsync(int value, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "RestrictedServiceEcho")]
            public int RestrictedServiceEcho(int value, HookContext context) => value;
        }
        """;

    private static string PortableServiceSource => Source
        .Replace(
            "[RequiresUnreferencedCode(\"Trimming contract\", Url = \"https://example.invalid/trimming\")]",
            string.Empty,
            StringComparison.Ordinal)
        .Replace(
            "[RequiresDynamicCode(\"Dynamic-code contract\", Url = \"https://example.invalid/dynamic-code\")]",
            string.Empty,
            StringComparison.Ordinal)
        .Replace("RestrictedServiceEcho", "PortableServiceEcho", StringComparison.Ordinal);

    private const string DirectExtensionSource = """
        #nullable enable
        using System.Diagnostics.CodeAnalysis;
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
            [RequiresUnreferencedCode("Trimming contract", Url = "https://example.invalid/trimming")]
            [RequiresDynamicCode("Dynamic-code contract", Url = "https://example.invalid/dynamic-code")]
            [ServerExtensionMethod(typeof(IRemoteControl), "RestrictedDirectEcho")]
            public int RestrictedDirectEcho(int value, HookContext context) => value;
        }
        """;
}
