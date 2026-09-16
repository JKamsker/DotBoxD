using System.IO.Pipelines;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public class CodeRequirementSubServiceCompanionTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Theory]
    [InlineData("System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute", "type")]
    [InlineData("System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute", "type")]
    [InlineData("System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute", "constructor")]
    [InlineData("System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute", "constructor")]
    public void MetadataOnlyCompanionWithCodeRequirement_BecomesUnsupportedStub(
        string attributeType,
        string target)
    {
        var runResult = Generate(attributeType, target);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("global::ReferencedContracts.ISub") &&
            d.GetMessage().Contains("cannot be proxied because that service was not generated"));

        AssertUnsupportedStub(runResult);
    }

    [Fact]
    public void MetadataOnlyCompanionWithoutCodeRequirement_RemainsAvailable()
    {
        var runResult = Generate(attributeType: null, target: null);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002");

        var proxy = GetGeneratedSource(runResult, "IRoot.DotBoxDRpcProxy.g.cs");
        proxy.Should().Contain("new global::ReferencedContracts.SubProxy");

        var dispatcher = GetGeneratedSource(runResult, "IRoot.DotBoxDRpcDispatcher.g.cs");
        dispatcher.Should().Contain("case \"GetSubAsync\":");
    }

    private static GeneratorDriverRunResult Generate(string? attributeType, string? target)
    {
        var referenced = CompileReference(CreateReferencedContracts(attributeType, target));
        var compilation = CreateCompilation(referenced);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        AssertCompiles(compilation, runResult);
        return runResult;
    }

    private static string CreateReferencedContracts(string? attributeType, string? target)
    {
        var typeAttribute = target == "type" ? $"[{attributeType}(\"manual companion\")]" : string.Empty;
        var constructorAttribute = target == "constructor" ? $"[{attributeType}(\"manual companion\")]" : string.Empty;

        return $$"""
            using DotBoxD.Services.Attributes;
            using DotBoxD.Services.Server;
            using System.Threading.Tasks;

            namespace ReferencedContracts;

            [RpcService]
            public interface ISub
            {
                Task<int> CountAsync();
            }

            {{typeAttribute}}
            public sealed class SubProxy : ISub
            {
                {{constructorAttribute}}
                public SubProxy(IRpcInvoker invoker, string instanceId)
                {
                }

                public Task<int> CountAsync() => Task.FromResult(0);
            }
            """;
    }

    private static void AssertUnsupportedStub(GeneratorDriverRunResult runResult)
    {
        var proxy = GetGeneratedSource(runResult, "IRoot.DotBoxDRpcProxy.g.cs");
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("new global::ReferencedContracts.SubProxy");

        var dispatcher = GetGeneratedSource(runResult, "IRoot.DotBoxDRpcDispatcher.g.cs");
        dispatcher.Should().NotContain("case \"GetSubAsync\":");
    }

    private static string GetGeneratedSource(GeneratorDriverRunResult runResult, string hintName) =>
        runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith(hintName))
            .SourceText.ToString();

    private static MetadataReference CompileReference(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ReferencedContracts_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, s_parseOptions)],
            references: CreateBaseReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assembly = new MemoryStream();
        var emit = compilation.Emit(assembly);
        emit.Success.Should().BeTrue(FormatErrors(emit.Diagnostics));
        return MetadataReference.CreateFromImage(assembly.ToArray());
    }

    private static CSharpCompilation CreateCompilation(MetadataReference referenced) =>
        CSharpCompilation.Create(
            assemblyName: "CodeRequirementSubService_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [CSharpSyntaxTree.ParseText("""
                using DotBoxD.Services.Attributes;
                using ReferencedContracts;
                using System.Threading.Tasks;

                namespace Consumer;

                [RpcService]
                public interface IRoot
                {
                    Task<ISub> GetSubAsync();
                }
                """, s_parseOptions)],
            references: CreateBaseReferences().Append(referenced),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static void AssertCompiles(CSharpCompilation compilation, GeneratorDriverRunResult runResult)
    {
        using var assembly = new MemoryStream();
        var emit = compilation.AddSyntaxTrees(runResult.GeneratedTrees).Emit(assembly);
        emit.Success.Should().BeTrue(FormatErrors(emit.Diagnostics));
    }

    private static string FormatErrors(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(
            "\n",
            diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));

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
