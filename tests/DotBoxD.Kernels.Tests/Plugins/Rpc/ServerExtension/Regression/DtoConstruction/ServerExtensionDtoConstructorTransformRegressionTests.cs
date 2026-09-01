using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDtoConstructorTransformRegressionTests
{
    [Fact]
    public void Server_extension_rejects_dto_constructor_that_transforms_matching_member()
        => AssertConstructorRejected("""
                public Score(int value)
                {
                    Value = value + 1;
                }
            """);

    [Fact]
    public void Server_extension_rejects_dto_constructor_that_omits_matching_member_assignment()
        => AssertConstructorRejected("""
                public Score(int value)
                {
                }
            """);

    [Fact]
    public void Server_extension_rejects_dto_constructor_that_assigns_matching_member_conditionally()
        => AssertConstructorRejected("""
                public Score(int value)
                {
                    if (value >= 0)
                    {
                        Value = value;
                    }
                }
            """);

    [Fact]
    public void Server_extension_rejects_dto_constructor_that_delegates_matching_member_assignment()
        => AssertConstructorRejected("""
                public Score()
                {
                    Value = 0;
                }

                public Score(int value)
                    : this()
                {
                }
            """);

    [Fact]
    public void Constructor_assignment_verification_handles_symbol_from_another_compilation()
    {
        var sourceTree = CSharpSyntaxTree.ParseText("""
            public sealed class Score
            {
                public Score(int value)
                {
                    Value = value;
                }

                public int Value { get; }
            }
            """);
        var sourceCompilation = CSharpCompilation.Create(
            "Source",
            [sourceTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var type = Assert.IsAssignableFrom<INamedTypeSymbol>(
            sourceCompilation.GetTypeByMetadataName("Score"));
        var constructor = Assert.Single(type.InstanceConstructors, candidate => candidate.Parameters.Length == 1);
        var property = Assert.IsAssignableFrom<IPropertySymbol>(
            type.GetMembers("Value").Single());
        var member = new RecordMember(property.Name, property.Type, property);
        var otherCompilation = sourceCompilation
            .RemoveAllSyntaxTrees()
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(string.Empty));

        var preservesMember = RpcDtoConstructorAssignmentVerifier.ConstructorPreservesMember(
            constructor,
            member,
            constructor.Parameters[0],
            otherCompilation);

        Assert.True(preservesMember);
    }

    [Fact]
    public void Constructor_assignment_verification_treats_identity_cast_as_parameter_preserving()
    {
        var sourceTree = CSharpSyntaxTree.ParseText("""
            public sealed class Score
            {
                public Score(int value)
                {
                    Value = (int)value;
                }

                public int Value { get; }
            }
            """);
        var sourceCompilation = CSharpCompilation.Create(
            "Source",
            [sourceTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var type = Assert.IsAssignableFrom<INamedTypeSymbol>(
            sourceCompilation.GetTypeByMetadataName("Score"));
        var constructor = Assert.Single(type.InstanceConstructors, candidate => candidate.Parameters.Length == 1);
        var property = Assert.IsAssignableFrom<IPropertySymbol>(
            type.GetMembers("Value").Single());
        var member = new RecordMember(property.Name, property.Type, property);

        var preservesMember = RpcDtoConstructorAssignmentVerifier.ConstructorPreservesMember(
            constructor,
            member,
            constructor.Parameters[0],
            sourceCompilation);

        Assert.True(preservesMember);
    }

    [Fact]
    public void Constructor_assignment_verification_accepts_direct_assignment_after_safe_constructor_chain()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            public sealed class Score
            {
                public Score()
                {
                }

                public Score(int value)
                    : this()
                {
                    Value = value;
                }

                public int Value { get; }
            }
            """);
        var compilation = CSharpCompilation.Create(
            "Source",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var type = Assert.IsAssignableFrom<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName("Score"));
        var constructor = Assert.Single(type.InstanceConstructors, candidate => candidate.Parameters.Length == 1);
        var property = Assert.IsAssignableFrom<IPropertySymbol>(
            type.GetMembers("Value").Single());

        var preservesMember = RpcDtoConstructorAssignmentVerifier.ConstructorPreservesMember(
            constructor,
            new RecordMember(property.Name, property.Type, property),
            constructor.Parameters[0],
            compilation);

        Assert.True(preservesMember);
    }

    private static void AssertConstructorRejected(string constructor)
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator($$"""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class Score
            {
            {{constructor}}

                public int Value { get; }
            }

            [ServerExtension("score-transform")]
            public sealed partial class ScoreKernel
            {
                public Score Read(int value, HookContext ctx)
                {
                    return new Score(value);
                }
            }
            """);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                diagnostic.GetMessage().Contains("Score", StringComparison.Ordinal) &&
                diagnostic.GetMessage().Contains("constructor", StringComparison.Ordinal));
        Assert.Empty(result.GeneratedTrees);
    }
}
