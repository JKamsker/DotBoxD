using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class SystemNamespaceDtoPayloadValidationTests
{
    [Fact]
    public void UserDefinedDtosInSystemNamespaces_ValidateUnsupportedMembers()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.IO;
            using System.Threading.Tasks;

            namespace System.Custom
            {
                public sealed class HiddenPayload
                {
                    public Stream Data { get; init; } = Stream.Null;
                }
            }

            namespace UserCode
            {
                public sealed class ControlPayload
                {
                    public Stream Data { get; init; } = Stream.Null;
                }

                [RpcService]
                public interface IPayloadService
                {
                    Task<int> SendHiddenAsync(System.Custom.HiddenPayload payload);
                    Task<int> SendControlAsync(ControlPayload payload);
                }
            }
            """;

        var runResult = Compile(source);

        var diagnostics = runResult.Diagnostics.Where(diagnostic => diagnostic.Id == "DBXS002").ToArray();
        diagnostics.Should().HaveCount(2);
        diagnostics.Should().OnlyContain(diagnostic =>
            diagnostic.GetMessage().Contains("streaming or control type", StringComparison.Ordinal));
    }

    private static GeneratorDriverRunResult Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var stream = new MemoryStream();
        var emit = finalCompilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString())));

        return runResult;
    }
}
