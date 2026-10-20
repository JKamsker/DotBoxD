using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;

using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class ForeignMemberAttributeIdentityTests
{
    [Theory]
    [InlineData("System", "Obsolete")]
    [InlineData("System.Diagnostics.CodeAnalysis", "RequiresDynamicCode")]
    [InlineData("System.Diagnostics.CodeAnalysis", "RequiresUnreferencedCode")]
    [InlineData("System.Diagnostics.CodeAnalysis", "RequiresAssemblyFiles")]
    public void Globally_resolved_attribute_definitions_remain_supported(string ns, string name)
    {
        var (final, _) = Run($$"""
            namespace {{ns}}
            {
                public sealed class {{name}}Attribute : System.Attribute
                {
                    public {{name}}Attribute(string message) { }
                }
            }
            namespace BackportedMembers
            {
                [DotBoxD.Services.Attributes.RpcService]
                public interface IRoot
                {
                    [{{ns}}.{{name}}("custom annotation")]
                    System.Threading.Tasks.Task CallAsync();
                }
            }
            """);
        AssertCompiles(final);
        var expected = final.GetTypeByMetadataName(ns + "." + name + "Attribute");
        foreach (var typeName in new[] { "BackportedMembers.RootProxy", "BackportedMembers.IRootAsync" })
        {
            var methods = final.GetTypeByMetadataName(typeName)!.GetMembers("CallAsync");
            methods.Should().NotBeEmpty();
            foreach (var method in methods)
            {
                method.GetAttributes().Should().ContainSingle(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, expected));
            }
        }
    }

    [Theory]
    [InlineData("System", "Obsolete")]
    [InlineData("System.Diagnostics.CodeAnalysis", "RequiresDynamicCode")]
    [InlineData("System.Diagnostics.CodeAnalysis", "RequiresUnreferencedCode")]
    [InlineData("System.Diagnostics.CodeAnalysis", "RequiresAssemblyFiles")]
    [InlineData("System.Runtime.Versioning", "SupportedOSPlatform")]
    [InlineData("System.Runtime.Versioning", "UnsupportedOSPlatform")]
    [InlineData("System.Runtime.Versioning", "ObsoletedOSPlatform")]
    public void Foreign_member_attributes_do_not_become_framework_annotations(string ns, string name)
    {
        var foreign = GeneratorTestHelper.CreateCompilation($$"""
            namespace {{ns}};
            public sealed class {{name}}Attribute : System.Attribute
            {
                public {{name}}Attribute(string message) { }
            }
            """);
        using var image = new MemoryStream();
        foreign.Emit(image).Success.Should().BeTrue();
        var reference = MetadataReference.CreateFromImage(image.ToArray())
            .WithAliases(ImmutableArray.Create("Foreign"));
        var compilation = GeneratorTestHelper.CreateCompilation($$"""
            extern alias Foreign;
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;
            namespace ForeignMembers;
            [RpcService] public interface IChild { Task PingAsync(); }
            [RpcService] public interface IRoot
            {
                [Foreign::{{ns}}.{{name}}("custom annotation")]
                Task CallAsync();
                [Foreign::{{ns}}.{{name}}("custom annotation")]
                IChild Child { get; }
            }
            """).AddReferences(reference);
        var result = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();
        var final = compilation.AddSyntaxTrees(result.GeneratedTrees);
        AssertCompiles(final);

        var frameworkAttribute = final.GetTypeByMetadataName(ns + "." + name + "Attribute");
        foreach (var typeName in new[] { "ForeignMembers.RootProxy", "ForeignMembers.IRootAsync" })
        {
            var type = final.GetTypeByMetadataName(typeName);
            type.Should().NotBeNull();
            foreach (var memberName in typeName.EndsWith("Proxy", StringComparison.Ordinal)
                ? new[] { "CallAsync", "Child" } : new[] { "CallAsync" })
            {
                var members = type!.GetMembers(memberName);
                members.Should().NotBeEmpty();
                foreach (var member in members)
                {
                    member.GetAttributes().Should().NotContain(a =>
                        SymbolEqualityComparer.Default.Equals(a.AttributeClass, frameworkAttribute));
                }
            }
        }
    }
}
