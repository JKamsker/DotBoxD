using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

/// <summary>
/// Negative-path tests: malformed user code must never crash the generator and
/// degenerate-but-valid services must still produce compilable output.
/// </summary>
public class DiagnosticTests
{
    [Fact]
    public void EmptyServiceInterface_StillGeneratesCompilableOutput()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Diag.Empty
            {
                [ShaRpcService]
                public interface IEmpty
                {
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // No SHARPC001 errors.
        runResult.Diagnostics.Should().NotContain(d => d.Id == "SHARPC001" && d.Severity == DiagnosticSeverity.Error);

        var hints = runResult.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("IEmpty.ShaRpcProxy.g.cs");
        hints.Should().Contain("IEmpty.ShaRpcDispatcher.g.cs");
        hints.Should().Contain("ShaRpcExtensions.g.cs");

        // The combined compilation should emit successfully.
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue("empty service should still emit successfully. Errors: " +
            string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
    }

    [Fact]
    public void ServiceWithUnresolvableMethodSignature_DoesNotCrashGenerator()
    {
        // This source is broken (UnknownType doesn't exist). The generator should still
        // complete without throwing and either skip or emit a diagnostic - it must not bubble
        // an exception through the driver.
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Diag.Broken
            {
                [ShaRpcService]
                public interface IBroken
                {
                    Task<UnknownType> DoSomethingAsync(UnknownType input);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        // The driver should have completed without throwing. Generator-level diagnostics
        // (SHARPC001) may or may not appear, but at minimum we must not have an unhandled
        // exception surface as a driver error.
        runResult.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error && d.GetMessage().Contains("System.NullReferenceException"));
    }
}
