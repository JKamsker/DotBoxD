using FluentAssertions;

using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation.MemberMetadata;

public sealed class ServiceMemberCodeRequirementAttributeTests
{
    [Fact]
    public void GeneratedServiceMembers_PreserveCodeRequirementAttributes()
    {
        var (_, runResult) = Run(ServiceSource);
        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.ServiceMemberCodeRequirements",
                "IRoot",
                GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString()
            .Replace("\r\n", "\n");
        var asyncSibling = generated
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcAsync.g.cs", StringComparison.Ordinal))
            .SourceText.ToString()
            .Replace("\r\n", "\n");

        const string trimmingAttribute =
            "[global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute(\"Trimming can remove contract types\", Url = \"https://example.test/trimming\")]";
        const string dynamicCodeAttribute =
            "[global::System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute(\"Dynamic code is required for this call\", Url = \"https://example.test/dynamic-code\")]";
        const string assemblyFilesAttribute =
            "[global::System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute(\"Files adjacent to the assembly are required\", Url = \"https://example.test/assembly-files\")]";
        const string parameterlessAssemblyFilesAttribute =
            "[global::System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute]";

        AssertMemberHasAttribute(
            proxy,
            trimmingAttribute,
            "public global::System.Threading.Tasks.Task TrimAsync()");
        AssertMemberHasAttribute(
            proxy,
            trimmingAttribute,
            "public global::System.Threading.Tasks.Task TrimAsync(global::System.Threading.CancellationToken ct = default)");
        AssertMemberHasAttribute(
            proxy,
            dynamicCodeAttribute,
            "public global::System.Threading.Tasks.Task DynamicCodeAsync()");
        AssertMemberHasAttribute(
            proxy,
            dynamicCodeAttribute,
            "public global::System.Threading.Tasks.Task DynamicCodeAsync(global::System.Threading.CancellationToken ct = default)");
        AssertMemberHasAttribute(
            proxy,
            assemblyFilesAttribute,
            "public global::System.Threading.Tasks.Task AssemblyFilesAsync()");
        AssertMemberHasAttribute(
            proxy,
            assemblyFilesAttribute,
            "public global::System.Threading.Tasks.Task AssemblyFilesAsync(global::System.Threading.CancellationToken ct = default)");
        AssertMemberHasAttribute(
            proxy,
            parameterlessAssemblyFilesAttribute,
            "public global::System.Threading.Tasks.Task ParameterlessAssemblyFilesAsync()");
        AssertMemberHasAttribute(
            proxy,
            parameterlessAssemblyFilesAttribute,
            "public global::System.Threading.Tasks.Task ParameterlessAssemblyFilesAsync(global::System.Threading.CancellationToken ct = default)");
        AssertMemberDoesNotHaveCodeRequirementAttribute(
            proxy,
            "public global::System.Threading.Tasks.Task ControlAsync()");
        AssertMemberDoesNotHaveCodeRequirementAttribute(
            proxy,
            "public global::System.Threading.Tasks.Task ControlAsync(global::System.Threading.CancellationToken ct = default)");

        AssertMemberHasAttribute(
            asyncSibling,
            trimmingAttribute,
            "global::System.Threading.Tasks.Task TrimAsync(global::System.Threading.CancellationToken ct = default);");
        AssertMemberHasAttribute(
            asyncSibling,
            dynamicCodeAttribute,
            "global::System.Threading.Tasks.Task DynamicCodeAsync(global::System.Threading.CancellationToken ct = default);");
        AssertMemberHasAttribute(
            asyncSibling,
            assemblyFilesAttribute,
            "global::System.Threading.Tasks.Task AssemblyFilesAsync(global::System.Threading.CancellationToken ct = default);");
        AssertMemberHasAttribute(
            asyncSibling,
            parameterlessAssemblyFilesAttribute,
            "global::System.Threading.Tasks.Task ParameterlessAssemblyFilesAsync(global::System.Threading.CancellationToken ct = default);");
        AssertMemberDoesNotHaveCodeRequirementAttribute(
            asyncSibling,
            "global::System.Threading.Tasks.Task ControlAsync(global::System.Threading.CancellationToken ct = default);");
    }

    private static void AssertMemberHasAttribute(string source, string attribute, string declaration)
    {
        source.Should().Contain($"{attribute}\n        {declaration}");
    }

    private static void AssertMemberDoesNotHaveCodeRequirementAttribute(string source, string declaration)
    {
        var declarationIndex = source.IndexOf(declaration, StringComparison.Ordinal);
        declarationIndex.Should().BeGreaterThanOrEqualTo(0);

        var declarationLineStart = source.LastIndexOf('\n', declarationIndex - 1) + 1;
        var attributeBlockStart = declarationLineStart;
        while (attributeBlockStart > 0)
        {
            var previousLineEnd = attributeBlockStart - 1;
            var previousLineStart = source.LastIndexOf('\n', previousLineEnd - 1) + 1;
            var previousLine = source.Substring(previousLineStart, previousLineEnd - previousLineStart).Trim();
            if (!previousLine.StartsWith("[", StringComparison.Ordinal))
            {
                break;
            }

            attributeBlockStart = previousLineStart;
        }

        var attributeBlock = source.Substring(attributeBlockStart, declarationLineStart - attributeBlockStart);
        attributeBlock.Should().NotContain("RequiresUnreferencedCodeAttribute")
            .And.NotContain("RequiresDynamicCodeAttribute")
            .And.NotContain("RequiresAssemblyFilesAttribute");
    }

    private const string ServiceSource = """
        using DotBoxD.Services.Attributes;
        using System.Diagnostics.CodeAnalysis;
        using System.Threading.Tasks;

        namespace Regress.ServiceMemberCodeRequirements
        {
            [RpcService]
            public interface IRoot
            {
                [RequiresUnreferencedCode("Trimming can remove contract types", Url = "https://example.test/trimming")]
                Task TrimAsync();

                [RequiresDynamicCode("Dynamic code is required for this call", Url = "https://example.test/dynamic-code")]
                Task DynamicCodeAsync();

                [RequiresAssemblyFiles("Files adjacent to the assembly are required", Url = "https://example.test/assembly-files")]
                Task AssemblyFilesAsync();

                [RequiresAssemblyFiles]
                Task ParameterlessAssemblyFilesAsync();

                Task ControlAsync();
            }
        }
        """;
}
