using System.Collections.Immutable;
using DotBoxD.Kernels.Tests.Plugins.Rpc.Kernel.MemberMetadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientForeignMethodMetadataAttributeIdentitySurpriseTests
{
    [Fact]
    public void Generated_server_extension_methods_ignore_foreign_experimental_attributes()
    {
        var foreignAttributeReference = CompileForeignExperimentalAttributeReference();
        var result = RpcMemberMetadataGeneratorHarness.RunGenerator(
            ForeignAttributeSource,
            foreignAttributeReference);
        var directResult = RpcMemberMetadataGeneratorHarness.RunGenerator(
            ForeignDirectAttributeSource,
            foreignAttributeReference);

        Assert.DoesNotContain(result.GeneratorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertGeneratedMethodDoesNotContain(
            result.GeneratedSources,
            "EchoKernelServerExtensionClient",
            "FOREIGN_RPC_METHOD");
        AssertGeneratedMethodDoesNotContain(
            result.GeneratedSources,
            "EchoKernelServerExtensionClientExtensions",
            "FOREIGN_RPC_METHOD");
        Assert.DoesNotContain(directResult.GeneratorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertGeneratedMethodDoesNotContain(
            directResult.GeneratedSources,
            "EchoKernelDirectServerExtensionClientExtensions",
            "FOREIGN_RPC_METHOD");
    }

    private static void AssertGeneratedMethodDoesNotContain(
        IReadOnlyList<string> generatedSources,
        string generatedTypeName,
        string unexpectedSource)
    {
        var source = Assert.Single(
            generatedSources,
            candidate => candidate.Contains(generatedTypeName, StringComparison.Ordinal));

        Assert.DoesNotContain(
            "global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(unexpectedSource, source, StringComparison.Ordinal);
    }

    private static MetadataReference CompileForeignExperimentalAttributeReference()
    {
        var compilation = CSharpCompilation.Create(
            "ForeignExperimentalAttribute",
            [CSharpSyntaxTree.ParseText(ForeignExperimentalAttributeSource)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray())
            .WithAliases(ImmutableArray.Create("ForeignExperimental"));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));

    private const string ForeignExperimentalAttributeSource = """
        namespace System.Diagnostics.CodeAnalysis;

        [System.AttributeUsage(System.AttributeTargets.Method)]
        public sealed class ExperimentalAttribute(string diagnosticId) : System.Attribute
        {
            public string DiagnosticId { get; } = diagnosticId;
        }
        """;

    private const string ForeignDirectAttributeSource = """
        extern alias ForeignExperimental;

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

        [ServerExtension(typeof(IRemoteControl), "echo")]
        public sealed partial class EchoKernel
        {
            [ForeignExperimental::System.Diagnostics.CodeAnalysis.Experimental("FOREIGN_RPC_METHOD")]
            [ServerExtensionMethod(typeof(IRemoteControl), "EchoValue")]
            public int Echo(int value, HookContext ctx) => value;
        }
        """;

    private const string ForeignAttributeSource = """
        extern alias ForeignExperimental;

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
            [ForeignExperimental::System.Diagnostics.CodeAnalysis.Experimental("FOREIGN_RPC_METHOD")]
            ValueTask<int> EchoAsync(int value, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ForeignExperimental::System.Diagnostics.CodeAnalysis.Experimental("FOREIGN_RPC_METHOD")]
            [ServerExtensionMethod(typeof(IRemoteControl), "EchoValue")]
            public int Echo(int value, HookContext ctx) => value;
        }
        """;
}
