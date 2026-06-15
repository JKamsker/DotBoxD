using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Tests.Coverage;

/// <summary>
/// Round 7 regression (deferred R6 finding #7). An explicitly configured empty wire name was accepted with
/// no build-time diagnostic. <c>[DotBoxDService(Name = "")]</c> compiled but every dispatch failed at
/// runtime (the empty name never matches), and <c>[DotBoxDMethod(Name = "")]</c> threw
/// <c>ArgumentException</c> on the first call. An empty/whitespace wire name must be rejected at build time:
/// DBXS003 for the service, DBXS002 for the method.
/// </summary>
public sealed class Round7_EmptyWireNameDiagnosticTests
{
    [Fact]
    public void Generator_ReportsError_ForEmptyServiceWireName()
    {
        const string source = @"
using DotBoxD.Services.Attributes;
using System.Threading.Tasks;

namespace Bug.EmptyServiceName
{
    [DotBoxDService(Name = """")]
    public interface IEmptyName
    {
        Task<int> GetAsync();
    }
}";
        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(GeneratorTestHelper.CreateCompilation(source))
            .GetRunResult();

        Assert.Contains(
            runResult.Diagnostics,
            d => d.Id == "DBXS003" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_ReportsError_ForEmptyMethodWireName()
    {
        const string source = @"
using DotBoxD.Services.Attributes;
using System.Threading.Tasks;

namespace Bug.EmptyMethodName
{
    [DotBoxDService]
    public interface IEmptyMethod
    {
        [DotBoxDMethod(Name = """")]
        Task<int> GetAsync();
    }
}";
        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(GeneratorTestHelper.CreateCompilation(source))
            .GetRunResult();

        Assert.Contains(
            runResult.Diagnostics,
            d => d.Id == "DBXS002" && d.Severity == DiagnosticSeverity.Error);
    }
}
