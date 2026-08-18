using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc.Kernel.ReturnFlow;

public sealed class ServerExtensionClientReturnFlowAttributeIdentitySurpriseTests
{
    [Fact]
    public void Generated_clients_ignore_aliased_lookalike_return_flow_attributes()
    {
        var foreignMaybeNull = CompileForeignMaybeNullAttribute();
        var foreignGeneratedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSourcesWithReferences(
            ServiceBackedSource,
            foreignMaybeNull.WithProperties(
                MetadataReferenceProperties.Assembly.WithAliases(["ForeignFlowAttributes"])));
        var bclGeneratedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSourcesWithReferences(
            ServiceBackedSource.Replace(
                "[return: ForeignFlowAttributes::System.Diagnostics.CodeAnalysis.MaybeNull]",
                "[return: MaybeNull]",
                StringComparison.Ordinal),
            foreignMaybeNull.WithProperties(
                MetadataReferenceProperties.Assembly.WithAliases(["ForeignFlowAttributes"])));
        var foreignGeneratedSource = string.Join(Environment.NewLine, foreignGeneratedSources);
        var bclGeneratedSource = string.Join(Environment.NewLine, bclGeneratedSources);

        Assert.DoesNotContain(
            "[return: global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute]",
            foreignGeneratedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "[return: global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute]",
            bclGeneratedSource,
            StringComparison.Ordinal);
    }

    private static MetadataReference CompileForeignMaybeNullAttribute()
    {
        var compilation = CSharpCompilation.Create(
            "ForeignFlowAttributes",
            [CSharpSyntaxTree.ParseText(ForeignMaybeNullAttributeSource)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assembly = new MemoryStream();
        var emit = compilation.Emit(assembly);

        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        return MetadataReference.CreateFromImage(assembly.ToArray());
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }

    private const string ForeignMaybeNullAttributeSource = """
        namespace System.Diagnostics.CodeAnalysis;

        [System.AttributeUsage(System.AttributeTargets.ReturnValue)]
        public sealed class MaybeNullAttribute : System.Attribute;
        """;

    private const string ServiceBackedSource = """
        extern alias ForeignFlowAttributes;

        #nullable enable

        using System.Diagnostics.CodeAnalysis;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
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
            [return: ForeignFlowAttributes::System.Diagnostics.CodeAnalysis.MaybeNull]
            ValueTask<string> ForeignAsync(CancellationToken cancellationToken = default);

        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "Foreign")]
            public string Foreign(HookContext ctx) => "";
        }

        public static class Probe
        {
            public static ValueTask<string> ViaProperty(RemoteControl control, CancellationToken cancellationToken)
                => control.EchoClient.ForeignAsync(cancellationToken);

        }
        """;
}
