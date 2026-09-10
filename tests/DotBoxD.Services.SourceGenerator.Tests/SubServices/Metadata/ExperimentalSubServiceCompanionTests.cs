using System.IO.Pipelines;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public class ExperimentalSubServiceCompanionTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Theory]
    [InlineData("[Experimental(\"DBXEXP_COMPANION\")]\n    ")]
    [InlineData("")]
    public void MetadataOnlySubServiceWithExperimentalManualCompanion_BecomesUnsupportedStub(
        string typeAttribute)
    {
        var constructorAttribute = typeAttribute.Length == 0
            ? "[Experimental(\"DBXEXP_COMPANION\")]\n        "
            : string.Empty;
        var referenced = CompileReference($$"""
            using DotBoxD.Services.Attributes;
            using DotBoxD.Services.Server;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;

            namespace ReferencedContracts;

            [RpcService]
            public interface ISub
            {
                Task<int> CountAsync();
            }

            {{typeAttribute}}public sealed class SubProxy : ISub
            {
                {{constructorAttribute}}public SubProxy(IRpcInvoker invoker, string instanceId)
                {
                }

                public Task<int> CountAsync() => Task.FromResult(0);
            }
            """);
        var compilation = CreateCompilation("""
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;
            using ReferencedContracts;

            namespace Consumer;

            [RpcService]
            public interface IRoot
            {
                Task<ISub> GetSubAsync();
            }
            """, referenced);

        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        AssertRootSubServiceUnsupported(runResult, compilation);
    }

    private static void AssertRootSubServiceUnsupported(
        GeneratorDriverRunResult runResult,
        CSharpCompilation compilation)
    {
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("global::ReferencedContracts.ISub") &&
            d.GetMessage().Contains("cannot be proxied because that service was not generated"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("ReferencedContracts.SubProxy");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"GetSubAsync\":");

        var finalCompilation = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }

    private static MetadataReference CompileReference(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ReferencedContracts_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, s_parseOptions)],
            references: CreateBaseReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        return MetadataReference.CreateFromImage(ms.ToArray());
    }

    private static CSharpCompilation CreateCompilation(string source, MetadataReference reference)
        => CSharpCompilation.Create(
            assemblyName: "MetadataOnlySubService_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, s_parseOptions)],
            references: CreateBaseReferences().Append(reference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IEnumerable<MetadataReference> CreateBaseReferences()
    {
        foreach (var reference in Basic.Reference.Assemblies.Net80.References.All)
        {
            yield return reference;
        }

        yield return MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Pipe).Assembly.Location);
    }
}
