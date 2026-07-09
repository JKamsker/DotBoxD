using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation.AssemblyMetadata;

public sealed class ClsComplianceGeneratedSurfaceTests
{
    private static readonly HashSet<string> s_clsDiagnosticIds = ["CS3001", "CS3002", "CS3003"];

    [Fact]
    public void ClsCompliantAssembly_DoesNotReportGeneratedClsDiagnosticsForOrdinaryService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            [assembly: System.CLSCompliant(true)]

            namespace Regress.ClsCompliantServices
            {
                [RpcService]
                public interface ILegacyNumbers
                {
                    int GetValue();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var output = new MemoryStream();
        var emitResult = finalCompilation.Emit(output);

        var generatedClsDiagnostics = emitResult.Diagnostics
            .Where(d => s_clsDiagnosticIds.Contains(d.Id))
            .Where(d => d.Location.SourceTree is not null &&
                runResult.GeneratedTrees.Contains(d.Location.SourceTree))
            .Select(d => d.ToString())
            .ToArray();

        generatedClsDiagnostics.Should().BeEmpty();
    }
}
