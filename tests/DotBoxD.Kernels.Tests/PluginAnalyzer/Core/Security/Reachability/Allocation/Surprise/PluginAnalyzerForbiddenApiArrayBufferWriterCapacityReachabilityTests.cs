namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiArrayBufferWriterCapacityReachabilityTests
{
    [Theory]
    [InlineData(
        "ArrayBufferWriter initial capacity",
        "private static readonly System.Buffers.ArrayBufferWriter<byte> Retained = new(int.MaxValue);",
        "Retained.WrittenCount >= 0",
        "System.Buffers.ArrayBufferWriter<byte>")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Retained = System.IO.File.Exists(\"/x\");",
        "Retained",
        "System.IO.File")]
    public async Task Reports_unbounded_array_buffer_writer_capacity_static_initializer(
        string testCase,
        string fieldDeclaration,
        string predicate,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(fieldDeclaration, predicate),
            "DotBoxDPluginAnalyzerArrayBufferWriterCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    private static string Source(string fieldDeclaration, string predicate)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("array-buffer-writer-capacity-reachability")]
                public sealed class ArrayBufferWriterCapacityKernel : IEventKernel<string>
                {
                    {{fieldDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => {{predicate}};

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}
