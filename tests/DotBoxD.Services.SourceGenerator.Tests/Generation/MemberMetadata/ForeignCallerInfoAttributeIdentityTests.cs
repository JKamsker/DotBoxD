using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class ForeignCallerInfoAttributeIdentityTests
{
    [Fact]
    public void ForeignCallerMemberNameAttribute_IsNotPreservedInGeneratedServiceSurface()
    {
        var foreignAttribute = CompileForeignAttribute();
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.ForeignCallerInfoAttributeIdentity
            {
                [RpcService]
                public interface ITraceService
                {
                    Task TraceAsync(
                        [Foreign::System.Runtime.CompilerServices.CallerMemberName] string member = "fallback");
                }
            }
            """, foreignAttribute);

        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var final = compilation.AddSyntaxTrees(runResult.GeneratedTrees);

        final.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.ForeignCallerInfoAttributeIdentity", "ITraceService", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        var asyncSibling = generated
            .Single(g => g.HintName.EndsWith("ITraceService.DotBoxDRpcAsync.g.cs", StringComparison.Ordinal))
            .SourceText.ToString();

        const string callerMemberNameAttribute =
            "[global::System.Runtime.CompilerServices.CallerMemberNameAttribute]";
        proxy.Should().NotContain(callerMemberNameAttribute);
        asyncSibling.Should().NotContain(callerMemberNameAttribute);
    }

    private static MetadataReference CompileForeignAttribute()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ForeignCallerInfoAttribute",
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

    private static CSharpCompilation CreateCompilation(
        string source,
        MetadataReference foreignAttribute) =>
        CSharpCompilation.Create(
            assemblyName: "ForeignCallerInfoAttributeIdentity",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: Basic.Reference.Assemblies.Net80.References.All
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
                .Append(foreignAttribute),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private const string ForeignAttributeSource = """
        namespace System.Runtime.CompilerServices
        {
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class CallerMemberNameAttribute : System.Attribute;
        }
        """;
}
