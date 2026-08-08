namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiBitArrayLengthReachabilityTests
{
    [Theory]
    [InlineData(
        "BitArray length",
        "private static readonly System.Collections.BitArray Retained = new(int.MaxValue);",
        "System.Collections.BitArray")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin-bitarray.txt\");",
        "System.IO.File")]
    public async Task Reports_unbounded_bit_array_length_static_initializer(
        string testCase,
        string staticMember,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(staticMember),
            "DotBoxDPluginAnalyzerBitArrayLengthReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains(expectedApi, StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    [Fact]
    public async Task Allows_zero_length_bit_array_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly System.Collections.BitArray Retained = new(0);"),
            "DotBoxDPluginAnalyzerZeroLengthBitArrayReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string staticMember)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("bitarray-length-static-initializer")]
                public sealed class BitArrayLengthKernel : IEventKernel<string>
                {
                    {{staticMember}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

}
