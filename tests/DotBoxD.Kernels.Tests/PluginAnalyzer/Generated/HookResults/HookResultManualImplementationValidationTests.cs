using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookResultManualImplementationValidationTests
{
    [Fact]
    public void Generic_manual_hook_result_still_reports_DBXK112()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [HookResult]
            public readonly record struct ManualResult<T>(bool Success, string? Reason, T Value) : IHookResult;
            """;

        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "DBXK112" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage().Contains("generic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FileLocal_manual_hook_result_still_reports_focused_diagnostic()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [HookResult]
            file readonly record struct ManualResult(bool Success, string? Reason, int Value) : IHookResult;
            """;

        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == "DBXK100" &&
            diagnostic.Severity == DiagnosticSeverity.Error &&
            diagnostic.GetMessage().Contains("file-local", StringComparison.OrdinalIgnoreCase));
    }
}
