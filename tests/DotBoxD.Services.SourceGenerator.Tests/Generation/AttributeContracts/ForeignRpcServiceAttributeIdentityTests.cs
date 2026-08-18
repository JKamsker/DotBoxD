using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class ForeignRpcServiceAttributeIdentityTests
{
    [Fact]
    public void ForeignRpcServiceAttribute_DoesNotOptInterfaceIntoServiceGeneration()
    {
        var foreignAttribute = CompileForeignRpcServiceAttribute();
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.ForeignRpcServiceAttributeIdentity
            {
                [Foreign::DotBoxD.Services.Attributes.RpcService]
                public interface IForeignService
                {
                    Task<int> PingAsync();
                }

                [RpcService]
                public interface IRealService
                {
                    Task<int> PingAsync();
                }
            }
            """, foreignAttribute);

        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(compilation)
            .GetRunResult();
        var sources = runResult.Results.Single().GeneratedSources
            .ToDictionary(source => source.HintName, source => source.SourceText.ToString(), StringComparer.Ordinal);

        sources.Keys.Should().NotContain(hintName => hintName.Contains("IForeignService", StringComparison.Ordinal));
        sources.Keys.Should().Contain(GeneratorTestHelper.HintName(
            "Regress.ForeignRpcServiceAttributeIdentity",
            "IRealService",
            GeneratorTestHelper.GeneratedKind.Proxy));
        sources.Keys.Should().Contain(GeneratorTestHelper.HintName(
            "Regress.ForeignRpcServiceAttributeIdentity",
            "IRealService",
            GeneratorTestHelper.GeneratedKind.Dispatcher));
        sources.Keys.Should().Contain(GeneratorTestHelper.HintName(
            "Regress.ForeignRpcServiceAttributeIdentity",
            "IRealService",
            GeneratorTestHelper.GeneratedKind.Async));
        sources["DotBoxDGenerated.g.cs"].Should().NotContain("IForeignService");
        sources["DotBoxDGenerated.g.cs"].Should().Contain("IRealService");
    }

    private static MetadataReference CompileForeignRpcServiceAttribute()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ForeignRpcServiceAttribute",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(ForeignAttributeSource) },
            references: Basic.Reference.Assemblies.Net80.References.All,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join(
            Environment.NewLine,
            emit.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString())));

        return MetadataReference.CreateFromImage(stream.ToArray())
            .WithAliases(ImmutableArray.Create("Foreign"));
    }

    private static CSharpCompilation CreateCompilation(string source, MetadataReference foreignAttribute)
    {
        var references = Basic.Reference.Assemblies.Net80.References.All
            .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
            .Append(foreignAttribute);

        return CSharpCompilation.Create(
            assemblyName: "ForeignRpcServiceAttributeIdentity",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private const string ForeignAttributeSource = """
        namespace DotBoxD.Services.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Interface)]
            public sealed class RpcServiceAttribute : System.Attribute;
        }
        """;
}
