using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures.InheritedFlow;

public sealed class InheritedFlowAttributeIdentityTests
{
    [Fact]
    public void DuplicateInheritedMethodsWithForeignMaybeNullAttribute_AcceptService()
    {
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.InheritedForeignMaybeNull
            {
                public interface ILeft
                {
                    Task<string> EchoAsync(
                        [Foreign::System.Diagnostics.CodeAnalysis.MaybeNull] string value);
                }

                public interface IRight
                {
                    Task<string> EchoAsync(string value);
                }

                [RpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """);

        var runResult = Run(compilation);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        runResult.Results.Single().GeneratedSources
            .Should().Contain(g => g.HintName.Contains("ICombined.", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateInheritedMethodsWithFrameworkMaybeNullAttribute_RejectService()
    {
        var compilation = GeneratorTestHelper.CreateCompilation("""
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;

            namespace Regress.InheritedFrameworkMaybeNull
            {
                public interface ILeft
                {
                    Task<string> EchoAsync([MaybeNull] string value);
                }

                public interface IRight
                {
                    Task<string> EchoAsync(string value);
                }

                [RpcService]
                public interface ICombined : ILeft, IRight
                {
                }
            }
            """);

        var runResult = Run(compilation);

        compilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("flow attributes", StringComparison.Ordinal));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("ICombined.", StringComparison.Ordinal));
    }

    private static GeneratorDriverRunResult Run(CSharpCompilation compilation) =>
        GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

    private static CSharpCompilation CreateCompilation(string source)
    {
        var foreignAttributes = CompileForeignAttributes();
        var references = Basic.Reference.Assemblies.Net80.References.All
            .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
            .Append(foreignAttributes);

        return CSharpCompilation.Create(
            assemblyName: "InheritedFlowAttributeIdentity",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static MetadataReference CompileForeignAttributes()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ForeignInheritedFlowAttributes",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(ForeignAttributeSource) },
            references: Basic.Reference.Assemblies.Net80.References.All,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        return MetadataReference.CreateFromImage(stream.ToArray())
            .WithAliases(ImmutableArray.Create("Foreign"));
    }

    private const string ForeignAttributeSource = """
        namespace System.Diagnostics.CodeAnalysis
        {
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class MaybeNullAttribute : System.Attribute;
        }
        """;
}
