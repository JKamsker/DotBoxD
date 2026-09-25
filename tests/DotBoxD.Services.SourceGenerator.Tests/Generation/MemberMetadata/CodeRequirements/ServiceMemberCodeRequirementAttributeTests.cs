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
        AssertMemberDoesNotHaveCodeRequirementAttribute(
            proxy,
            "public global::System.Threading.Tasks.Task ControlAsync()");

        AssertMemberHasAttribute(
            asyncSibling,
            trimmingAttribute,
            "global::System.Threading.Tasks.Task TrimAsync(global::System.Threading.CancellationToken ct = default);");
        AssertMemberHasAttribute(
            asyncSibling,
            dynamicCodeAttribute,
            "global::System.Threading.Tasks.Task DynamicCodeAsync(global::System.Threading.CancellationToken ct = default);");
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

        var previousLineEnd = source.LastIndexOf('\n', declarationIndex - 1);
        var previousLineStart = source.LastIndexOf('\n', previousLineEnd - 1) + 1;
        var previousLine = source.Substring(previousLineStart, previousLineEnd - previousLineStart).Trim();
        previousLine.Should().NotContain("RequiresUnreferencedCodeAttribute")
            .And.NotContain("RequiresDynamicCodeAttribute");
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

                Task ControlAsync();
            }
        }
        """;
}
