using System.Collections.Immutable;
using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class ExplicitDtoUnionAttributeIdentityTests
{
    [Fact]
    public void ForeignJsonPolymorphismAttributes_ProduceDBXS002_AndSkipDispatch()
    {
        var foreignAttributes = CompileForeignAttributes();
        var compilation = CreateCompilation("""
            extern alias Foreign;

            using DotBoxD.Services.Attributes;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;

            namespace Regress.ExplicitDtoUnionAttributeIdentity
            {
                public sealed class ForeignEnvelope
                {
                    public ForeignWidget Value { get; init; } = null!;
                }

                [Foreign::System.Text.Json.Serialization.JsonPolymorphic]
                [Foreign::System.Text.Json.Serialization.JsonDerivedType(typeof(ForeignTextWidget), "text")]
                public abstract class ForeignWidget;

                public sealed class ForeignTextWidget : ForeignWidget;

                public sealed class RealEnvelope
                {
                    public RealWidget Value { get; init; } = null!;
                }

                [JsonPolymorphic]
                [JsonDerivedType(typeof(RealTextWidget), "text")]
                public abstract class RealWidget;

                public sealed class RealTextWidget : RealWidget;

                [RpcService]
                public interface IWidgetService
                {
                    Task SendForeignAsync(ForeignEnvelope request);
                    Task SendRealAsync(RealEnvelope request);
                }
            }
            """, foreignAttributes);

        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(compilation)
            .GetRunResult();

        runResult.Diagnostics.Should().ContainSingle(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("SendForeignAsync") &&
            d.GetMessage().Contains("abstract"));
        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("SendRealAsync"));

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IWidgetService.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"SendForeignAsync\":");
        dispatcher.Should().Contain("case \"SendRealAsync\":");
    }

    private static MetadataReference CompileForeignAttributes()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ForeignJsonPolymorphismAttributes",
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
        MetadataReference foreignAttributes)
    {
        var references = Basic.Reference.Assemblies.Net80.References.All
            .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location))
            .Append(foreignAttributes);

        return CSharpCompilation.Create(
            assemblyName: "ExplicitDtoUnionAttributeIdentity",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private const string ForeignAttributeSource = """
        namespace System.Text.Json.Serialization
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Interface)]
            public sealed class JsonPolymorphicAttribute : System.Attribute;

            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Interface, AllowMultiple = true)]
            public sealed class JsonDerivedTypeAttribute : System.Attribute
            {
                public JsonDerivedTypeAttribute(System.Type derivedType, string typeDiscriminator)
                {
                }
            }
        }
        """;
}
