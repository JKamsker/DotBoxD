using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc.Kernel.MemberMetadata;

public sealed class ServerExtensionClientForeignExperimentalAttributeIdentitySurpriseTests
{
    [Fact]
    public void Service_backed_generated_client_ignores_foreign_experimental_attribute()
    {
        var foreignExperimentalAttribute = CompileForeignExperimentalAttributeReference();

        var result = RpcMemberMetadataGeneratorHarness.RunGenerator(
            ServiceBackedSource,
            foreignExperimentalAttribute);

        Assert.DoesNotContain(
            result.GeneratedSources,
            source => source.Contains(
                "global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"FOREIGN_RPC_TYPE\")",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.GeneratedSources,
            source => source.Contains("#pragma warning disable FOREIGN_RPC_TYPE", StringComparison.Ordinal));
    }

    private static MetadataReference CompileForeignExperimentalAttributeReference()
    {
        var compilation = CSharpCompilation.Create(
            "ForeignExperimentalAttribute",
            [CSharpSyntaxTree.ParseText(
                """
                namespace System.Diagnostics.CodeAnalysis;

                public sealed class ExperimentalAttribute : System.Attribute
                {
                    public ExperimentalAttribute(string diagnosticId) { }
                }
                """,
                RpcMemberMetadataGeneratorHarness.ParseOptions)],
            RpcMemberMetadataGeneratorHarness.References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assembly = new MemoryStream();
        var emit = compilation.Emit(assembly);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));

        return MetadataReference.CreateFromImage(assembly.ToArray())
            .WithAliases(ImmutableArray.Create("Foreign"));
    }

    private const string ServiceBackedSource = """
        #nullable enable
        extern alias Foreign;

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
            public RemoteControl(IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public IServerExtensionClientRegistry ServerExtensions { get; }
        }

        [Foreign::System.Diagnostics.CodeAnalysis.Experimental("FOREIGN_RPC_TYPE")]
        public interface IEchoService
        {
            ValueTask<int> EchoAsync(int value, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "EchoValue")]
            public int Echo(int value, HookContext ctx) => value;
        }
        """;
}
