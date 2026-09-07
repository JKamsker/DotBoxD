using System.Collections.Immutable;
using System.IO.Pipelines;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public sealed class ExternAliasSubServiceCompanionIdentityTests
{
    private static readonly CSharpParseOptions s_parseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public void ReferencedSubServiceProxyWithForeignInvoker_BecomesUnsupportedStub()
    {
        var foreignInvokerReference = CompileForeignInvokerReference();
        var referencedContracts = CompileReferencedContracts(
            foreignInvokerReference.WithAliases(ImmutableArray.Create("Foreign")));
        var compilation = CreateCompilation(
            """
            using DotBoxD.Services.Attributes;
            using ReferencedContracts;
            using System.Threading.Tasks;

            namespace Consumer;

            [RpcService]
            public interface IRoot
            {
                Task<ISub> GetSubAsync();
            }
            """,
            referencedContracts,
            foreignInvokerReference);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("global::ReferencedContracts.ISub") &&
            d.GetMessage().Contains("cannot be proxied because that service was not generated"),
            "an assembly-distinct IRpcInvoker must not make a manual proxy companion available");

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(source => source.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText
            .ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("new global::ReferencedContracts.SubProxy");

        var final = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        final.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty("the unavailable companion must not leak foreign-invoker CS1503 errors");
    }

    private static MetadataReference CompileForeignInvokerReference()
        => CompileReference(
            """
            namespace DotBoxD.Services.Server;

            public interface IRpcInvoker
            {
            }
            """,
            "Foreign.Invoker");

    private static MetadataReference CompileReferencedContracts(MetadataReference foreignInvokerReference)
        => CompileReference(
            """
            extern alias Foreign;

            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace ReferencedContracts;

            [RpcService]
            public interface ISub
            {
                Task<int> CountAsync();
            }

            public sealed class SubProxy : ISub
            {
                public SubProxy(Foreign::DotBoxD.Services.Server.IRpcInvoker invoker, string instanceId)
                {
                }

                public Task<int> CountAsync() => Task.FromResult(0);
            }
            """,
            "Referenced.Contracts",
            foreignInvokerReference);

    private static MetadataReference CompileReference(
        string source,
        string assemblyName,
        params MetadataReference[] references)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, s_parseOptions)],
            CreateBaseReferences().Concat(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assembly = new MemoryStream();
        var emit = compilation.Emit(assembly);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        return MetadataReference.CreateFromImage(assembly.ToArray());
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        MetadataReference referencedContracts,
        MetadataReference foreignInvokerReference)
        => CSharpCompilation.Create(
            "ExternAliasSubServiceCompanionIdentity_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, s_parseOptions)],
            CreateBaseReferences().Append(referencedContracts).Append(foreignInvokerReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

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
