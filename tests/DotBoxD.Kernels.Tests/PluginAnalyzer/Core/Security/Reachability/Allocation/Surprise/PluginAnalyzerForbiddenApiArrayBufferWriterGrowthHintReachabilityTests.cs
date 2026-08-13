namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiArrayBufferWriterGrowthHintReachabilityTests
{
    [Theory]
    [InlineData(
        "ArrayBufferWriter<byte>.GetMemory size hint",
        "GetMemory",
        "System.Buffers.ArrayBufferWriter<byte>")]
    [InlineData(
        "ArrayBufferWriter<byte>.GetSpan size hint",
        "GetSpan",
        "System.Buffers.ArrayBufferWriter<byte>")]
    [InlineData(
        "System.IO positive control",
        null,
        "System.IO.File")]
    public async Task Reports_unbounded_array_buffer_writer_growth_hint_static_initializer(
        string testCase,
        string? growthMethod,
        string expectedApi)
    {
        var source = CreateSource(growthMethod);

        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            source,
            "DotBoxDPluginAnalyzerArrayBufferWriterGrowthHintReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    private static string CreateSource(string? growthMethod)
        => growthMethod is null
            ? WrapSource("private static readonly bool Retained = System.IO.File.Exists(\"plugin.txt\");")
            : WrapSource(
                $$"""
                  private static readonly int Retained = CreateRetained();

                  private static int CreateRetained()
                  {
                      var writer = new System.Buffers.ArrayBufferWriter<byte>();
                      return writer.{{growthMethod}}(int.MaxValue).Length;
                  }
                  """);

    private static string WrapSource(string memberDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("array-buffer-writer-growth-hint-static-initializer")]
                public sealed class ArrayBufferWriterGrowthHintKernel : IEventKernel<string>
                {
                    {{memberDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
