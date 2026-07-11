using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer;

internal static class PluginGeneratorAssert
{
    private static readonly string[] SourceGeneratorFailureIds = ["DBXK117", "CS8784", "CS8785"];

    public static GeneratorDriverRunResult NoUnexpectedSourceGeneratorFailures(GeneratorDriverRunResult result)
    {
        NoUnexpectedSourceGeneratorFailures(result.Diagnostics);

        var exceptions = result.Results
            .Where(static item => item.Exception is not null)
            .Select(static item => item.Exception!)
            .ToArray();
        Assert.True(
            exceptions.Length == 0,
            "Unexpected source generator exception(s):" + Environment.NewLine +
            string.Join(Environment.NewLine, exceptions.Select(static exception => exception.ToString())));

        return result;
    }

    public static void NoUnexpectedSourceGeneratorFailures(IEnumerable<Diagnostic> diagnostics)
    {
        var failures = diagnostics
            .Where(IsUnexpectedSourceGeneratorFailure)
            .ToArray();
        Assert.True(
            failures.Length == 0,
            "Unexpected source generator failure diagnostic(s):" + Environment.NewLine +
            string.Join(Environment.NewLine, failures.Select(static diagnostic => diagnostic.ToString())));
    }

    private static bool IsUnexpectedSourceGeneratorFailure(Diagnostic diagnostic)
        => SourceGeneratorFailureIds.Contains(diagnostic.Id, StringComparer.Ordinal);
}
