using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures.InheritedCallerInfo;

public sealed class InheritedCallerInfoAttributeIdentityTests
{
    [Fact]
    public void DuplicateInheritedMethodsWithForeignCallerMemberNameAttribute_AcceptService()
    {
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;

            namespace Regress.InheritedForeignCallerMemberName
            {
                public interface ILeft
                {
                    void Trace([Foreign::System.Runtime.CompilerServices.CallerMemberName] string member = "");
                }

                public interface IRight
                {
                    void Trace(string member = "");
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
    public void DuplicateInheritedMethodsWithFrameworkCallerMemberNameAttribute_RejectService()
    {
        var compilation = GeneratorTestHelper.CreateCompilation("""
            using DotBoxD.Services.Attributes;
            using System.Runtime.CompilerServices;

            namespace Regress.InheritedFrameworkCallerMemberName
            {
                public interface ILeft
                {
                    void Trace([CallerMemberName] string member = "");
                }

                public interface IRight
                {
                    void Trace(string member = "");
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
            d.GetMessage().Contains("caller info attributes", StringComparison.Ordinal));
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
            assemblyName: "InheritedCallerInfoAttributeIdentity",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static MetadataReference CompileForeignAttributes()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ForeignInheritedCallerInfoAttributes",
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
        namespace System.Runtime.CompilerServices
        {
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class CallerMemberNameAttribute : System.Attribute;
        }
        """;
}
