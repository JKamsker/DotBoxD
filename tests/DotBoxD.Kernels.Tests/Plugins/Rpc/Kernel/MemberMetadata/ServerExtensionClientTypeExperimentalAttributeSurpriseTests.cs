using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc.Kernel.MemberMetadata;

public sealed class ServerExtensionClientTypeExperimentalAttributeSurpriseTests
{
    [Fact]
    public void Service_backed_generated_client_preserves_experimental_service_attribute()
    {
        var result = RpcMemberMetadataGeneratorHarness.RunGenerator(ServiceBackedSource);

        AssertNoGeneratorErrors(result.GeneratorDiagnostics);
        AssertNoGeneratedExperimentalDiagnostics(result.OutputCompilation, result.GeneratedTrees);
        RpcMemberMetadataGeneratorHarness.AssertGeneratedSourceContains(
            result.GeneratedSources,
            "EchoKernelServerExtensionClient",
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBXEXP_TYPE\", " +
            "UrlFormat = \"https://example.test/{0}\", Message = \"Use the stable API.\")]");
        AssertDirectGeneratedClientUseReportsExperimentalDiagnostic(result.GeneratedTrees);
    }

    [Fact]
    public void Service_backed_generated_client_preserves_experimental_service_attribute_with_unsuppressible_id()
    {
        var result = RpcMemberMetadataGeneratorHarness.RunGenerator(
            ServiceBackedSource.Replace("\"DBXEXP_TYPE\"", "\"DBX-EXP\"", StringComparison.Ordinal));

        AssertNoGeneratorErrors(result.GeneratorDiagnostics);
        RpcMemberMetadataGeneratorHarness.AssertGeneratedSourceContains(
            result.GeneratedSources,
            "EchoKernelServerExtensionClient",
            "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"DBX-EXP\", " +
            "UrlFormat = \"https://example.test/{0}\", Message = \"Use the stable API.\")]");
        Assert.DoesNotContain(
            result.GeneratedSources,
            source => source.Contains("#pragma warning disable DBX-EXP", StringComparison.Ordinal));
    }

    private static void AssertNoGeneratorErrors(IReadOnlyList<Diagnostic> diagnostics)
        => Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private static void AssertNoGeneratedExperimentalDiagnostics(
        Compilation compilation,
        IReadOnlySet<SyntaxTree> generatedTrees)
    {
        var generatedDiagnostics = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Id == "DBXEXP_TYPE" &&
                                 RpcMemberMetadataGeneratorHarness.IsGeneratedDiagnostic(
                                     diagnostic,
                                     generatedTrees))
            .ToArray();

        Assert.Empty(generatedDiagnostics);
    }

    private static void AssertDirectGeneratedClientUseReportsExperimentalDiagnostic(
        IReadOnlySet<SyntaxTree> generatedTrees)
    {
        var syntaxTrees = new[]
            {
                CSharpSyntaxTree.ParseText(ServiceBackedSource, RpcMemberMetadataGeneratorHarness.ParseOptions),
                CSharpSyntaxTree.ParseText(ConsumerSource, RpcMemberMetadataGeneratorHarness.ParseOptions),
            }
            .Concat(generatedTrees);
        var compilation = CSharpCompilation.Create(
            "DotBoxDGeneratedPackageExperimentalTypeConsumerTest",
            syntaxTrees,
            RpcMemberMetadataGeneratorHarness.References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var diagnostics = compilation.GetDiagnostics();

        AssertExperimentalDiagnosticOnLine(diagnostics, "EchoKernelServerExtensionClient client");
    }

    private static void AssertExperimentalDiagnosticOnLine(
        IReadOnlyList<Diagnostic> diagnostics,
        string expectedLine)
    {
        var matchingDiagnostic = diagnostics.Any(diagnostic =>
            diagnostic.Id == "DBXEXP_TYPE" &&
            diagnostic.Location.SourceTree is not null &&
            DiagnosticLine(diagnostic).Contains(expectedLine, StringComparison.Ordinal));

        Assert.True(
            matchingDiagnostic,
            "Expected DBXEXP_TYPE on generated-client use line '" +
            expectedLine +
            "'. Actual diagnostics:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));
    }

    private static string DiagnosticLine(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var text = diagnostic.Location.SourceTree!.GetText();
        return text.Lines[span.StartLinePosition.Line].ToString();
    }

    private const string ConsumerSource = """
        #nullable enable
        using System.Threading;
        using System.Threading.Tasks;

        namespace Sample;

        public static class GeneratedClientConsumer
        {
            public static ValueTask<int> ViaClient(
                EchoKernelServerExtensionClient client,
                CancellationToken cancellationToken)
                => client.EchoAsync(42, cancellationToken);
        }
        """;

    private const string ServiceBackedSource = """
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

        [Experimental("DBXEXP_TYPE", UrlFormat = "https://example.test/{0}", Message = "Use the stable API.")]
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
