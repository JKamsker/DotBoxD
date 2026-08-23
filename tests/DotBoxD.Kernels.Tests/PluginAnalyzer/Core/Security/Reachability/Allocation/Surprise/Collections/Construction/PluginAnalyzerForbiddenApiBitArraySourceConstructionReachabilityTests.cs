namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiBitArraySourceConstructionReachabilityTests
{
    [Theory]
    [InlineData(
        "BitArray copy",
        "private static readonly System.Collections.BitArray Retained = new(new System.Collections.BitArray(0));")]
    [InlineData(
        "bool array copy",
        "private static readonly System.Collections.BitArray Retained = Copy(System.Array.Empty<bool>());\n\nprivate static System.Collections.BitArray Copy(bool[] source) => new(source);")]
    [InlineData(
        "byte array copy",
        "private static readonly System.Collections.BitArray Retained = Copy(System.Array.Empty<byte>());\n\nprivate static System.Collections.BitArray Copy(byte[] source) => new(source);")]
    [InlineData(
        "int array copy",
        "private static readonly System.Collections.BitArray Retained = Copy(System.Array.Empty<int>());\n\nprivate static System.Collections.BitArray Copy(int[] source) => new(source);")]
    public async Task Reports_unbounded_bit_array_source_constructor_static_initializer(
        string testCase,
        string memberDeclaration)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(memberDeclaration),
            $"DotBoxDPluginAnalyzerBitArray{testCase.Replace(" ", string.Empty, StringComparison.Ordinal)}ConstructionReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.BitArray", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_zero_length_bit_array_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly System.Collections.BitArray Retained = new(0);"),
            "DotBoxDPluginAnalyzerZeroLengthBitArraySourceConstructionReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string memberDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("bitarray-source-construction-reachability")]
                public sealed class BitArraySourceConstructionKernel : IEventKernel<string>
                {
                    {{memberDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
