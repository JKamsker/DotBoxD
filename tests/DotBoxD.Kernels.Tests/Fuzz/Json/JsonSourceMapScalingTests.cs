using System.Diagnostics;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Tests.Fuzz.Json;

/// <summary>
/// The source map indexes every container before schema validation, so a budget-sized payload
/// made of many identically shaped containers must not degrade into quadratic key comparisons.
/// </summary>
public sealed class JsonSourceMapScalingTests
{
    [Theory]
    [InlineData("[]", 4)]
    [InlineData("""{"var":"x"}""", 2)]
    public void Many_identical_containers_import_in_linear_time(string item, int groups)
    {
        var group = "[" + string.Join(',', Enumerable.Repeat(item, 10_000)) + "]";
        var json = "[" + string.Join(',', Enumerable.Repeat(group, groups)) + "]";

        var stopwatch = Stopwatch.StartNew();
        var exception = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(json));
        stopwatch.Stop();

        Assert.Contains(exception.Diagnostics, d => d.Code == "E-JSON-TYPE");
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(3),
            $"Importing {groups * 10_000} identical containers took {stopwatch.Elapsed}.");
    }
}
