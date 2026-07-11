namespace DotBoxD.Architecture.Tests;

/// <summary>
/// Maintains the readability ratchet for shipping source. The limit is intentionally per executable block
/// rather than per file: large tables and dispatchers are acceptable only when the individual control-flow
/// paths stay straightforward.
/// </summary>
public sealed class CyclomaticComplexityTests
{
    [Fact]
    public void Shipping_source_executable_blocks_stay_at_or_below_complexity_limit()
    {
        var root = ArchTestSupport.RepositoryRoot();
        var srcRoot = Path.Combine(root, "src");
        var offenders = CyclomaticComplexityAnalyzer
            .AnalyzeSourceTree(root, srcRoot)
            .Where(block => block.Complexity > 10)
            .OrderByDescending(block => block.Complexity)
            .ThenBy(block => block.File, StringComparer.Ordinal)
            .ThenBy(block => block.StartLine)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Shipping source blocks must have cyclomatic complexity <= 10. Offenders:\n"
            + string.Join("\n", offenders.Select(FormatOffender)));
    }

    private static string FormatOffender(ComplexityBlock block)
        => $"{block.File}:{block.StartLine} CC={block.Complexity} {block.Kind} {block.Name}";
}
