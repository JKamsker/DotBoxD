using System.IO.Pipelines;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public class NonConstructibleSubServiceCompanionTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Theory]
    [InlineData("public sealed class SubProxy<T> : ISub", "generic")]
    [InlineData("public abstract class SubProxy : ISub", "abstract")]
    public void RootMethodReturningMetadataOnlySubServiceWithNonConstructibleCompanion_BecomesUnsupportedStub(
        string companionDeclaration,
        string companionKind)
    {
        var referenced = CompileReference($$"""
            using DotBoxD.Services.Attributes;

            namespace ReferencedContracts;

            [RpcService]
            public interface ISub
            {
            }

            {{companionDeclaration}}
            {
                public SubProxy(global::DotBoxD.Services.Server.IRpcInvoker invoker, string instanceId)
                {
                }
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

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = compilation.AddSyntaxTrees(runResult.GeneratedTrees);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("global::ReferencedContracts.ISub") &&
            d.GetMessage().Contains("cannot be proxied because that service was not generated"),
            $"a {companionKind} companion cannot be constructed by generated code");

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("new global::ReferencedContracts.SubProxy");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"GetSubAsync\":");

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
            assemblyName: "NonConstructibleSubService_" + Guid.NewGuid().ToString("N"),
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
